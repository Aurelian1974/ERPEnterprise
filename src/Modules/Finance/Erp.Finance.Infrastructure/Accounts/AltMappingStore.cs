using System.Data;
using System.Globalization;
using System.Text.Json;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Accounts;
using Erp.Finance.Domain;

namespace Erp.Finance.Infrastructure.Accounts;

/// <summary>Planurile alternative si regulile de mapare prin fin.usp_AltChart*, fin.usp_AltGlAccount_Save, fin.usp_AccountMappingRule*, fin.usp_AltMapping* (NET-06).</summary>
public sealed class AltMappingStore(IDbSession session) : IAltMappingStore
{
    public async Task<AltChartDto?> GetChartAsync(int id, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_AltChart_Get", new { Id = id }, ct)).ConfigureAwait(false);
        var c = (await grid.ReadAsync<ChartRow>().ConfigureAwait(false)).SingleOrDefault();
        if (c is null)
        {
            return null;
        }

        var companies = (await grid.ReadAsync<AltChartCompanyDto>().ConfigureAwait(false)).ToList();
        return new AltChartDto(c.Id, c.Code, c.Name, c.Kind, c.Purpose, c.LanguageCode.Trim(), c.RequireCompleteMapping, c.IsActive, c.RowVersion, c.AccountCount,
            c.RuleCount, companies);
    }

    public async Task<FinanceSaved> SaveChartAsync(int? id, string code, string name, byte purpose, string languageCode, bool requireCompleteMapping,
        bool isActive, IReadOnlyList<int> companies, byte[]? rowVersion, int userId, CancellationToken ct)
    {
        var row = await session.Connection.QuerySingleAsync<(int Id, byte[] RowVersion)>(Command("fin.usp_AltChart_Save", new
        {
            Id = id, Code = code, Name = name, Purpose = purpose, LanguageCode = languageCode, RequireCompleteMapping = requireCompleteMapping,
            IsActive = isActive, Companies = JsonSerializer.Serialize(companies), RowVersion = rowVersion, UserId = userId,
        }, ct)).ConfigureAwait(false);
        return new FinanceSaved(row.Id, row.RowVersion);
    }

    public async Task<FinanceSaved> SaveAccountAsync(int? id, int chartId, int? parentId, string code, string name, bool isActive, byte[]? rowVersion,
        int userId, CancellationToken ct)
    {
        var row = await session.Connection.QuerySingleAsync<(int Id, byte[] RowVersion)>(Command("fin.usp_AltGlAccount_Save", new
        {
            Id = id, ChartId = chartId, ParentId = parentId, Code = code, Name = name, IsActive = isActive, RowVersion = rowVersion, UserId = userId,
        }, ct)).ConfigureAwait(false);
        return new FinanceSaved(row.Id, row.RowVersion);
    }

    public async Task<IReadOnlyList<MappingRuleDto>> RulesAsync(int chartId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_AccountMappingRule_List", new { ChartId = chartId }, ct))
            .ConfigureAwait(false);
        var rules = (await grid.ReadAsync<RuleRow>().ConfigureAwait(false)).ToList();
        var sources = (await grid.ReadAsync<(int RuleId, int AccountId, string Code)>().ConfigureAwait(false)).ToLookup(s => s.RuleId);
        var targets = (await grid.ReadAsync<(int RuleId, int TargetAccountId, string Code, string Name, decimal Percent)>().ConfigureAwait(false))
            .ToLookup(t => t.RuleId);
        return rules.Select(r => new MappingRuleDto(r.Id, r.Priority, r.SourceKind, r.SourceAccountId, r.SourceAccountCode, r.SourceFrom, r.SourceTo,
            sources[r.Id].Select(s => new MappingSourceDto(s.AccountId, s.Code)).ToList(), r.CompanyId, r.Side, r.PartnerKind, r.PartnerGroupId, r.PartnerId,
            r.PartnerName, r.DimensionType, r.DimensionCondition, r.DimensionValueId, r.DimensionValueCode, r.DimensionValueName, r.ItemGroupId,
            r.CurrencyCode?.Trim(), r.BalanceSign, DateOnly.FromDateTime(r.ValidFrom), r.ValidTo is { } t ? DateOnly.FromDateTime(t) : null, r.IsActive, r.Note,
            r.RowVersion, targets[r.Id].Select(x => new MappingTargetDto(x.TargetAccountId, x.Code, x.Name, Percent(x.Percent))).ToList())).ToList();
    }

    public async Task<FinanceSaved> SaveRuleAsync(MappingRuleRecord rule, int userId, CancellationToken ct)
    {
        var row = await session.Connection.QuerySingleAsync<(int Id, byte[] RowVersion)>(Command("fin.usp_AccountMappingRule_Save", new
        {
            rule.Id, rule.ChartId, rule.Priority, rule.SourceKind, rule.SourceAccountId, rule.SourceFrom, rule.SourceTo,
            SourceAccounts = JsonSerializer.Serialize(rule.SourceAccounts), rule.CompanyId, rule.Side, rule.PartnerKind, rule.PartnerGroupId, rule.PartnerId,
            rule.DimensionType, rule.DimensionCondition, rule.DimensionValueId, rule.ItemGroupId, rule.CurrencyCode, rule.BalanceSign,
            ValidFrom = rule.ValidFrom.ToDateTime(TimeOnly.MinValue), ValidTo = rule.ValidTo?.ToDateTime(TimeOnly.MinValue), rule.IsActive, rule.Note,
            Targets = JsonSerializer.Serialize(rule.Targets.Select(t => new { accountId = t.AccountId, percent = t.Percent })), rule.RowVersion, UserId = userId,
        }, ct)).ConfigureAwait(false);
        return new FinanceSaved(row.Id, row.RowVersion);
    }

    public Task ReorderAsync(int chartId, IReadOnlyList<(int Id, int Priority)> order, int userId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_AccountMappingRule_Reorder", new
        {
            ChartId = chartId, UserId = userId, Order = JsonSerializer.Serialize(order.Select(o => new { id = o.Id, priority = o.Priority })),
        }, ct));

    public async Task<SimulationDto> SimulateAsync(int chartId, int companyId, string accountCode, byte side, int? partnerId, int? costCenterId, int? projectId,
        int? warehouseId, int? siteId, int? itemGroupId, string? currencyCode, DateOnly onDate, CancellationToken ct)
    {
        var rows = (await session.Connection.QueryAsync<(int RuleId, int Priority, string? Note, int TargetAccountId, string Code, string Name, decimal Percent)>(
            Command("fin.usp_AltMapping_Simulate", new
            {
                ChartId = chartId, CompanyId = companyId, AccountCode = accountCode, Side = side, PartnerId = partnerId, CostCenterId = costCenterId,
                ProjectId = projectId, WarehouseId = warehouseId, SiteId = siteId, ItemGroupId = itemGroupId, CurrencyCode = currencyCode,
                OnDate = onDate.ToDateTime(TimeOnly.MinValue),
            }, ct)).ConfigureAwait(false)).ToList();
        return rows.Count == 0 ? new SimulationDto(null, null, null, [])
            : new SimulationDto(rows[0].RuleId, rows[0].Priority, rows[0].Note,
                rows.Select(r => new MappingTargetDto(r.TargetAccountId, r.Code, r.Name, Percent(r.Percent))).ToList());
    }

    public async Task<IReadOnlyList<AltAccountRefDto>> AccountsWithoutSourceAsync(int chartId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<AltAccountRefDto>(Command("fin.usp_AltMapping_Check", new { ChartId = chartId }, ct)).ConfigureAwait(false)).ToList();

    private static string Percent(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private CommandDefinition Command(string name, object? parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record ChartRow(int Id, string Code, string Name, byte Kind, byte? Purpose, string LanguageCode, bool RequireCompleteMapping, bool IsActive,
        byte[] RowVersion, int AccountCount, int RuleCount);

    private sealed record RuleRow(int Id, int Priority, byte SourceKind, int? SourceAccountId, string? SourceAccountCode, string? SourceFrom, string? SourceTo,
        int? CompanyId, byte Side, byte? PartnerKind, int? PartnerGroupId, int? PartnerId, string? PartnerName, string? DimensionType, byte DimensionCondition,
        int? DimensionValueId, string? DimensionValueCode, string? DimensionValueName, int? ItemGroupId, string? CurrencyCode, byte BalanceSign,
        DateTime ValidFrom, DateTime? ValidTo, bool IsActive, string? Note, byte[] RowVersion);
}
