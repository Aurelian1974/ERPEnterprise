using System.Data;
using System.Security.Cryptography;
using Dapper;
using Erp.Core.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;

namespace Erp.Auth.Data;

/// <summary>
/// Conturile ids administrate din fisa utilizatorului (02.01, 3.4 si 11; 08, 8.7), prin procedurile ids.usp_UserAccount_* in
/// tranzactia comenzii din Nucleu. Hash-ul parolei e cel al ASP.NET Core Identity; SecurityStamp nou la fiecare actiune care
/// trebuie sa respinga cookie-urile existente. Parolele temporare nu se jurnalizeaza.
/// </summary>
public sealed class IdentityAccountAdmin(IDbSession session, IPasswordHasher<UserAccount> hasher) : IIdentityAccountAdmin
{
    public const int AccountMissing = 64004;
    public const int DuplicateUserName = 64002;

    public async Task<string?> CreateAsync(Guid subjectId, string userName, string? email, bool interactive, bool twoFactorRequired, int actorUserId,
        CancellationToken ct)
    {
        var password = interactive ? TemporaryPassword.Generate() : null;
        await Execute("ids.usp_UserAccount_Create", new
        {
            SubjectId = subjectId,
            UserName = userName,
            Email = email,
            PasswordHash = password is null ? null : Hash(password),
            TwoFactorRequired = twoFactorRequired,
            SecurityStamp = NewStamp(),
            ConcurrencyStamp = NewStamp(),
            CreatedBy = actorUserId,
        }, ct);
        return password;
    }

    public async Task<bool> UpdateAsync(Guid subjectId, string userName, string? email, bool isActive, bool twoFactorRequired, bool invalidateSessions,
        int actorUserId, CancellationToken ct)
    {
        // Un THROW in procedura ar anula tranzactia comenzii (XACT_ABORT): lipsa contului se verifica inainte.
        if (!await ExistsAsync(subjectId, ct))
        {
            return false;
        }

        await Execute("ids.usp_UserAccount_AdminUpdate", new
        {
            SubjectId = subjectId,
            UserName = userName,
            Email = email,
            IsActive = isActive,
            TwoFactorRequired = twoFactorRequired,
            NewSecurityStamp = invalidateSessions ? NewStamp() : null,
            ConcurrencyStamp = NewStamp(),
            ModifiedBy = actorUserId,
        }, ct);
        return true;
    }

    public async Task<string> ResetPasswordAsync(Guid subjectId, string userName, string? email, bool twoFactorRequired, int actorUserId,
        CancellationToken ct)
    {
        if (!await ExistsAsync(subjectId, ct))
        {
            // Utilizator fara cont de autentificare (de exemplu adaugat direct in baza): contul se creeaza acum.
            return await CreateAsync(subjectId, userName, email, interactive: true, twoFactorRequired, actorUserId, ct)
                ?? throw new InvalidOperationException("Contul interactiv trebuie să aibă parolă.");
        }

        var password = TemporaryPassword.Generate();
        await Reset(subjectId, "ResetPassword", Hash(password), actorUserId, ct);
        return password;
    }

    public Task UnlockAsync(Guid subjectId, int actorUserId, CancellationToken ct) => Reset(subjectId, "Unlock", null, actorUserId, ct);

    public Task ResetTwoFactorAsync(Guid subjectId, int actorUserId, CancellationToken ct) => Reset(subjectId, "ResetTwoFactor", null, actorUserId, ct);

    public async Task<IReadOnlyList<IdentityAccountSession>> GetSessionsAsync(Guid subjectId, CancellationToken ct) =>
        [.. (await (await OpenAsync(ct)).QueryAsync<SessionRow>(new CommandDefinition("ids.usp_UserSession_List", new { SubjectId = subjectId },
                session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)))
            .Select(s => new IdentityAccountSession(s.SessionId, DateTime.SpecifyKind(s.CreatedAt, DateTimeKind.Utc),
                DateTime.SpecifyKind(s.LastSeenAt, DateTimeKind.Utc), s.IpAddress, s.UserAgent))];

    public async Task RevokeSessionsAsync(Guid subjectId, Guid? sessionId, int actorUserId, CancellationToken ct)
    {
        if (sessionId is null)
        {
            await Reset(subjectId, "RevokeSessions", null, actorUserId, ct);
            return;
        }

        var accountId = await AccountIdAsync(subjectId, ct) ?? throw MissingAccount();
        await (await OpenAsync(ct)).ExecuteAsync(new CommandDefinition("ids.usp_UserSession_Revoke", new { SessionId = sessionId, UserAccountId = accountId },
            session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    private async Task Reset(Guid subjectId, string action, string? passwordHash, int actorUserId, CancellationToken ct)
    {
        if (!await ExistsAsync(subjectId, ct))
        {
            throw MissingAccount();
        }

        await Execute("ids.usp_UserAccount_AdminReset", new
        {
            SubjectId = subjectId,
            Action = new DbString { Value = action, Length = 20, IsAnsi = true },
            PasswordHash = passwordHash,
            SecurityStamp = NewStamp(),
            ConcurrencyStamp = NewStamp(),
            ModifiedBy = actorUserId,
        }, ct);
    }

    private async Task<bool> ExistsAsync(Guid subjectId, CancellationToken ct) => await AccountIdAsync(subjectId, ct) is not null;

    private async Task<int?> AccountIdAsync(Guid subjectId, CancellationToken ct) =>
        await (await OpenAsync(ct)).QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            "SELECT Id FROM ids.UserAccount WHERE SubjectId = @SubjectId", new { SubjectId = subjectId }, session.Transaction, cancellationToken: ct));

    private static string NewStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(20));

    // Hash-ul Identity nu depinde de utilizator (PasswordHasher V3); contul gol satisface doar semnatura.
    private string Hash(string password) => hasher.HashPassword(new UserAccount(), password);

    private async Task Execute(string procedure, object parameters, CancellationToken ct)
    {
        try
        {
            await (await OpenAsync(ct)).ExecuteAsync(new CommandDefinition(procedure, parameters, session.Transaction,
                commandType: CommandType.StoredProcedure, cancellationToken: ct));
        }
        catch (SqlException ex) when (ex.Number == DuplicateUserName)
        {
            throw new ErpException(ErpErrorKind.Conflict, "CORE_USER_DUPLICATE_USERNAME", "Numele de utilizator este deja folosit.", ex);
        }
    }

    private static ErpException MissingAccount() =>
        new(ErpErrorKind.Conflict, "CORE_USER_NO_ACCOUNT", "Utilizatorul nu are încă un cont de autentificare; resetați parola ca să-l creați.");

    private async Task<System.Data.Common.DbConnection> OpenAsync(CancellationToken ct)
    {
        if (session.Connection.State != ConnectionState.Open)
        {
            await session.Connection.OpenAsync(ct);
        }

        return session.Connection;
    }

    private sealed record SessionRow(Guid SessionId, DateTime CreatedAt, DateTime LastSeenAt, string? IpAddress, string? UserAgent);
}

/// <summary>
/// Parola temporara data de administrator (02.01, 11.2): 16 caractere, cu litera mare, litera mica si cifra (regulile Identity),
/// fara caractere care se confunda la citire (0/O, 1/l/I). Se schimba obligatoriu la prima autentificare.
/// </summary>
public static class TemporaryPassword
{
    public const int Length = 16;

    private static readonly string[] Categories = ["ABCDEFGHJKLMNPQRSTUVWXYZ", "abcdefghijkmnpqrstuvwxyz", "23456789"];
    private static readonly string Alphabet = string.Concat(Categories);

    public static string Generate()
    {
        var characters = new char[Length];
        var position = 0;
        foreach (var category in Categories)
        {
            for (var i = 0; i < 2; i++)
            {
                characters[position++] = category[RandomNumberGenerator.GetInt32(category.Length)];
            }
        }

        while (position < Length)
        {
            characters[position++] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        RandomNumberGenerator.Shuffle(characters.AsSpan());
        return new string(characters);
    }
}
