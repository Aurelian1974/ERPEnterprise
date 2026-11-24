using System.Data;
using System.Text.Json;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Accounts;
using Erp.Finance.Contracts.Accounts;

namespace Erp.Finance.Infrastructure.Accounts;

/// <summary>Planurile de conturi prin procedurile fin.usp_GlAccount*, fin.usp_ChartOfAccounts*, fin.usp_GlAccountRole* (NET-06).</summary>
public sealed class ChartOfAccountsStore(IDbSession session) : IChartOfAccountsStore
{
    public async Task<IReadOnlyList<ChartDto>> ChartsAsync(CancellationToken ct) =>
        (await session.Connection.QueryAsync<ChartRow>(Command("fin.usp_ChartOfAccounts_List", null, ct)).ConfigureAwait(false))
        .Select(c => new ChartDto(c.Id, c.Code, c.Name, c.Kind, c.Purpose, c.LanguageCode.Trim(), c.RequireCompleteMapping, c.IsActive, c.AccountCount)).ToList();

    public async Task<IReadOnlyList<GlAccountItemDto>> AccountsAsync(int? chartId, int companyId, bool includeInactive, CancellationToken ct) =>
        (await session.Connection.QueryAsync<AccountRow>(Command("fin.usp_GlAccount_List", new
        {
            ChartId = chartId, CompanyId = companyId, IncludeInactive = includeInactive,
        }, ct)).ConfigureAwait(false)).Select(Item).ToList();

    public async Task<GlAccountDetailDto?> GetAsync(int id, int companyId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_GlAccount_Get", new { Id = id, CompanyId = companyId }, ct))
            .ConfigureAwait(false);
        var a = (await grid.ReadAsync<DetailRow>().ConfigureAwait(false)).SingleOrDefault();
        if (a is null)
        {
            return null;
        }

        var dimensions = (await grid.ReadAsync<AccountDimensionDto>().ConfigureAwait(false)).ToList();
        var roles = (await grid.ReadAsync<(int Id, int? CompanyId, string Role, byte? PartnerKind, string? CurrencyCode, DateTime ValidFrom, DateTime? ValidTo)>()
            .ConfigureAwait(false)).Select(r => new AccountRoleDto(r.Id, r.CompanyId, r.Role, r.PartnerKind, r.CurrencyCode?.Trim(), Day(r.ValidFrom),
                r.ValidTo is { } t ? Day(t) : null)).ToList();
        var history = (await grid.ReadAsync<AccountNameHistoryDto>().ConfigureAwait(false)).ToList();
        return new GlAccountDetailDto(a.Id, a.ChartId, a.Code, a.Name, a.ParentId, a.ParentCode, a.Level, a.AccountType, a.IsPostable, a.CompanyId,
            a.IsOffBalance, a.IsManagement, a.StatutoryReporting, a.CurrencyMode, a.CurrencyCode?.Trim(), a.PartyRequirement, a.ManualPosting,
            a.SaftAccountCode, a.TaxTreatment, a.ClosingTarget, a.ValidFrom is { } f ? Day(f) : null, a.ValidTo is { } to ? Day(to) : null, a.IsSystem,
            a.IsActive, a.RowVersion, dimensions, roles, history);
    }

    public async Task<FinanceSaved> SaveAsync(GlAccountRecord account, int userId, CancellationToken ct)
    {
        var row = await session.Connection.QuerySingleAsync<(int Id, byte[] RowVersion)>(Command("fin.usp_GlAccount_Save", new
        {
            account.Id, account.ParentId, account.Code, account.Name, account.CompanyId, account.CurrencyMode, account.CurrencyCode,
            account.PartyRequirement, account.StatutoryReporting, account.ManualPosting, account.TaxTreatment,
            ValidFrom = account.ValidFrom is { } f ? Date(f) : (DateTime?)null, ValidTo = account.ValidTo is { } t ? Date(t) : (DateTime?)null,
            account.RowVersion, UserId = userId,
        }, ct)).ConfigureAwait(false);
        return new FinanceSaved(row.Id, row.RowVersion);
    }

    public Task<byte[]> SetActiveAsync(int id, bool isActive, byte[] rowVersion, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<byte[]>(Command("fin.usp_GlAccount_SetActive", new
        {
            Id = id, IsActive = isActive, RowVersion = rowVersion, UserId = userId,
        }, ct));

    public Task SaveDimensionRulesAsync(int accountId, IReadOnlyList<DimensionRuleInput> rules, int userId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_GlAccountDimensionRule_Save", new
        {
            AccountId = accountId, UserId = userId,
            Rules = JsonSerializer.Serialize(rules.Select(r => new { dimensionType = r.DimensionType, mode = r.Mode })),
        }, ct));

    public async Task<IReadOnlyList<RoleAssignmentDto>> RolesAsync(int companyId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<RoleRow>(Command("fin.usp_GlAccountRole_List", new { CompanyId = companyId }, ct)).ConfigureAwait(false))
        .Select(r => new RoleAssignmentDto(r.Id, r.CompanyId, r.Role, r.AccountId, r.AccountCode, r.AccountName, r.PartnerKind, r.CurrencyCode?.Trim(),
            Day(r.ValidFrom), r.ValidTo is { } t ? Day(t) : null)).ToList();

    public Task SaveRolesAsync(int companyId, IReadOnlyList<RoleAssignmentInput> roles, int userId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_GlAccountRole_Save", new
        {
            CompanyId = companyId, UserId = userId,
            Roles = JsonSerializer.Serialize(roles.Select(r => new
            {
                role = r.Role, accountId = r.AccountId, partnerKind = r.PartnerKind, currencyCode = r.CurrencyCode,
                validFrom = r.ValidFrom?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                validTo = r.ValidTo?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            })),
        }, ct));

    public async Task<(GlAccountDto? Account, PostableReason Reason)> FindPostableAsync(int companyId, string code, DateOnly onDate, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_GlAccount_FindPostable", new
        {
            CompanyId = companyId, Code = code, OnDate = Date(onDate),
        }, ct)).ConfigureAwait(false);
        var account = (await grid.ReadAsync<PostableRow>().ConfigureAwait(false)).SingleOrDefault();
        var reason = await grid.ReadSingleAsync<int>().ConfigureAwait(false);
        return (account is null ? null : Dto(account), (PostableReason)reason);
    }

    public async Task<GlAccountDto?> FindByRoleAsync(int companyId, string role, byte? partnerKind, string? currencyCode, DateOnly onDate, CancellationToken ct)
    {
        var row = await session.Connection.QuerySingleOrDefaultAsync<PostableRow>(Command("fin.usp_GlAccount_FindByRole", new
        {
            CompanyId = companyId, Role = role, PartnerKind = partnerKind, CurrencyCode = currencyCode, OnDate = Date(onDate),
        }, ct)).ConfigureAwait(false);
        return row is null ? null : Dto(row);
    }

    public async Task<IReadOnlyList<GlAccountRuleDto>> DimensionRulesAsync(int accountId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<GlAccountRuleDto>(Command("fin.usp_GlAccount_DimensionRules", new { AccountId = accountId }, ct))
            .ConfigureAwait(false)).ToList();

    private static GlAccountDto Dto(PostableRow a) =>
        new(a.Id, a.Code, a.Name, a.AccountType, a.IsPostable, a.CurrencyMode, a.CurrencyCode?.Trim(), a.PartyRequirement, a.CompanyId);

    private static GlAccountItemDto Item(AccountRow a) =>
        new(a.Id, a.Code, a.Name, a.ParentId, a.Level, a.AccountType, a.IsPostable, a.CompanyId, a.IsOffBalance, a.IsManagement, a.StatutoryReporting,
            a.CurrencyMode, a.CurrencyCode?.Trim(), a.PartyRequirement, a.ManualPosting, a.SaftAccountCode, a.TaxTreatment, a.ClosingTarget,
            a.ValidFrom is { } f ? Day(f) : null, a.ValidTo is { } t ? Day(t) : null, a.IsSystem, a.IsActive);

    private static DateOnly Day(DateTime value) => DateOnly.FromDateTime(value);

    private static DateTime Date(DateOnly day) => day.ToDateTime(TimeOnly.MinValue);

    private CommandDefinition Command(string name, object? parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record ChartRow(int Id, string Code, string Name, byte Kind, byte? Purpose, string LanguageCode, bool RequireCompleteMapping, bool IsActive,
        int AccountCount);

    private sealed record AccountRow(int Id, string Code, string Name, int? ParentId, byte Level, byte? AccountType, bool IsPostable, int? CompanyId,
        bool IsOffBalance, bool IsManagement, bool StatutoryReporting, byte CurrencyMode, string? CurrencyCode, byte PartyRequirement, bool ManualPosting,
        string? SaftAccountCode, byte TaxTreatment, byte ClosingTarget, DateTime? ValidFrom, DateTime? ValidTo, bool IsSystem, bool IsActive);

    private sealed record DetailRow(int Id, int ChartId, string Code, string Name, int? ParentId, string? ParentCode, byte Level, byte? AccountType,
        bool IsPostable, int? CompanyId, bool IsOffBalance, bool IsManagement, bool StatutoryReporting, byte CurrencyMode, string? CurrencyCode,
        byte PartyRequirement, bool ManualPosting, string? SaftAccountCode, byte TaxTreatment, byte ClosingTarget, DateTime? ValidFrom, DateTime? ValidTo,
        bool IsSystem, bool IsActive, byte[] RowVersion);

    private sealed record RoleRow(int Id, int? CompanyId, string Role, int AccountId, string AccountCode, string AccountName, byte? PartnerKind,
        string? CurrencyCode, DateTime ValidFrom, DateTime? ValidTo);

    private sealed record PostableRow(int Id, string Code, string Name, byte? AccountType, bool IsPostable, byte CurrencyMode, string? CurrencyCode,
        byte PartyRequirement, int? CompanyId);
}
