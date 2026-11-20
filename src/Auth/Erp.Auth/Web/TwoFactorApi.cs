using Erp.Auth.Pages.Account;
using Erp.Core.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using QRCoder;

namespace Erp.Auth.Web;

public sealed record TwoFactorStatusDto(bool Enabled, bool Required, string? SharedKey, string? QrSvg);

public sealed record EnableTwoFactorRequest(string Password, string Code);

public sealed record DisableTwoFactorRequest(string Password);

public sealed record RecoveryCodesDto(IReadOnlyList<string> Codes);

/// <summary>
/// Activarea si dezactivarea 2FA din ecranul „Contul meu” (02.01, 11.2), cu parola contului. Cheia TOTP se pastreaza criptata,
/// codurile de rezerva se afiseaza o singura data. 2FA obligatoriu nu se poate dezactiva; activarea obligatorie ramane pe /account/2fa.
/// </summary>
public static class TwoFactorApi
{
    public const string WrongPassword = "IDS_PASSWORD_WRONG";
    public const string InvalidCode = "IDS_TWO_FACTOR_CODE_INVALID";
    public const string DisableRefused = "IDS_TWO_FACTOR_REQUIRED";

    public static IEndpointRouteBuilder MapTwoFactorApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/account/two-factor").WithTags("Auth").RequireAuthorization();
        group.MapGet("/", StatusAsync).WithName("GetTwoFactorStatus");
        group.MapPost("/enable", EnableAsync).WithName("EnableTwoFactor");
        group.MapPost("/disable", DisableAsync).WithName("DisableTwoFactor");
        return endpoints;
    }

    private static async Task<TwoFactorStatusDto> StatusAsync(HttpContext http, UserManager<UserAccount> users, SignInManager<UserAccount> signIn)
    {
        var user = await CurrentAsync(http, users);
        if (user.TwoFactorEnabled)
        {
            return new TwoFactorStatusDto(true, user.TwoFactorRequired, null, null);
        }

        var key = await users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await users.ResetAuthenticatorKeyAsync(user);
            await signIn.RefreshSignInAsync(user);
            key = await users.GetAuthenticatorKeyAsync(user);
        }

        var uri = $"otpauth://totp/{Uri.EscapeDataString(TwoFactorModel.Issuer)}:{Uri.EscapeDataString(user.UserName)}?secret={key}&issuer={Uri.EscapeDataString(TwoFactorModel.Issuer)}&digits=6";
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
        var qr = new SvgQRCode(data).GetGraphic(4, "#0b1f3a", "#ffffff", drawQuietZones: false);
        return new TwoFactorStatusDto(false, user.TwoFactorRequired, string.Join(' ', key!.Chunk(4).Select(chunk => new string(chunk))), qr);
    }

    private static async Task<RecoveryCodesDto> EnableAsync(EnableTwoFactorRequest request, HttpContext http, UserManager<UserAccount> users,
        SignInManager<UserAccount> signIn, IStringLocalizer<AccountText> text)
    {
        var user = await CurrentAsync(http, users);
        if (user.TwoFactorEnabled)
        {
            throw new ErpException(ErpErrorKind.Conflict, "IDS_TWO_FACTOR_ALREADY_ENABLED", text["Setup2fa.AlreadyEnabled"]);
        }

        await RequirePasswordAsync(users, user, request.Password, text);
        var code = request.Code.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        if (!await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code))
        {
            throw new ErpException(ErpErrorKind.Validation, InvalidCode, text["Setup2fa.Invalid"]);
        }

        await users.SetTwoFactorEnabledAsync(user, true);
        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, TwoFactorModel.RecoveryCodeCount) ?? [];
        await signIn.RefreshSignInAsync(user);
        return new RecoveryCodesDto([.. codes]);
    }

    private static async Task<IResult> DisableAsync(DisableTwoFactorRequest request, HttpContext http, UserManager<UserAccount> users,
        SignInManager<UserAccount> signIn, IStringLocalizer<AccountText> text, ILoggerFactory loggers)
    {
        var user = await CurrentAsync(http, users);
        if (!user.TwoFactorEnabled || user.TwoFactorRequired)
        {
            throw new ErpException(ErpErrorKind.Forbidden, DisableRefused, text["Setup2fa.DisableRefused"]);
        }

        await RequirePasswordAsync(users, user, request.Password, text);
        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);
        await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 0);
        await signIn.RefreshSignInAsync(user);
        loggers.CreateLogger("Erp.Auth.TwoFactor").LogInformation("2FA dezactivat de utilizatorul {User}.", user.UserName);
        return Results.NoContent();
    }

    private static async Task<UserAccount> CurrentAsync(HttpContext http, UserManager<UserAccount> users) =>
        await users.GetUserAsync(http.User) ?? throw new ErpException(ErpErrorKind.Unauthenticated, "IDS_UNAUTHENTICATED", "Autentificați-vă.");

    private static async Task RequirePasswordAsync(UserManager<UserAccount> users, UserAccount user, string password, IStringLocalizer<AccountText> text)
    {
        if (!await users.CheckPasswordAsync(user, password))
        {
            throw new ErpException(ErpErrorKind.Validation, WrongPassword, text["Setup2fa.DisableWrongPassword"]);
        }
    }
}
