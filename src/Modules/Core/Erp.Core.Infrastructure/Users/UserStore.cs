using System.Data;
using Dapper;
using Erp.Core.Application.Partners;
using Erp.Core.Application.Users;
using Erp.Core.Contracts;

namespace Erp.Core.Infrastructure.Users;

/// <summary>Utilizatorii prin procedurile core.usp_User* (NET-06), pe conexiunea si tranzactia cererii.</summary>
public sealed class UserStore(IDbSession session) : IUserStore
{
    public async Task<UserPageDto> SearchAsync(UserSearchCriteria criteria, int? licensedUsers, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("core.usp_User_Search", new
        {
            criteria.Search, criteria.Kind, criteria.Filter, criteria.CompanyId, criteria.RoleId, Today = Date(criteria.Today), criteria.Offset,
            criteria.PageSize,
        }, ct)).ConfigureAwait(false);
        var rows = (await grid.ReadAsync<SearchRow>().ConfigureAwait(false)).ToList();
        var roles = (await grid.ReadAsync<RoleRow>().ConfigureAwait(false)).ToLookup(r => r.UserId);
        var counters = await grid.ReadSingleAsync<CounterRow>().ConfigureAwait(false);
        var items = rows.Select(r => new UserListItemDto(r.Id, r.UserName, r.DisplayName, r.Email, r.IsActive, r.IsSystem, r.IsEmployee, r.EmployeeNo,
            Date(r.EmployeeLeaveDate), r.ExternalAccessType, r.OrganizationName, Date(r.ValidUntil), r.TwoFactorEnabled, Utc(r.LastLoginAt),
            [.. roles[r.Id].Select(x => new UserRoleSummaryDto(x.CompanyId, x.CompanyName, x.RoleCode, x.RoleName, x.CompanyGroupId, x.GroupName,
                x.BranchId, x.BranchName, x.LocationId, x.LocationName))])).ToList();
        var total = rows.Count > 0 ? rows[0].TotalCount : 0;
        if (rows.Count == 0 && criteria.Offset > 0)
        {
            total = (await SearchAsync(criteria with { Offset = 0, PageSize = 1 }, licensedUsers, ct).ConfigureAwait(false)).Total;
        }

        return new UserPageDto(items, total, new UserCountersDto(counters.ActiveCount ?? 0, licensedUsers, counters.EmployeeCount ?? 0,
            counters.ExternalCount ?? 0, counters.ExpiringCount ?? 0, counters.WithoutTwoFactorCount ?? 0, counters.InactiveCount ?? 0));
    }

    public async Task<UserDetailDto?> GetAsync(int id, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("core.usp_User_Get", new { Id = id }, ct)).ConfigureAwait(false);
        var r = await grid.ReadSingleOrDefaultAsync<DetailRow>().ConfigureAwait(false);
        var roles = (await grid.ReadAsync<UserRoleAssignmentDto>().ConfigureAwait(false)).ToList();
        return r is null ? null : new UserDetailDto(r.Id, r.UserName, r.DisplayName, r.FirstName, r.LastName, r.Email, r.Phone, r.IsActive, r.IsSystem,
            r.IsEmployee, r.EmployeeId, r.EmployeeNo, r.EmployeeCompanyId, r.EmployeeCompanyName, r.EmployeeBranchName, r.EmployeeDepartment,
            r.EmployeeJobTitle, r.EmployeePhone, Date(r.EmployeeHireDate), Date(r.EmployeeLeaveDate), r.EmployeeIsActive, r.ExternalAccessType,
            r.ExternalPartnerId, r.ExternalPartnerName, r.ExternalPartnerFiscalCode, r.SponsorUserId, r.SponsorName, r.AccessReason, Date(r.ValidFrom),
            Date(r.ValidUntil), r.Language, r.LastCompanyId, Utc(r.LastLoginAt), Utc(r.CreatedAt)!.Value, r.CreatedByName, Utc(r.LastChangedAt)!.Value,
            r.ModifiedByName, r.HasAccount, r.HasPassword ?? false, Utc(r.PasswordChangedAt), r.MustChangePassword ?? false,
            r.TwoFactorEnabled ?? false, r.TwoFactorRequired ?? false, Utc(r.LockedUntil), r.ActiveSessions ?? 0, r.RowVersion, roles,
            NotificationPreferenceRules.Effective(r.NotificationPreferences));
    }

    public Task<UserAccountRef?> GetAccountAsync(int id, CancellationToken ct) =>
        session.Connection.QuerySingleOrDefaultAsync<UserAccountRef>(Command("core.usp_User_GetAccount", new { Id = id }, ct));

    public async Task<UserLookupsDto> GetLookupsAsync(int externalAccessMaxMonths, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("core.usp_UserLookups_Get", null, ct)).ConfigureAwait(false);
        var roles = (await grid.ReadAsync<RoleOptionDto>().ConfigureAwait(false)).ToList();
        var companies = (await grid.ReadAsync<CompanyOptionDto>().ConfigureAwait(false)).ToList();
        var sponsors = (await grid.ReadAsync<UserOptionDto>().ConfigureAwait(false)).ToList();
        var groups = (await grid.ReadAsync<GroupOptionDto>().ConfigureAwait(false)).ToList();
        var branches = (await grid.ReadAsync<Application.Employees.BranchOptionDto>().ConfigureAwait(false)).ToList();
        var locations = (await grid.ReadAsync<LocationOptionDto>().ConfigureAwait(false)).ToList();
        return new UserLookupsDto(roles, companies, sponsors, externalAccessMaxMonths, NotificationTypes.All, groups, branches, locations);
    }

    public async Task<IReadOnlyList<PartnerHistoryEntryDto>> GetHistoryAsync(int id, CancellationToken ct) =>
        [.. (await session.Connection.QueryAsync<HistoryRow>(Command("core.usp_User_History", new { Id = id }, ct)).ConfigureAwait(false))
            .Select(h => new PartnerHistoryEntryDto(h.Id, DateTime.SpecifyKind(h.OccurredAt, DateTimeKind.Utc), h.Action, h.EntityType, h.EntityId,
                h.UserName, h.Changes))];

    public Task<UserSaveResult> SaveAsync(UserRecord user, int? maxActiveUsers, int actorUserId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<UserSaveResult>(Command("core.usp_User_Save", new
        {
            user.Id, user.SubjectId, user.UserName, user.IsEmployee, user.EmployeeId, user.FirstName, user.LastName, user.Email, user.Phone,
            ExternalAccessType = new DbString { Value = user.ExternalAccessType, Length = 20, IsAnsi = true },
            user.ExternalPartnerId, user.SponsorUserId, user.AccessReason, ValidFrom = Date(user.ValidFrom), ValidUntil = Date(user.ValidUntil),
            Language = new DbString { Value = user.Language, Length = 10, IsAnsi = true }, user.LastCompanyId, user.IsActive,
            MaxActiveUsers = maxActiveUsers, user.RowVersion, user.SaveNotificationPreferences, user.NotificationPreferences, UserId = actorUserId,
        }, ct));

    public Task<bool> SaveRolesAsync(int userId, IReadOnlyList<UserRoleInput> roles, int actorUserId, CancellationToken ct)
    {
        var table = new DataTable();
        table.Columns.Add("RoleId", typeof(int));
        table.Columns.Add("CompanyGroupId", typeof(int));
        table.Columns.Add("CompanyId", typeof(int));
        table.Columns.Add("BranchId", typeof(int));
        table.Columns.Add("LocationId", typeof(int));
        foreach (var role in roles)
        {
            table.Rows.Add(role.RoleId, (object?)role.CompanyGroupId ?? DBNull.Value, (object?)role.CompanyId ?? DBNull.Value,
                (object?)role.BranchId ?? DBNull.Value, (object?)role.LocationId ?? DBNull.Value);
        }

        return session.Connection.QuerySingleAsync<bool>(Command("core.usp_UserRoleAssignment_Save", new
        {
            UserId = userId,
            Assignments = table.AsTableValuedParameter("core.UserRoleAssignmentList"),
            ModifiedBy = actorUserId,
        }, ct));
    }

    private CommandDefinition Command(string procedure, object? parameters, CancellationToken ct) =>
        new(procedure, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private static DateTime? Date(DateOnly? value) => value?.ToDateTime(TimeOnly.MinValue);

    private static DateTime Date(DateOnly value) => value.ToDateTime(TimeOnly.MinValue);

    private static DateOnly? Date(DateTime? value) => value is { } v ? DateOnly.FromDateTime(v) : null;

    private static DateTime? Utc(DateTime? value) => value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;

    private sealed record SearchRow(int Id, Guid SubjectId, string UserName, string DisplayName, string? Email, bool IsActive, bool IsSystem, bool IsEmployee,
        string? EmployeeNo, DateTime? EmployeeLeaveDate, string? ExternalAccessType, string? OrganizationName, DateTime? ValidUntil,
        bool? TwoFactorEnabled, DateTime? LastLoginAt, int TotalCount);

    private sealed record RoleRow(int UserId, int? CompanyId, string? CompanyName, string RoleCode, string RoleName, int? CompanyGroupId,
        string? GroupName, int? BranchId, string? BranchName, int? LocationId, string? LocationName);

    private sealed record CounterRow(int? ActiveCount, int? EmployeeCount, int? ExternalCount, int? ExpiringCount, int? WithoutTwoFactorCount,
        int? InactiveCount);

    private sealed record DetailRow(int Id, Guid SubjectId, string UserName, string DisplayName, string? FirstName, string? LastName, string? Email,
        string? Phone, bool IsActive, bool IsSystem, bool IsEmployee, int? EmployeeId, string? EmployeeNo, int? EmployeeCompanyId,
        string? EmployeeCompanyName, string? EmployeeBranchName, string? EmployeeDepartment, string? EmployeeJobTitle, string? EmployeePhone,
        DateTime? EmployeeHireDate, DateTime? EmployeeLeaveDate, bool? EmployeeIsActive, string? ExternalAccessType, int? ExternalPartnerId,
        string? ExternalPartnerName, string? ExternalPartnerFiscalCode, int? SponsorUserId, string? SponsorName, string? AccessReason,
        DateTime? ValidFrom, DateTime? ValidUntil, string Language, int? LastCompanyId, DateTime? LastLoginAt, DateTime? CreatedAt,
        string CreatedByName, DateTime? LastChangedAt, string ModifiedByName, bool? HasPassword, DateTime? PasswordChangedAt,
        bool? MustChangePassword, bool? TwoFactorEnabled, bool? TwoFactorRequired, DateTime? LockedUntil, int? ActiveSessions, bool HasAccount,
        byte[] RowVersion, string? NotificationPreferences);

    private sealed record HistoryRow(long Id, DateTime OccurredAt, string Action, string EntityType, long? EntityId, string UserName, string? Changes);
}
