using System.Data;
using Dapper;
using Erp.Core.Application.Organization;
using Erp.Core.Application.Users;
using Erp.Core.Contracts;

namespace Erp.Core.Infrastructure.Users;

/// <summary>Rolurile si exceptiile de drepturi prin procedurile core.usp_Role* / usp_UserPermissionOverride_Save (NET-06).</summary>
public sealed class RoleStore(IDbSession session) : IRoleStore
{
    public async Task<RoleMatrixDto> GetMatrixAsync(int? companyId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("core.usp_RoleMatrix_Get", new { CompanyId = companyId }, ct))
            .ConfigureAwait(false);
        var permissions = (await grid.ReadAsync<PermissionDto>().ConfigureAwait(false)).ToList();
        var roles = (await grid.ReadAsync<RoleRow>().ConfigureAwait(false)).ToList();
        var rolePermissions = (await grid.ReadAsync<(int RoleId, int PermissionId)>().ConfigureAwait(false)).ToLookup(x => x.RoleId, x => x.PermissionId);
        var companies = (await grid.ReadAsync<CompanyOptionDto>().ConfigureAwait(false)).ToList();
        var users = (await grid.ReadAsync<UserRow>().ConfigureAwait(false)).ToList();
        var userRoles = (await grid.ReadAsync<(int UserId, int RoleId)>().ConfigureAwait(false)).ToLookup(x => x.UserId, x => x.RoleId);
        var overrides = (await grid.ReadAsync<PermissionOverrideDto>().ConfigureAwait(false)).ToList();
        var assignments = (await grid.ReadAsync<AssignmentRow>().ConfigureAwait(false)).ToLookup(a => a.UserId,
            a => new MatrixAssignmentDto(a.RoleId, a.CompanyGroupId, a.GroupName, a.CompanyId, a.CompanyName, a.BranchId, a.BranchName, a.LocationId,
                a.LocationName));
        return new RoleMatrixDto(companyId, permissions,
            [.. roles.Select(r => new RoleDto(r.Id, r.Code, r.Name, r.Description, r.IsSystem, r.HasAllPermissions, r.UserCount, r.RowVersion,
                [.. rolePermissions[r.Id].Order()]))],
            companies,
            [.. users.Select(u => new MatrixUserDto(u.Id, u.DisplayName, u.UserName, u.IsActive, [.. userRoles[u.Id].Order()], [.. assignments[u.Id]]))],
            overrides);
    }

    public Task<SavedEntity> SaveAsync(RoleRecord role, int actorUserId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<SavedEntity>(Command("core.usp_Role_Save", new
        {
            role.Id, role.Code, role.Name, role.Description,
            Permissions = Ids(role.PermissionIds, "PermissionId").AsTableValuedParameter("core.PermissionIdList"),
            role.RowVersion, UserId = actorUserId,
        }, ct));

    public Task DeleteAsync(int id, byte[] rowVersion, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("core.usp_Role_Delete", new { Id = id, RowVersion = rowVersion }, ct));

    public Task SaveOverridesAsync(int userId, int? companyId, IReadOnlyList<PermissionOverrideInput> overrides, int actorUserId,
        CancellationToken ct)
    {
        var table = new DataTable();
        table.Columns.Add("UserId", typeof(int));
        table.Columns.Add("PermissionId", typeof(int));
        table.Columns.Add("Effect", typeof(string));
        table.Columns.Add("Reason", typeof(string));
        foreach (var o in overrides)
        {
            table.Rows.Add(userId, o.PermissionId, o.Effect, (object?)o.Reason ?? DBNull.Value);
        }

        return session.Connection.ExecuteAsync(Command("core.usp_UserPermissionOverride_Save", new
        {
            CompanyId = companyId,
            UserIds = Ids([userId], "Id").AsTableValuedParameter("core.IdList"),
            Overrides = table.AsTableValuedParameter("core.UserPermissionOverrideList"),
            UserId = actorUserId,
        }, ct));
    }

    private static DataTable Ids(IEnumerable<int> ids, string column)
    {
        var table = new DataTable();
        table.Columns.Add(column, typeof(int));
        foreach (var id in ids)
        {
            table.Rows.Add(id);
        }

        return table;
    }

    private CommandDefinition Command(string procedure, object? parameters, CancellationToken ct) =>
        new(procedure, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record RoleRow(int Id, string Code, string Name, string? Description, bool IsSystem, bool HasAllPermissions, int UserCount,
        byte[] RowVersion);

    private sealed record UserRow(int Id, string DisplayName, string UserName, bool IsActive);

    private sealed record AssignmentRow(int UserId, int RoleId, int? CompanyGroupId, string? GroupName, int? CompanyId, string? CompanyName, int? BranchId,
        string? BranchName, int? LocationId, string? LocationName);
}
