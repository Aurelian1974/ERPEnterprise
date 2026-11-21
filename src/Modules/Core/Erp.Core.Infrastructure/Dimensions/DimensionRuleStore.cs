using System.Data;
using Dapper;
using Erp.Core.Application.Dimensions;
using Erp.Core.Application.Organization;
using Erp.Core.Contracts;

namespace Erp.Core.Infrastructure.Dimensions;

/// <summary>Tipurile, regulile implicite si combinatiile blocate ale dimensiunilor (core.usp_DimensionType_*, usp_DefaultDimension_*, ...).</summary>
public sealed class DimensionRuleStore(IDbSession session) : IDimensionRuleStore
{
    public Task<SavedEntity> SaveTypeAsync(SaveDimensionType command, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<SavedEntity>(Command("core.usp_DimensionType_Save", new
        {
            Code = DimensionStore.Type(command.Code), command.Name, command.SortOrder, command.IsActive, command.IsNew, command.RowVersion,
        }, ct));

    public async Task<DefaultDimensionPageDto> ListDefaultsAsync(int companyId, DimensionSource source, int sourceId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("core.usp_DefaultDimension_List",
            new { CompanyId = companyId, SourceType = (byte)source, SourceId = sourceId }, ct)).ConfigureAwait(false);
        var rows = (await grid.ReadAsync<DefaultDimensionRowDto>().ConfigureAwait(false)).ToList();
        var values = (await grid.ReadAsync<DimensionValueOptionDto>().ConfigureAwait(false)).ToList();
        return new DefaultDimensionPageDto(rows, values);
    }

    public Task SaveDefaultsAsync(int companyId, DimensionSource source, int sourceId, IReadOnlyList<DefaultDimensionInput> rows, int userId, CancellationToken ct)
    {
        var table = new DataTable();
        table.Columns.Add("DimensionType", typeof(string));
        table.Columns.Add("DimensionValueId", typeof(int));
        table.Columns.Add("ValuePosting", typeof(byte));
        foreach (var r in rows)
        {
            table.Rows.Add(r.DimensionType, (object?)r.DimensionValueId ?? DBNull.Value, r.ValuePosting);
        }

        return session.Connection.ExecuteAsync(Command("core.usp_DefaultDimension_Save", new
        {
            CompanyId = companyId, SourceType = (byte)source, SourceId = sourceId, Defaults = table.AsTableValuedParameter("core.DefaultDimensionList"),
            UserId = userId,
        }, ct));
    }

    public async Task<IReadOnlyList<DimensionCombinationDto>> ListCombinationsAsync(int companyId, CancellationToken ct) =>
        [.. await session.Connection.QueryAsync<DimensionCombinationDto>(Command("core.usp_DimensionCombination_List", new { CompanyId = companyId }, ct))
            .ConfigureAwait(false)];

    public Task<SavedEntity> SaveCombinationAsync(int companyId, SaveDimensionCombination command, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<SavedEntity>(Command("core.usp_DimensionCombination_Save", new
        {
            CompanyId = companyId, TypeA = DimensionStore.Type(command.TypeA), command.ValueAId, TypeB = DimensionStore.Type(command.TypeB), command.ValueBId,
            command.Note, UserId = userId,
        }, ct));

    public Task DeleteCombinationAsync(int companyId, int id, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("core.usp_DimensionCombination_Delete", new { CompanyId = companyId, Id = id }, ct));

    public async Task<DimensionRuleRows> GetRulesAsync(int companyId, int? partnerId, int? itemId, int? locationId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("core.usp_DimensionRules_Get",
            new { CompanyId = companyId, PartnerId = partnerId, ItemId = itemId, LocationId = locationId }, ct)).ConfigureAwait(false);
        var rules = (await grid.ReadAsync<RuleRow>().ConfigureAwait(false))
            .Select(r => new DefaultDimensionRule((DimensionSource)r.SourceType, r.DimensionType, r.DimensionValueId, (DimensionPosting)r.ValuePosting)).ToList();
        var combinations = (await grid.ReadAsync<BlockedCombination>().ConfigureAwait(false)).ToList();
        return new DimensionRuleRows(rules, combinations);
    }

    private CommandDefinition Command(string procedure, object parameters, CancellationToken ct) =>
        new(procedure, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record RuleRow(byte SourceType, string DimensionType, int? DimensionValueId, byte ValuePosting);
}
