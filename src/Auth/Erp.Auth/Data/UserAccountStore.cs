using System.Data;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Erp.Core.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;

namespace Erp.Auth.Data;

/// <summary>
/// Store-ul ASP.NET Core Identity peste ids.UserAccount, cu Dapper (08, 8.7; R2: fara EF Core). Scrierile trec prin
/// proceduri (SP-01); concurenta pe ConcurrencyStamp (64003). Cheia TOTP se pastreaza criptata cu Data Protection,
/// codurile de rezerva doar ca SHA-256. Crearea si stergerea conturilor apartin administrarii utilizatorilor (Nucleu, 3.4).
/// </summary>
public sealed class UserAccountStore(IDbSession session, IDataProtectionProvider dataProtection, IdentityErrorDescriber errors) :
    IUserPasswordStore<UserAccount>,
    IUserEmailStore<UserAccount>,
    IUserSecurityStampStore<UserAccount>,
    IUserLockoutStore<UserAccount>,
    IUserTwoFactorStore<UserAccount>,
    IUserAuthenticatorKeyStore<UserAccount>,
    IUserTwoFactorRecoveryCodeStore<UserAccount>
{
    public const int ConcurrencyError = 64003;

    private const string SelectAccount = """
        SELECT ua.Id, ua.SubjectId, ua.UserName, ua.NormalizedUserName, ua.Email, ua.NormalizedEmail, ua.EmailConfirmed,
               ua.PasswordHash, ua.PasswordChangedAt, ua.MustChangePassword, ua.SecurityStamp, ua.ConcurrencyStamp,
               ua.TwoFactorEnabled, ua.TwoFactorRequired, ua.AuthenticatorKeyEncrypted, ua.LockoutEnabled, ua.LockoutEndAt,
               ua.AccessFailedCount,
               CAST(CASE WHEN ua.IsActive = 1 AND s.CanSignIn = 1 THEN 1 ELSE 0 END AS bit) AS CanSignIn,
               s.UserId AS CoreUserId, COALESCE(s.DisplayName, ua.UserName) AS DisplayName, COALESCE(s.IsSystem, CAST(0 AS bit)) AS IsSystem
        FROM ids.UserAccount AS ua
        LEFT JOIN core.v_UserLoginStatus AS s ON s.SubjectId = ua.SubjectId
        """;

    private readonly IDataProtector authenticatorKeys = dataProtection.CreateProtector("Erp.Auth.AuthenticatorKey.v1");

    public void Dispose()
    {
    }

    public Task<string> GetUserIdAsync(UserAccount user, CancellationToken cancellationToken) => Task.FromResult(user.SubjectId.ToString());

    public Task<string?> GetUserNameAsync(UserAccount user, CancellationToken cancellationToken) => Task.FromResult<string?>(user.UserName);

    public Task SetUserNameAsync(UserAccount user, string? userName, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Numele de utilizator se schimbă din administrarea utilizatorilor.");

    public Task<string?> GetNormalizedUserNameAsync(UserAccount user, CancellationToken cancellationToken) => Task.FromResult<string?>(user.NormalizedUserName);

    // Numele normalizat e calculat in baza (UPPER, CK_UserAccount_Normalized); valoarea din Identity e identica.
    public Task SetNormalizedUserNameAsync(UserAccount user, string? normalizedName, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IdentityResult> CreateAsync(UserAccount user, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Conturile se creează din administrarea utilizatorilor (02.01, 3.4).");

    public Task<IdentityResult> DeleteAsync(UserAccount user, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Conturile nu se șterg; se dezactivează (02.01, 3.4).");

    public async Task<IdentityResult> UpdateAsync(UserAccount user, CancellationToken cancellationToken)
    {
        var newStamp = Guid.NewGuid().ToString();
        try
        {
            await (await OpenAsync(cancellationToken)).ExecuteAsync(new CommandDefinition("ids.usp_UserAccount_Update", new
            {
                user.Id,
                ExpectedConcurrencyStamp = user.ConcurrencyStamp,
                ConcurrencyStamp = newStamp,
                user.Email,
                user.NormalizedEmail,
                user.EmailConfirmed,
                user.PasswordHash,
                user.PasswordChangedAt,
                user.MustChangePassword,
                user.SecurityStamp,
                user.TwoFactorEnabled,
                user.AuthenticatorKeyEncrypted,
                user.LockoutEnabled,
                user.LockoutEndAt,
                user.AccessFailedCount,
                ModifiedBy = user.CoreUserId ?? 1,
            }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken));
        }
        catch (SqlException ex) when (ex.Number == ConcurrencyError)
        {
            return IdentityResult.Failed(errors.ConcurrencyFailure());
        }

        user.ConcurrencyStamp = newStamp;
        return IdentityResult.Success;
    }

    public async Task<UserAccount?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
        Guid.TryParse(userId, out var subjectId)
            ? await QueryAccountAsync("WHERE ua.SubjectId = @Value", subjectId, cancellationToken)
            : null;

    public async Task<UserAccount?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
        await QueryAccountAsync("WHERE ua.NormalizedUserName = @Value", new DbString { Value = normalizedUserName, Length = 50 }, cancellationToken);

    public async Task<UserAccount?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        await QueryAccountAsync("WHERE ua.NormalizedEmail = @Value", new DbString { Value = normalizedEmail, Length = 256 }, cancellationToken);

    public Task SetPasswordHashAsync(UserAccount user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.PasswordHash = passwordHash;
        user.PasswordChangedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(UserAccount user, CancellationToken cancellationToken) => Task.FromResult(user.PasswordHash);

    public Task<bool> HasPasswordAsync(UserAccount user, CancellationToken cancellationToken) => Task.FromResult(user.PasswordHash is not null);

    public Task SetEmailAsync(UserAccount user, string? email, CancellationToken cancellationToken)
    {
        user.Email = email;
        return Task.CompletedTask;
    }

    public Task<string?> GetEmailAsync(UserAccount user, CancellationToken cancellationToken) => Task.FromResult(user.Email);

    public Task<bool> GetEmailConfirmedAsync(UserAccount user, CancellationToken cancellationToken) => Task.FromResult(user.EmailConfirmed);

    public Task SetEmailConfirmedAsync(UserAccount user, bool confirmed, CancellationToken cancellationToken)
    {
        user.EmailConfirmed = confirmed;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedEmailAsync(UserAccount user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedEmail);

    public Task SetNormalizedEmailAsync(UserAccount user, string? normalizedEmail, CancellationToken cancellationToken)
    {
        user.NormalizedEmail = normalizedEmail;
        return Task.CompletedTask;
    }

    public Task SetSecurityStampAsync(UserAccount user, string stamp, CancellationToken cancellationToken)
    {
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(UserAccount user, CancellationToken cancellationToken) => Task.FromResult<string?>(user.SecurityStamp);

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(UserAccount user, CancellationToken cancellationToken) =>
        Task.FromResult(user.LockoutEndAt is { } end ? new DateTimeOffset(DateTime.SpecifyKind(end, DateTimeKind.Utc)) : (DateTimeOffset?)null);

    public Task SetLockoutEndDateAsync(UserAccount user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
    {
        user.LockoutEndAt = lockoutEnd?.UtcDateTime;
        return Task.CompletedTask;
    }

    public Task<int> IncrementAccessFailedCountAsync(UserAccount user, CancellationToken cancellationToken) => Task.FromResult(++user.AccessFailedCount);

    public Task ResetAccessFailedCountAsync(UserAccount user, CancellationToken cancellationToken)
    {
        user.AccessFailedCount = 0;
        return Task.CompletedTask;
    }

    public Task<int> GetAccessFailedCountAsync(UserAccount user, CancellationToken cancellationToken) => Task.FromResult(user.AccessFailedCount);

    public Task<bool> GetLockoutEnabledAsync(UserAccount user, CancellationToken cancellationToken) => Task.FromResult(user.LockoutEnabled);

    public Task SetLockoutEnabledAsync(UserAccount user, bool enabled, CancellationToken cancellationToken)
    {
        user.LockoutEnabled = enabled;
        return Task.CompletedTask;
    }

    public Task SetTwoFactorEnabledAsync(UserAccount user, bool enabled, CancellationToken cancellationToken)
    {
        user.TwoFactorEnabled = enabled;
        return Task.CompletedTask;
    }

    public Task<bool> GetTwoFactorEnabledAsync(UserAccount user, CancellationToken cancellationToken) => Task.FromResult(user.TwoFactorEnabled);

    public Task SetAuthenticatorKeyAsync(UserAccount user, string key, CancellationToken cancellationToken)
    {
        user.AuthenticatorKeyEncrypted = authenticatorKeys.Protect(Encoding.UTF8.GetBytes(key));
        return Task.CompletedTask;
    }

    public Task<string?> GetAuthenticatorKeyAsync(UserAccount user, CancellationToken cancellationToken) =>
        Task.FromResult(user.AuthenticatorKeyEncrypted is { } encrypted ? Encoding.UTF8.GetString(authenticatorKeys.Unprotect(encrypted)) : null);

    public async Task ReplaceCodesAsync(UserAccount user, IEnumerable<string> recoveryCodes, CancellationToken cancellationToken)
    {
        var table = new DataTable();
        table.Columns.Add("CodeHash", typeof(string));
        foreach (var hash in recoveryCodes.Select(HashRecoveryCode).Distinct(StringComparer.Ordinal))
        {
            table.Rows.Add(hash);
        }

        await (await OpenAsync(cancellationToken)).ExecuteAsync(new CommandDefinition("ids.usp_UserRecoveryCode_Replace",
            new { UserAccountId = user.Id, Codes = table.AsTableValuedParameter("ids.tt_RecoveryCode") },
            session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken));
    }

    public async Task<bool> RedeemCodeAsync(UserAccount user, string code, CancellationToken cancellationToken) =>
        await (await OpenAsync(cancellationToken)).ExecuteScalarAsync<int>(new CommandDefinition("ids.usp_UserRecoveryCode_Redeem",
            new { UserAccountId = user.Id, CodeHash = new DbString { Value = HashRecoveryCode(code), Length = 64, IsAnsi = true, IsFixedLength = true } },
            session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken)) == 1;

    public async Task<int> CountCodesAsync(UserAccount user, CancellationToken cancellationToken) =>
        await (await OpenAsync(cancellationToken)).ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM ids.UserRecoveryCode WHERE UserAccountId = @Id AND UsedAt IS NULL;", new { user.Id }, session.Transaction,
            cancellationToken: cancellationToken));

    /// <summary>Hash-urile ultimelor parole (cea curenta inclusa), pentru regula „diferita de ultimele 5”.</summary>
    public async Task<IReadOnlyList<string>> RecentPasswordHashesAsync(UserAccount user, int count, CancellationToken cancellationToken) =>
        [.. await (await OpenAsync(cancellationToken)).QueryAsync<string>(new CommandDefinition(
            "SELECT TOP (@Count) PasswordHash FROM ids.PasswordHistory WHERE UserAccountId = @Id ORDER BY CreatedAt DESC, Id DESC;",
            new { Count = count, user.Id }, session.Transaction, cancellationToken: cancellationToken))];

    // Codurile se compara fara spatii, cratime si diferenta de majuscule; se pastreaza doar SHA-256 (hex).
    internal static string HashRecoveryCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant())));

    private async Task<UserAccount?> QueryAccountAsync(string where, object value, CancellationToken ct)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Value", value);
        return await (await OpenAsync(ct)).QuerySingleOrDefaultAsync<UserAccount>(new CommandDefinition(
            SelectAccount + "\n" + where + ";", parameters, session.Transaction, cancellationToken: ct));
    }

    private async Task<System.Data.Common.DbConnection> OpenAsync(CancellationToken ct)
    {
        if (session.Connection.State != ConnectionState.Open)
        {
            await session.Connection.OpenAsync(ct);
        }

        return session.Connection;
    }
}
