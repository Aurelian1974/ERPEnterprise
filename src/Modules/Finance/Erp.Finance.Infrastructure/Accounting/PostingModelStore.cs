using System.Data;
using System.Text.Json;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Accounting;
using Erp.Finance.Contracts.Accounting;

namespace Erp.Finance.Infrastructure.Accounting;

/// <summary>Modelele prin fin.usp_PostingModel_*, fin.usp_PostingModelVariant_SaveLines, fin.usp_JournalEntrySource_Get (NET-06).</summary>
public sealed class PostingModelStore(IDbSession session) : IPostingModelStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<PostingModelItemDto>> ListAsync(CancellationToken ct) =>
        (await session.Connection.QueryAsync<PostingModelItemDto>(Command("fin.usp_PostingModel_List", null, ct)).ConfigureAwait(false)).ToList();

    public async Task<PostingModelDto?> GetAsync(int id, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_PostingModel_Get", new { Id = id }, ct)).ConfigureAwait(false);
        var m = (await grid.ReadAsync<ModelRow>().ConfigureAwait(false)).SingleOrDefault();
        var variants = (await grid.ReadAsync<VariantRow>().ConfigureAwait(false)).ToList();
        var lines = (await grid.ReadAsync<LineRow>().ConfigureAwait(false)).ToLookup(l => l.VariantId);
        var keys = (await grid.ReadAsync<AccountKeyDto>().ConfigureAwait(false)).ToList();
        return m is null ? null : new PostingModelDto(m.Id, m.EventType, m.Name, m.JournalCode, m.JournalName, m.IsSystem, m.IsActive,
            variants.Select(v => new PostingVariantDto(v.Id, v.Priority, v.Name, v.RequiredFlags, v.ForbiddenFlags, v.PartnerKind, v.DocumentType,
                DateOnly.FromDateTime(v.ValidFrom), v.ValidTo is { } to ? DateOnly.FromDateTime(to) : null, v.IsActive, v.RowVersion,
                lines[v.Id].Select(l => new PostingModelLineDto(l.LineNumber, l.Component, l.FilterVatCategory, l.FilterOperationTypeCode, l.Side, l.Sign,
                    l.AccountKey, l.DefaultAccountCode, l.CopyDimensions, l.GroupBy, l.DescriptionTemplate)).ToList())).ToList(), keys);
    }

    public async Task<byte[]> SaveLinesAsync(int variantId, IReadOnlyList<PostingModelLineInput> lines, byte[] rowVersion, int userId, CancellationToken ct) =>
        await session.Connection.QuerySingleAsync<byte[]>(Command("fin.usp_PostingModelVariant_SaveLines", new
        {
            VariantId = variantId, Lines = JsonSerializer.Serialize(lines, Json), RowVersion = rowVersion, UserId = userId,
        }, ct)).ConfigureAwait(false);

    public async Task<(string EventType, AccountingPayload Payload)?> SourceAsync(int companyId, long entryId, CancellationToken ct)
    {
        var row = await session.Connection.QuerySingleOrDefaultAsync<(string EventType, string Payload)?>(Command("fin.usp_JournalEntrySource_Get", new
        {
            CompanyId = companyId, EntryId = entryId,
        }, ct)).ConfigureAwait(false);
        return row is { } r && JsonSerializer.Deserialize<AccountingPayload>(r.Payload, Json) is { } payload ? (r.EventType, payload) : null;
    }

    private CommandDefinition Command(string name, object? parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record ModelRow(int Id, string EventType, string Name, string JournalCode, string JournalName, bool IsSystem, bool IsActive);

    private sealed record VariantRow(int Id, int Priority, string Name, string? RequiredFlags, string? ForbiddenFlags, byte? PartnerKind, string? DocumentType,
        DateTime ValidFrom, DateTime? ValidTo, bool IsActive, byte[] RowVersion);

    private sealed record LineRow(int VariantId, int LineNumber, string Component, string? FilterVatCategory, string? FilterOperationTypeCode, byte Side, short Sign,
        string AccountKey, string? DefaultAccountCode, byte CopyDimensions, byte GroupBy, string? DescriptionTemplate);
}
