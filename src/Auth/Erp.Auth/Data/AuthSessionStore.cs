using System.Data;
using Dapper;
using Erp.Core.Contracts;

namespace Erp.Auth.Data;

/// <summary>Sesiunile active din ids.UserSession (08, 8.7) si jurnalul de autentificari core.LoginEvent (02.01, 9).</summary>
public sealed class AuthSessionStore(IDbSession session)
{
    public async Task CreateAsync(Guid sessionId, int userAccountId, string? ipAddress, string? userAgent, CancellationToken ct) =>
        await (await OpenAsync(ct)).ExecuteAsync(new CommandDefinition("ids.usp_UserSession_Create", new
        {
            SessionId = sessionId,
            UserAccountId = userAccountId,
            IpAddress = new DbString { Value = ipAddress, Length = 45, IsAnsi = true },
            UserAgent = new DbString { Value = Truncate(userAgent, 300), Length = 300 },
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct));

    /// <summary>true = sesiunea exista si nu e revocata (LastSeenAt actualizat).</summary>
    public async Task<bool> TouchAsync(Guid sessionId, CancellationToken ct) =>
        await (await OpenAsync(ct)).ExecuteScalarAsync<int>(new CommandDefinition("ids.usp_UserSession_Touch",
            new { SessionId = sessionId }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)) == 1;

    public async Task RevokeAsync(Guid sessionId, CancellationToken ct) =>
        await (await OpenAsync(ct)).ExecuteAsync(new CommandDefinition("ids.usp_UserSession_Revoke",
            new { SessionId = sessionId, UserAccountId = (int?)null }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct));

    public async Task RevokeAllAsync(int userAccountId, CancellationToken ct) =>
        await (await OpenAsync(ct)).ExecuteAsync(new CommandDefinition("ids.usp_UserSession_Revoke",
            new { SessionId = (Guid?)null, UserAccountId = userAccountId }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct));

    /// <param name="lockedOut">Incercarea curenta a blocat contul: evenimentul UtilizatorBlocat intra in outbox (A20).</param>
    public async Task WriteLoginEventAsync(LoginEvent loginEvent, CancellationToken ct) =>
        await (await OpenAsync(ct)).ExecuteAsync(new CommandDefinition("core.usp_LoginEvent_Insert", new
        {
            UserName = new DbString { Value = Truncate(loginEvent.UserName, 256), Length = 256 },
            loginEvent.SubjectId,
            loginEvent.Success,
            FailureReason = new DbString { Value = loginEvent.FailureReason, Length = 50, IsAnsi = true },
            IpAddress = new DbString { Value = loginEvent.IpAddress, Length = 45, IsAnsi = true },
            UserAgent = new DbString { Value = Truncate(loginEvent.UserAgent, 300), Length = 300 },
            loginEvent.LockedOut,
            loginEvent.CorrelationId,
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct));

    private static string? Truncate(string? value, int length) => value is { Length: > 0 } && value.Length > length ? value[..length] : value;

    private async Task<System.Data.Common.DbConnection> OpenAsync(CancellationToken ct)
    {
        if (session.Connection.State != ConnectionState.Open)
        {
            await session.Connection.OpenAsync(ct);
        }

        return session.Connection;
    }
}

/// <summary>O incercare de autentificare. FailureReason: InvalidCredentials, LockedOut, NotAllowed, InvalidTwoFactorCode.</summary>
public sealed record LoginEvent(
    string UserName,
    Guid? SubjectId,
    bool Success,
    string? FailureReason,
    string? IpAddress,
    string? UserAgent,
    bool LockedOut,
    Guid CorrelationId);

public static class LoginFailures
{
    public const string InvalidCredentials = "InvalidCredentials";
    public const string LockedOut = "LockedOut";
    public const string NotAllowed = "NotAllowed";
    public const string InvalidTwoFactorCode = "InvalidTwoFactorCode";
}
