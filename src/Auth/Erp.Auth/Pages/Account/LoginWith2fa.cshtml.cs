using Erp.Auth.Data;
using Erp.Auth.Identity;
using Erp.Auth.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Erp.Auth.Pages.Account;

/// <summary>Al doilea pas al autentificarii (macheta 09b): codul TOTP de 6 cifre sau un cod de rezerva de unica folosinta.</summary>
[AllowAnonymous]
public sealed class LoginWith2faModel(
    SignInManager<UserAccount> signIn,
    AccountFlow flow,
    IOptions<AuthOptions> options,
    IStringLocalizer<AccountText> text) : PageModel
{
    [BindProperty]
    public string Code { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public bool UseRecoveryCode { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool RememberMe { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync() =>
        await signIn.GetTwoFactorAuthenticationUserAsync() is null ? RedirectToPage("/Account/Login") : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            return RedirectToPage("/Account/Login");
        }

        var code = Code.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).Trim();
        Code = string.Empty;
        var wasLocked = await flow.IsLockedOutAsync(user);
        var result = UseRecoveryCode
            ? await signIn.TwoFactorRecoveryCodeSignInAsync(code)
            : await signIn.TwoFactorAuthenticatorSignInAsync(code, RememberMe, rememberClient: false);

        if (result.Succeeded)
        {
            await flow.RecordAsync(user.UserName, user, success: true, null, lockedOut: false);
            return LocalRedirect(AccountFlow.NextStep(user, ReturnUrl, Url));
        }

        if (result.IsLockedOut)
        {
            await flow.RecordAsync(user.UserName, user, success: false, LoginFailures.LockedOut, lockedOut: !wasLocked);
            Error = text["Login.LockedOut", options.Value.LockoutMinutes];
        }
        else
        {
            await flow.RecordAsync(user.UserName, user, success: false, LoginFailures.InvalidTwoFactorCode, lockedOut: false);
            Error = text["TwoFactor.Invalid"];
        }

        return Page();
    }
}
