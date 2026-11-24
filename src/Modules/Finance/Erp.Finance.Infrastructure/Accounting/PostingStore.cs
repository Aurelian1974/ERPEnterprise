using System.Data;
using System.Text.Json;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Accounting;
using Erp.Finance.Contracts.Accounting;
using Erp.Finance.Domain.Accounting;

namespace Erp.Finance.Infrastructure.Accounting;

/// <summary>Contarea prin fin.usp_PostingModel_Load, fin.usp_AccountResolution_LoadContext, fin.usp_JournalEntry_Insert, fin.usp_PostingQueue_Upsert (NET-06).</summary>
public sealed class PostingStore(IDbSession session) : IPostingStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PostingModelDef?> LoadModelAsync(string eventType, DateOnly onDate, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_PostingModel_Load", new { EventType = eventType, OnDate = Day(onDate) }, ct))
            .ConfigureAwait(false);
        var model = (await grid.ReadAsync<ModelRow>().ConfigureAwait(false)).SingleOrDefault();
        var variants = (await grid.ReadAsync<VariantRow>().ConfigureAwait(false)).ToList();
        var lines = (await grid.ReadAsync<LineRow>().ConfigureAwait(false)).ToLookup(l => l.VariantId);
        if (model is null)
        {
            return null;
        }

        return new PostingModelDef(model.EventType, model.JournalId, variants.Select(v => new VariantDef(v.Id, v.Priority, v.Name, Flags(v.RequiredFlags),
            Flags(v.ForbiddenFlags), lines[v.Id].Select(l => new ModelLineDef(l.LineNumber, l.Component, l.Side, l.AccountKey, l.DefaultAccountCode, l.Sign,
                l.FilterVatCategory, l.FilterOperationTypeCode, l.CopyDimensions, l.GroupBy, l.DescriptionTemplate)).ToList(),
            DateOnly.FromDateTime(v.ValidFrom), v.ValidTo is { } to ? DateOnly.FromDateTime(to) : null, v.PartnerKind, v.DocumentType)).ToList());
    }

    public async Task<ResolutionData> LoadContextAsync(int companyId, DateOnly onDate, IReadOnlyCollection<int> partnerIds, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_AccountResolution_LoadContext", new
        {
            CompanyId = companyId, OnDate = Day(onDate), Partners = JsonSerializer.Serialize(partnerIds),
        }, ct)).ConfigureAwait(false);
        var roles = (await grid.ReadAsync<RoleRow>().ConfigureAwait(false)).ToList();
        var treasury = (await grid.ReadAsync<(int Id, string Code)>().ConfigureAwait(false)).ToDictionary(x => x.Id, x => x.Code);
        var operations = (await grid.ReadAsync<(string Code, string Account)>().ConfigureAwait(false))
            .ToDictionary(x => x.Code, x => x.Account, StringComparer.OrdinalIgnoreCase);
        var expenses = (await grid.ReadAsync<(string Code, string Account)>().ConfigureAwait(false))
            .ToDictionary(x => x.Code, x => x.Account, StringComparer.OrdinalIgnoreCase);
        var partners = (await grid.ReadAsync<(int Id, byte Kind)>().ConfigureAwait(false)).ToDictionary(x => x.Id, x => x.Kind);
        var currency = (await grid.ReadAsync<(string Code, byte Mode)>().ConfigureAwait(false)).ToDictionary(x => x.Code.ToUpperInvariant(), x => x.Mode);
        return new ResolutionData(roles, treasury, operations, expenses, partners, currency);
    }

    public async Task<PostingInsertResult> InsertAsync(PostingRequest request, int journalId, int? variantId, IReadOnlyList<PostingLine> lines, CancellationToken ct)
    {
        var p = request.Payload;
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_JournalEntry_Insert", new
        {
            request.CompanyId, JournalId = journalId, AccountingDate = Day(p.AccountingDate), DocumentDate = Day(p.DocumentDate), p.SourceModule,
            SourceDocumentType = p.DocumentType, SourceDocumentId = p.DocumentId == 0 ? (long?)null : p.DocumentId, SourceDocumentNumber = p.DocumentNumber,
            SourceEventId = request.EventId, VariantId = variantId, Description = Description(p), UserId = PostingService.SystemUserId,
            request.EventType, Payload = JsonSerializer.Serialize(p, Json),
            Lines = LinesJson(lines),
        }, ct)).ConfigureAwait(false);
        return await Written(grid).ConfigureAwait(false);
    }

    public async Task<PostingInsertResult> ReverseFromEventAsync(PostingRequest request, long originalDocumentId, bool black, CancellationToken ct)
    {
        var p = request.Payload;
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_JournalEntry_ReverseFromEvent", new
        {
            request.CompanyId, OriginalDocumentType = p.DocumentType, OriginalDocumentId = originalDocumentId, SourceEventId = request.EventId,
            AccountingDate = Day(p.AccountingDate), SourceDocumentId = p.DocumentId == 0 ? (long?)null : p.DocumentId, SourceDocumentNumber = p.DocumentNumber,
            Black = black, UserId = PostingService.SystemUserId,
        }, ct)).ConfigureAwait(false);
        return await Written(grid).ConfigureAwait(false);
    }

    public Task<byte?> PendingReasonAsync(int companyId, string documentType, long documentId, CancellationToken ct) =>
        session.Connection.QuerySingleOrDefaultAsync<byte?>(Command("fin.usp_PostingQueue_PendingFor", new
        {
            CompanyId = companyId, SourceDocumentType = documentType, SourceDocumentId = documentId,
        }, ct));

    public async Task<IReadOnlyList<QueuedEvent>> DueAsync(QueueFilter filter, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var rows = await session.Connection.QueryAsync<(long Id, Guid EventId, string EventType, int CompanyId, string Payload)>(Command(
            "fin.usp_PostingQueue_Due", new
            {
                filter.CompanyId, filter.Id, Reason = (byte?)filter.Reason, From = filter.From is { } f ? Day(f) : (DateTime?)null,
                To = filter.To is { } t ? Day(t) : (DateTime?)null, filter.OlderThan, filter.SkipClosedPeriod, filter.Take,
            }, ct)).ConfigureAwait(false);
        return rows.Select(r => new QueuedEvent(r.Id, r.EventId, r.EventType, r.CompanyId, Payload(r.Payload))).ToList();
    }

    public async Task<IReadOnlyList<PostingQueueItemDto>> ListQueueAsync(int companyId, byte? status, CancellationToken ct) =>
        (await session.Connection.QueryAsync<QueueRow>(Command("fin.usp_PostingQueue_List", new { CompanyId = companyId, Status = status }, ct))
            .ConfigureAwait(false))
        .Select(q => new PostingQueueItemDto(q.Id, q.EventId, q.EventType, q.SourceDocumentType, q.SourceDocumentId, q.SourceDocumentNumber,
            DateOnly.FromDateTime(q.AccountingDate), (PostingFailureReason)q.Reason, q.Details, q.Status, q.Attempts, q.LastAttemptAt, q.ResolvedEntryId,
            q.IgnoreReason, q.RowVersion)).ToList();

    public Task IgnoreAsync(int companyId, long id, string reason, byte[] rowVersion, int userId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_PostingQueue_Ignore", new
        {
            CompanyId = companyId, Id = id, Reason = reason, RowVersion = rowVersion, UserId = userId,
        }, ct));

    public async Task<IReadOnlyList<RepostCandidate>> RepostCandidatesAsync(int companyId, DateOnly from, DateOnly to, string? eventType, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_JournalEntry_RepostCandidates", new
        {
            CompanyId = companyId, From = Day(from), To = Day(to), EventType = eventType,
        }, ct)).ConfigureAwait(false);
        var entries = (await grid.ReadAsync<CandidateRow>().ConfigureAwait(false)).ToList();
        var lines = (await grid.ReadAsync<CandidateLineRow>().ConfigureAwait(false)).ToLookup(l => l.EntryId);
        return entries.Select(e => new RepostCandidate(e.Id, e.JournalCode, e.Number, DateOnly.FromDateTime(e.AccountingDate), e.EventType, Payload(e.Payload),
            e.RowVersion, lines[e.Id].Select(l => new ExistingLine(l.AccountCode, l.Side, l.AmountRon, l.PartnerId, l.EmployeeId, l.CostCenterId, l.ProjectId,
                l.WarehouseId, l.SiteId)).ToList())).ToList();
    }

    public async Task<(int OldVersion, decimal OldTotal, decimal NewTotal)> RepostAsync(int companyId, long entryId, byte[] rowVersion, int? variantId,
        IReadOnlyList<PostingLine> lines, string reason, int userId, CancellationToken ct) =>
        await session.Connection.QuerySingleAsync<(int, decimal, decimal)>(Command("fin.usp_JournalEntry_Repost", new
        {
            CompanyId = companyId, Id = entryId, RowVersion = rowVersion, VariantId = variantId, Lines = LinesJson(lines), Reason = reason, UserId = userId,
        }, ct)).ConfigureAwait(false);

    public async Task<bool> QueueAsync(PostingRequest request, PostingFailureReason reason, string? details, CancellationToken ct) =>
        await session.Connection.QuerySingleAsync<bool>(Command("fin.usp_PostingQueue_Upsert", new
        {
            request.EventId, request.EventType, request.CompanyId, SourceDocumentType = request.Payload.DocumentType,
            SourceDocumentId = request.Payload.DocumentId == 0 ? (long?)null : request.Payload.DocumentId, SourceDocumentNumber = request.Payload.DocumentNumber,
            AccountingDate = Day(request.Payload.AccountingDate), Reason = (byte)reason, Details = details is { Length: > 2000 } ? details[..2000] : details,
            Payload = JsonSerializer.Serialize(request.Payload, Json),
        }, ct)).ConfigureAwait(false);

    public Task<string?> SettingAsync(int companyId, string code, CancellationToken ct) =>
        session.Connection.QuerySingleOrDefaultAsync<string?>(Command("fin.usp_CompanySetting_Get", new { CompanyId = companyId, Code = code }, ct));

    private static string LinesJson(IReadOnlyList<PostingLine> lines) => JsonSerializer.Serialize(lines.Select(l => new
    {
        accountCode = l.AccountCode, side = l.Side, amountRon = l.AmountRon, currencyCode = l.CurrencyCode, amountCurrency = l.AmountCurrency,
        partnerId = l.PartnerId, employeeId = l.EmployeeId, costCenterId = l.CostCenterId, projectId = l.ProjectId, warehouseId = l.WarehouseId,
        siteId = l.SiteId, vatRateCode = l.VatRateCode, vatCategory = l.VatCategory, description = l.Description, resolvedFrom = (byte)l.ResolvedFrom,
        sourceLineRef = l.SourceLineRef,
    }));

    private static AccountingPayload Payload(string json) =>
        JsonSerializer.Deserialize<AccountingPayload>(json, Json) ?? throw new InvalidOperationException("Contractul de contare păstrat e gol.");

    private static async Task<PostingInsertResult> Written(SqlMapper.GridReader grid)
    {
        var status = await grid.ReadSingleAsync<(int ErrorCode, string? ErrorMessage)>().ConfigureAwait(false);
        var header = grid.IsConsumed ? null : (await grid.ReadAsync<HeaderRow>().ConfigureAwait(false)).SingleOrDefault();
        return new PostingInsertResult(status.ErrorCode, status.ErrorMessage, header is null ? null
            : new JournalEntryHeader(header.Id, header.CompanyId, header.JournalCode, header.Number, DateOnly.FromDateTime(header.AccountingDate),
                (EntrySourceKind)header.SourceKind, header.SourceDocumentType, header.SourceDocumentId, header.TotalDebit, header.RowVersion));
    }

    private static string Description(AccountingPayload p) => p.DocumentNumber is { Length: > 0 } n ? $"{p.DocumentType} {n}" : p.DocumentType;

    private static IReadOnlyList<string> Flags(string? csv) =>
        string.IsNullOrWhiteSpace(csv) ? [] : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static DateTime Day(DateOnly day) => day.ToDateTime(TimeOnly.MinValue);

    private CommandDefinition Command(string name, object? parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record QueueRow(long Id, Guid EventId, string EventType, string? SourceDocumentType, long? SourceDocumentId, string? SourceDocumentNumber,
        DateTime AccountingDate, byte Reason, string? Details, byte Status, int Attempts, DateTime LastAttemptAt, long? ResolvedEntryId, string? IgnoreReason,
        string Payload, byte[] RowVersion);

    private sealed record CandidateRow(long Id, string JournalCode, int Number, DateTime AccountingDate, string EventType, string Payload, byte[] RowVersion);

    private sealed record CandidateLineRow(long EntryId, string AccountCode, byte Side, decimal AmountRon, int? PartnerId, int? EmployeeId, int? CostCenterId,
        int? ProjectId, int? WarehouseId, int? SiteId);

    private sealed record ModelRow(int Id, string EventType, int JournalId);

    private sealed record VariantRow(int Id, int Priority, string Name, string? RequiredFlags, string? ForbiddenFlags, byte? PartnerKind, string? DocumentType,
        DateTime ValidFrom, DateTime? ValidTo);

    private sealed record LineRow(int VariantId, int LineNumber, string Component, string? FilterVatCategory, string? FilterOperationTypeCode, byte Side, short Sign,
        string AccountKey, string? DefaultAccountCode, byte CopyDimensions, byte GroupBy, string? DescriptionTemplate);

    private sealed record HeaderRow(long Id, int CompanyId, string JournalCode, int Number, DateTime AccountingDate, byte SourceKind,
        string? SourceDocumentType, long? SourceDocumentId, decimal TotalDebit, byte[] RowVersion);
}
