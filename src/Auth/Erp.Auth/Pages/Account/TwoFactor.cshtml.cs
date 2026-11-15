using Erp.Auth.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using QRCoder;

namespace Erp.Auth.Pages.Account;

/// <summary>
/// Activarea 2FA (02.01, 11.2; macheta 09b): cod QR si cheie manuala TOTP, verificarea unui cod de 6 cifre, apoi cele
/// 10 coduri de rezerva afisate o singura data. Cheia TOTP se pastreaza criptata, codurile doar ca hash.
/// </summary>
[Authorize]
public sealed class TwoFactorModel(
    SignInManager<UserAccount> signIn,
    UserManager<UserAccount> users,
    IStringLocalizer<AccountText> text) : PageModel
{
    public const int RecoveryCodeCount = 10;
    public const string Issuer = "ERP";

    [BindProperty]
    public string Code { get; set; } = string.Empty;

    public bool Required { get; private set; }

    public bool AlreadyEnabled { get; private set; }

    public string SharedKey { get; private set; } = string.Empty;

    public string QrSvg { get; private set; } = string.Empty;

    public IReadOnlyList<string> RecoveryCodes { get; private set; } = [];

    public string AccountUserName { get; private set; } = string.Empty;

    public string? Error { get; private set; }

    public string ContinueUrl => AccountFlow.HomePath;

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return RedirectToPage("/Account/Login");
        }

        Describe(user);
        if (user.MustChangePassword)
        {
            return RedirectToPage("/Account/Password");
        }

        if (!AlreadyEnabled)
        {
            await PrepareKeyAsync(user);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return RedirectToPage("/Account/Login");
        }

        Describe(user);
        if (user.MustChangePassword)
        {
            return RedirectToPage("/Account/Password");
        }

        if (AlreadyEnabled)
        {
            return Page();
        }

        var code = Code.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        Code = string.Empty;
        if (!await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code))
        {
            Error = text["Setup2fa.Invalid"];
            await PrepareKeyAsync(user);
            return Page();
        }

        await users.SetTwoFactorEnabledAsync(user, true);
        RecoveryCodes = [.. await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount) ?? []];
        // Amprenta de securitate s-a schimbat: cookie-ul se reemite (aceeasi sesiune), fara pasul 2FA obligatoriu.
        await signIn.RefreshSignInAsync(user);
        return Page();
    }

    private void Describe(UserAccount user)
    {
        Required = user.TwoFactorRequired;
        AlreadyEnabled = user.TwoFactorEnabled;
        AccountUserName = user.UserName;
        ViewData["UserName"] = user.UserName;
    }

    private async Task PrepareKeyAsync(UserAccount user)
    {
        var key = await users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await users.ResetAuthenticatorKeyAsync(user);
            await signIn.RefreshSignInAsync(user);
            key = await users.GetAuthenticatorKeyAsync(user);
        }

        SharedKey = string.Join(' ', key!.Chunk(4).Select(chunk => new string(chunk)));
        var uri = $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(user.UserName)}?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6";
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
        QrSvg = new SvgQRCode(data).GetGraphic(4, "#0b1f3a", "#ffffff", drawQuietZones: false);
    }
}
