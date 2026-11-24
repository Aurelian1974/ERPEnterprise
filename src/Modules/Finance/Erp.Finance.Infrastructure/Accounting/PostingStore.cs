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
            Lines = JsonSerializer.Serialize(lines.Select(l => new
            {
                accountCode = l.AccountCode, side = l.Side, amountRon = l.AmountRon, currencyCode = l.CurrencyCode, amountCurrency = l.AmountCurrency,
                partnerId = l.PartnerId, employeeId = l.EmployeeId, costCenterId = l.CostCenterId, projectId = l.ProjectId, warehouseId = l.WarehouseId,
                siteId = l.SiteId, vatRateCode = l.VatRateCode, vatCategory = l.VatCategory, description = l.Description, resolvedFrom = (byte)l.ResolvedFrom,
                sourceLineRef = l.SourceLineRef,
            })),
        }, ct)).ConfigureAwait(false);
        var status = await grid.ReadSingleAsync<(int ErrorCode, string? ErrorMessage)>().ConfigureAwait(false);
        var header = grid.IsConsumed ? null : (await grid.ReadAsync<HeaderRow>().ConfigureAwait(false)).SingleOrDefault();
        return new PostingInsertResult(status.ErrorCode, status.ErrorMessage, header is null ? null
            : new JournalEntryHeader(header.Id, header.CompanyId, header.JournalCode, header.Number, DateOnly.FromDateTime(header.AccountingDate),
                (EntrySourceKind)header.SourceKind, header.SourceDocumentType, header.SourceDocumentId, header.TotalDebit, header.RowVersion));
    }

    public Task QueueAsync(PostingRequest request, PostingFailureReason reason, string? details, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_PostingQueue_Upsert", new
        {
            request.EventId, request.EventType, request.CompanyId, SourceDocumentType = request.Payload.DocumentType,
            SourceDocumentId = request.Payload.DocumentId == 0 ? (long?)null : request.Payload.DocumentId, SourceDocumentNumber = request.Payload.DocumentNumber,
            AccountingDate = Day(request.Payload.AccountingDate), Reason = (byte)reason, Details = details is { Length: > 2000 } ? details[..2000] : details,
            Payload = JsonSerializer.Serialize(request.Payload, Json),
        }, ct));

    public Task<string?> SettingAsync(int companyId, string code, CancellationToken ct) =>
        session.Connection.QuerySingleOrDefaultAsync<string?>(Command("fin.usp_CompanySetting_Get", new { CompanyId = companyId, Code = code }, ct));

    private static string Description(AccountingPayload p) => p.DocumentNumber is { Length: > 0 } n ? $"{p.DocumentType} {n}" : p.DocumentType;

    private static IReadOnlyList<string> Flags(string? csv) =>
        string.IsNullOrWhiteSpace(csv) ? [] : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static DateTime Day(DateOnly day) => day.ToDateTime(TimeOnly.MinValue);

    private CommandDefinition Command(string name, object? parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record ModelRow(int Id, string EventType, int JournalId);

    private sealed record VariantRow(int Id, int Priority, string Name, string? RequiredFlags, string? ForbiddenFlags, byte? PartnerKind, string? DocumentType,
        DateTime ValidFrom, DateTime? ValidTo);

    private sealed record LineRow(int VariantId, int LineNumber, string Component, string? FilterVatCategory, string? FilterOperationTypeCode, byte Side, short Sign,
        string AccountKey, string? DefaultAccountCode, byte CopyDimensions, byte GroupBy, string? DescriptionTemplate);

    private sealed record HeaderRow(long Id, int CompanyId, string JournalCode, int Number, DateTime AccountingDate, byte SourceKind,
        string? SourceDocumentType, long? SourceDocumentId, decimal TotalDebit, byte[] RowVersion);
}
