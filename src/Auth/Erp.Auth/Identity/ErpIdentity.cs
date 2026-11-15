using System.Globalization;
using System.Security.Claims;
using Erp.Auth.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Erp.Auth.Identity;

/// <summary>Claim-urile cookie-ului (08, 8.7), pe langa NameIdentifier = SubjectId, Name si SecurityStamp.</summary>
public static class ErpClaims
{
    public const string SessionId = "sid";

    /// <summary>Momentul autentificarii (secunde Unix, UTC), pentru durata maxima a sesiunii.</summary>
    public const string AuthTime = "auth_time";

    public const string DisplayName = "erp:display_name";

    /// <summary>Parola trebuie schimbata inainte de a intra in aplicatie (prima autentificare, resetare de administrator).</summary>
    public const string MustChangePassword = "erp:password_change";

    /// <summary>2FA e obligatoriu pentru cont, dar inca neactivat.</summary>
    public const string TwoFactorSetupRequired = "erp:2fa_setup";
}

/// <summary>Optiunile autentificarii (08, 8.7 si 8.10: sectiunea Auth din appsettings).</summary>
public sealed class AuthOptions
{
    public const string Section = "Auth";

    public int IdleTimeoutMinutes { get; set; } = 30;

    public int AbsoluteTimeoutHours { get; set; } = 10;

    /// <summary>Blocarea temporara dupa 5 incercari gresite (02.01, 11; durata propusa 15 minute, configurabila).</summary>
    public int LockoutMinutes { get; set; } = 15;

    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>DataRoot\&lt;Instanta&gt;\install\initial-admin.txt (08, 8.8), calculat din Erp:DataRoot si Erp:Instance.</summary>
    public string? InitialAdminFilePath { get; set; }
}

/// <summary>Claim-urile utilizatorului: cele standard Identity plus numele afisat si pasii obligatorii.</summary>
public sealed class ErpClaimsPrincipalFactory(UserManager<UserAccount> users, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<UserAccount>(users, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(UserAccount user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(ErpClaims.DisplayName, user.DisplayName));
        if (user.MustChangePassword)
        {
            identity.AddClaim(new Claim(ErpClaims.MustChangePassword, "true"));
        }

        if (user.TwoFactorRequired && !user.TwoFactorEnabled)
        {
            identity.AddClaim(new Claim(ErpClaims.TwoFactorSetupRequired, "true"));
        }

        return identity;
    }
}

/// <summary>
/// SignInManager cu sesiuni (08, 8.7): fiecare autentificare noua deschide o sesiune in ids.UserSession, al carei Id intra
/// in cookie (claim sid); reemiterea cookie-ului pentru acelasi utilizator (dupa 2FA, parola) pastreaza sesiunea si
/// momentul autentificarii. Deconectarea revoca sesiunea. Doar conturile active (si utilizatorii Nucleului activi) intra.
/// </summary>
public sealed class ErpSignInManager(
    UserManager<UserAccount> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<UserAccount> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<UserAccount>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<UserAccount> confirmation,
    AuthSessionStore sessions,
    TimeProvider time)
    : SignInManager<UserAccount>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(UserAccount user) => user.CanSignIn && await base.CanSignInAsync(user);

    public override async Task SignInWithClaimsAsync(UserAccount user, AuthenticationProperties? authenticationProperties, IEnumerable<Claim> additionalClaims)
    {
        var current = Context.User;
        var sameUser = current.Identity?.IsAuthenticated == true
            && string.Equals(current.FindFirstValue(ClaimTypes.NameIdentifier), user.SubjectId.ToString(), StringComparison.OrdinalIgnoreCase);
        var sessionClaim = sameUser ? current.FindFirst(ErpClaims.SessionId) : null;
        var authTimeClaim = sameUser ? current.FindFirst(ErpClaims.AuthTime) : null;

        if (sessionClaim is null || !Guid.TryParse(sessionClaim.Value, out _))
        {
            var sessionId = Guid.NewGuid();
            await sessions.CreateAsync(sessionId, user.Id, Context.Connection.RemoteIpAddress?.ToString(), Context.Request.Headers.UserAgent.ToString(),
                Context.RequestAborted);
            sessionClaim = new Claim(ErpClaims.SessionId, sessionId.ToString());
            authTimeClaim = null;
        }

        authTimeClaim ??= new Claim(ErpClaims.AuthTime, time.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64);
        await base.SignInWithClaimsAsync(user, authenticationProperties,
            additionalClaims.Where(c => c.Type is not ErpClaims.SessionId and not ErpClaims.AuthTime).Append(sessionClaim).Append(authTimeClaim));
    }

    public override async Task SignOutAsync()
    {
        if (Guid.TryParse(Context.User.FindFirstValue(ErpClaims.SessionId), out var sessionId))
        {
            await sessions.RevokeAsync(sessionId, CancellationToken.None);
        }

        await base.SignOutAsync();
    }
}

/// <summary>
/// Verificarea periodica a cookie-ului (cel mult o data pe minut, 8.7): SecurityStamp neschimbat, contul inca activ si
/// sesiunea nerevocata. Altfel cookie-ul e respins: dezactivarea, schimbarea parolei sau „Inchide sesiunea” au efect
/// in cel mult un minut.
/// </summary>
public sealed class ErpSecurityStampValidator(
    IOptions<SecurityStampValidatorOptions> options,
    SignInManager<UserAccount> signInManager,
    ILoggerFactory logger,
    AuthSessionStore sessions)
    : SecurityStampValidator<UserAccount>(options, signInManager, logger)
{
    protected override async Task<UserAccount?> VerifySecurityStamp(ClaimsPrincipal? principal)
    {
        var user = await base.VerifySecurityStamp(principal);
        if (user is null || !user.CanSignIn)
        {
            return null;
        }

        return Guid.TryParse(principal?.FindFirstValue(ErpClaims.SessionId), out var sessionId)
            && await sessions.TouchAsync(sessionId, CancellationToken.None)
            ? user
            : null;
    }
}

/// <summary>Reguli de parola peste cele Identity (02.01, 11.2): fara numele de utilizator, diferita de ultimele 5 parole.</summary>
public sealed class ErpPasswordValidator(UserAccountStore store, IPasswordHasher<UserAccount> hasher, ErpIdentityErrorDescriber errors)
    : IPasswordValidator<UserAccount>
{
    public const int HistoryCount = 5;

    public async Task<IdentityResult> ValidateAsync(UserManager<UserAccount> manager, UserAccount user, string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return IdentityResult.Success;
        }

        var failures = new List<IdentityError>();
        if (password.Contains(user.UserName, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(errors.PasswordContainsUserName());
        }

        var recent = (await store.RecentPasswordHashesAsync(user, HistoryCount, CancellationToken.None)).ToList();
        if (user.PasswordHash is not null)
        {
            recent.Add(user.PasswordHash);
        }

        if (recent.Any(hash => hasher.VerifyHashedPassword(user, hash, password) != PasswordVerificationResult.Failed))
        {
            failures.Add(errors.PasswordReused());
        }

        return failures.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. failures]);
    }
}

/// <summary>Mesajele Identity in romana, din resursele AccountText (UI in romana, cu resurse de traducere).</summary>
public sealed class ErpIdentityErrorDescriber(IStringLocalizer<AccountText> text) : IdentityErrorDescriber
{
    public override IdentityError PasswordTooShort(int length) => Error(nameof(PasswordTooShort), length);

    public override IdentityError PasswordRequiresDigit() => Error(nameof(PasswordRequiresDigit));

    public override IdentityError PasswordRequiresLower() => Error(nameof(PasswordRequiresLower));

    public override IdentityError PasswordRequiresUpper() => Error(nameof(PasswordRequiresUpper));

    public override IdentityError PasswordRequiresNonAlphanumeric() => Error(nameof(PasswordRequiresNonAlphanumeric));

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => Error(nameof(PasswordRequiresUniqueChars), uniqueChars);

    public override IdentityError PasswordMismatch() => Error(nameof(PasswordMismatch));

    public override IdentityError InvalidToken() => Error(nameof(InvalidToken));

    public override IdentityError ConcurrencyFailure() => Error(nameof(ConcurrencyFailure));

    public override IdentityError DefaultError() => Error(nameof(DefaultError));

    public IdentityError PasswordContainsUserName() => Error(nameof(PasswordContainsUserName));

    public IdentityError PasswordReused() => Error(nameof(PasswordReused), ErpPasswordValidator.HistoryCount);

    private IdentityError Error(string code, params object[] arguments) => new() { Code = code, Description = text["Error." + code, arguments] };
}