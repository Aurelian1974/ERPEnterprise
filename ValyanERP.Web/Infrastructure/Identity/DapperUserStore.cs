using Dapper;
using Microsoft.AspNetCore.Identity;
using ValyanERP.Web.Infrastructure.Data;

namespace ValyanERP.Web.Infrastructure.Identity;

/// <summary>
/// Custom UserStore implementation using Dapper for data access.
/// </summary>
public class DapperUserStore : IUserStore<ApplicationUser>,
    IUserPasswordStore<ApplicationUser>,
    IUserEmailStore<ApplicationUser>,
    IUserRoleStore<ApplicationUser>,
    IUserSecurityStampStore<ApplicationUser>,
    IUserLockoutStore<ApplicationUser>
{
    private readonly DapperContext _context;

    public DapperUserStore(DapperContext context)
    {
        _context = context;
    }

    #region IUserStore

    public async Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO [dbo].[Users] 
            (Id, PersoanaId, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, FirstName, LastName, 
             PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed,
             TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount, IsActive, CreatedAt)
            VALUES 
            (@Id, @PersoanaId, @UserName, @NormalizedUserName, @Email, @NormalizedEmail, @EmailConfirmed, @FirstName, @LastName,
             @PasswordHash, @SecurityStamp, @ConcurrencyStamp, @PhoneNumber, @PhoneNumberConfirmed,
             @TwoFactorEnabled, @LockoutEnd, @LockoutEnabled, @AccessFailedCount, @IsActive, @CreatedAt)";

        using var connection = _context.CreateConnection();
        await connection.ExecuteAsync(sql, user);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE [dbo].[Users] SET
                PersoanaId = @PersoanaId,
                UserName = @UserName,
                NormalizedUserName = @NormalizedUserName,
                Email = @Email,
                NormalizedEmail = @NormalizedEmail,
                EmailConfirmed = @EmailConfirmed,
                FirstName = @FirstName,
                LastName = @LastName,
                PasswordHash = @PasswordHash,
                SecurityStamp = @SecurityStamp,
                ConcurrencyStamp = @ConcurrencyStamp,
                PhoneNumber = @PhoneNumber,
                PhoneNumberConfirmed = @PhoneNumberConfirmed,
                TwoFactorEnabled = @TwoFactorEnabled,
                LockoutEnd = @LockoutEnd,
                LockoutEnabled = @LockoutEnabled,
                AccessFailedCount = @AccessFailedCount,
                IsActive = @IsActive,
                UpdatedAt = GETDATE()
            WHERE Id = @Id";

        using var connection = _context.CreateConnection();
        await connection.ExecuteAsync(sql, user);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        // Soft delete - set IsActive to 0 instead of hard delete
        // This maintains referential integrity and audit trail
        const string sql = @"
            UPDATE [dbo].[Users] SET
                IsActive = 0,
                UpdatedAt = GETDATE()
            WHERE Id = @Id";
        
        using var connection = _context.CreateConnection();
        await connection.ExecuteAsync(sql, new { user.Id });
        return IdentityResult.Success;
    }

    public async Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT * FROM [dbo].[Users] WHERE Id = @Id";
        using var connection = _context.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ApplicationUser>(sql, new { Id = Guid.Parse(userId) });
    }

    public async Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
    {
        const string sql = "SELECT * FROM [dbo].[Users] WHERE NormalizedUserName = @NormalizedUserName";
        using var connection = _context.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ApplicationUser>(sql, new { NormalizedUserName = normalizedUserName });
    }

    public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.NormalizedUserName);

    public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.Id.ToString());

    public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.UserName);

    public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken)
    {
        user.NormalizedUserName = normalizedName;
        return Task.CompletedTask;
    }

    public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken)
    {
        user.UserName = userName;
        return Task.CompletedTask;
    }

    #endregion

    #region IUserPasswordStore

    public Task<string?> GetPasswordHashAsync(ApplicationUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.PasswordHash);

    public Task<bool> HasPasswordAsync(ApplicationUser user, CancellationToken cancellationToken)
        => Task.FromResult(!string.IsNullOrEmpty(user.PasswordHash));

    public Task SetPasswordHashAsync(ApplicationUser user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    #endregion

    #region IUserEmailStore

    public async Task<ApplicationUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        const string sql = "SELECT * FROM [dbo].[Users] WHERE NormalizedEmail = @NormalizedEmail";
        using var connection = _context.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ApplicationUser>(sql, new { NormalizedEmail = normalizedEmail });
    }

    public Task<string?> GetEmailAsync(ApplicationUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.Email);

    public Task<bool> GetEmailConfirmedAsync(ApplicationUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.EmailConfirmed);

    public Task<string?> GetNormalizedEmailAsync(ApplicationUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.NormalizedEmail);

    public Task SetEmailAsync(ApplicationUser user, string? email, CancellationToken cancellationToken)
    {
        user.Email = email;
        return Task.CompletedTask;
    }

    public Task SetEmailConfirmedAsync(ApplicationUser user, bool confirmed, CancellationToken cancellationToken)
    {
        user.EmailConfirmed = confirmed;
        return Task.CompletedTask;
    }

    public Task SetNormalizedEmailAsync(ApplicationUser user, string? normalizedEmail, CancellationToken cancellationToken)
    {
        user.NormalizedEmail = normalizedEmail;
        return Task.CompletedTask;
    }

    #endregion

    #region IUserRoleStore

    public async Task AddToRoleAsync(ApplicationUser user, string roleName, CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO [dbo].[UserRoles] (UserId, RoleId)
            SELECT @UserId, Id FROM [dbo].[Roles] WHERE NormalizedName = @RoleName";
        
        using var connection = _context.CreateConnection();
        await connection.ExecuteAsync(sql, new { UserId = user.Id, RoleName = roleName.ToUpperInvariant() });
    }

    public async Task RemoveFromRoleAsync(ApplicationUser user, string roleName, CancellationToken cancellationToken)
    {
        const string sql = @"
            DELETE FROM [dbo].[UserRoles] 
            WHERE UserId = @UserId AND RoleId IN (SELECT Id FROM [dbo].[Roles] WHERE NormalizedName = @RoleName)";
        
        using var connection = _context.CreateConnection();
        await connection.ExecuteAsync(sql, new { UserId = user.Id, RoleName = roleName.ToUpperInvariant() });
    }

    public async Task<IList<string>> GetRolesAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT r.Name FROM [dbo].[Roles] r
            INNER JOIN [dbo].[UserRoles] ur ON r.Id = ur.RoleId
            WHERE ur.UserId = @UserId";
        
        using var connection = _context.CreateConnection();
        var roles = await connection.QueryAsync<string>(sql, new { UserId = user.Id });
        return roles.ToList();
    }

    public async Task<bool> IsInRoleAsync(ApplicationUser user, string roleName, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT COUNT(1) FROM [dbo].[UserRoles] ur
            INNER JOIN [dbo].[Roles] r ON ur.RoleId = r.Id
            WHERE ur.UserId = @UserId AND r.NormalizedName = @RoleName";
        
        using var connection = _context.CreateConnection();
        var count = await connection.ExecuteScalarAsync<int>(sql, new { UserId = user.Id, RoleName = roleName.ToUpperInvariant() });
        return count > 0;
    }

    public async Task<IList<ApplicationUser>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT u.* FROM [dbo].[Users] u
            INNER JOIN [dbo].[UserRoles] ur ON u.Id = ur.UserId
            INNER JOIN [dbo].[Roles] r ON ur.RoleId = r.Id
            WHERE r.NormalizedName = @RoleName";
        
        using var connection = _context.CreateConnection();
        var users = await connection.QueryAsync<ApplicationUser>(sql, new { RoleName = roleName.ToUpperInvariant() });
        return users.ToList();
    }

    #endregion

    #region IUserSecurityStampStore

    public Task<string?> GetSecurityStampAsync(ApplicationUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.SecurityStamp);

    public Task SetSecurityStampAsync(ApplicationUser user, string stamp, CancellationToken cancellationToken)
    {
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    #endregion

    #region IUserLockoutStore

    public Task<int> GetAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.AccessFailedCount);

    public Task<bool> GetLockoutEnabledAsync(ApplicationUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.LockoutEnabled);

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(ApplicationUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.LockoutEnd);

    public Task<int> IncrementAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        user.AccessFailedCount++;
        return Task.FromResult(user.AccessFailedCount);
    }

    public Task ResetAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        user.AccessFailedCount = 0;
        return Task.CompletedTask;
    }

    public Task SetLockoutEnabledAsync(ApplicationUser user, bool enabled, CancellationToken cancellationToken)
    {
        user.LockoutEnabled = enabled;
        return Task.CompletedTask;
    }

    public Task SetLockoutEndDateAsync(ApplicationUser user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
    {
        user.LockoutEnd = lockoutEnd;
        return Task.CompletedTask;
    }

    #endregion

    public void Dispose() { }
}
