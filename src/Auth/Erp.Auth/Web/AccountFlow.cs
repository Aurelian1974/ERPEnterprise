using Erp.Auth.Data;
using Erp.Core.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Auth.Web;

/// <summary>
/// Fluxul comun al paginilor /account/*: jurnalul incercarilor (core.LoginEvent, cu UtilizatorBlocat in outbox la blocare)
/// si pasul urmator dupa autentificare (parola de schimbat, 2FA de activat, apoi aplicatia).
/// </summary>
public sealed class AccountFlow(AuthSessionStore sessions, ICurrentContext context, IHttpContextAccessor http, UserManager<UserAccount> users)
{
    public const string HomePath = "/";

    public Task RecordAsync(string userName, UserAccount? user, bool success, string? failureReason, bool lockedOut)
    {
        var request = http.HttpContext!;
        return sessions.WriteLoginEventAsync(new LoginEvent(userName, user?.SubjectId, success, success ? null : failureReason,
            request.Connection.RemoteIpAddress?.ToString(), request.Request.Headers.UserAgent.ToString(), lockedOut, context.CorrelationId), CancellationToken.None);
    }

    public async Task<bool> IsLockedOutAsync(UserAccount? user) => user is not null && await users.IsLockedOutAsync(user);

    /// <summary>Unde merge utilizatorul dupa autentificare: pasii obligatorii au prioritate fata de returnUrl.</summary>
    public static string NextStep(UserAccount user, string? returnUrl, IUrlHelper url) =>
        user.MustChangePassword ? "/account/password"
        : user.TwoFactorRequired && !user.TwoFactorEnabled ? "/account/2fa"
        : SafeReturnUrl(returnUrl, url);

    public static string SafeReturnUrl(string? returnUrl, IUrlHelper url) =>
        !string.IsNullOrEmpty(returnUrl) && url.IsLocalUrl(returnUrl) && !returnUrl.StartsWith("/account", StringComparison.OrdinalIgnoreCase)
            ? returnUrl
            : HomePath;
}
