using Dapper;
using Microsoft.AspNetCore.Identity;
using ValyanERP.Web.Infrastructure.Data;

namespace ValyanERP.Web.Infrastructure.Identity;

/// <summary>
/// Custom RoleStore implementation using Dapper for data access.
/// </summary>
public class DapperRoleStore : IRoleStore<ApplicationRole>
{
    private readonly DapperContext _context;

    public DapperRoleStore(DapperContext context)
    {
        _context = context;
    }

    public async Task<IdentityResult> CreateAsync(ApplicationRole role, CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO [dbo].[Roles] (Id, Name, NormalizedName, ConcurrencyStamp)
            VALUES (@Id, @Name, @NormalizedName, @ConcurrencyStamp)";

        using var connection = _context.CreateConnection();
        await connection.ExecuteAsync(sql, role);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(ApplicationRole role, CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE [dbo].[Roles] SET
                Name = @Name,
                NormalizedName = @NormalizedName,
                ConcurrencyStamp = @ConcurrencyStamp
            WHERE Id = @Id";

        using var connection = _context.CreateConnection();
        await connection.ExecuteAsync(sql, role);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> DeleteAsync(ApplicationRole role, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM [dbo].[Roles] WHERE Id = @Id";
        using var connection = _context.CreateConnection();
        await connection.ExecuteAsync(sql, new { role.Id });
        return IdentityResult.Success;
    }

    public async Task<ApplicationRole?> FindByIdAsync(string roleId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT * FROM [dbo].[Roles] WHERE Id = @Id";
        using var connection = _context.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ApplicationRole>(sql, new { Id = Guid.Parse(roleId) });
    }

    public async Task<ApplicationRole?> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken)
    {
        const string sql = "SELECT * FROM [dbo].[Roles] WHERE NormalizedName = @NormalizedName";
        using var connection = _context.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ApplicationRole>(sql, new { NormalizedName = normalizedRoleName });
    }

    public Task<string?> GetNormalizedRoleNameAsync(ApplicationRole role, CancellationToken cancellationToken)
        => Task.FromResult(role.NormalizedName);

    public Task<string> GetRoleIdAsync(ApplicationRole role, CancellationToken cancellationToken)
        => Task.FromResult(role.Id.ToString());

    public Task<string?> GetRoleNameAsync(ApplicationRole role, CancellationToken cancellationToken)
        => Task.FromResult(role.Name);

    public Task SetNormalizedRoleNameAsync(ApplicationRole role, string? normalizedName, CancellationToken cancellationToken)
    {
        role.NormalizedName = normalizedName;
        return Task.CompletedTask;
    }

    public Task SetRoleNameAsync(ApplicationRole role, string? roleName, CancellationToken cancellationToken)
    {
        role.Name = roleName;
        return Task.CompletedTask;
    }

    public void Dispose() { }
}
