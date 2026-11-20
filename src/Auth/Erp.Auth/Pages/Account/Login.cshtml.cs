using Erp.Auth.Data;
using Erp.Auth.Identity;
using Erp.Auth.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace Erp.Auth.Pages.Account;

/// <summary>
/// Autentificarea (02.01, 11; macheta 01): numele se cauta fara diferenta de majuscule (IA-06), blocare temporara dupa
/// 5 incercari gresite, fiecare incercare in core.LoginEvent. Mesajul de eroare nu spune daca utilizatorul exista.
/// </summary>
[AllowAnonymous]
public sealed class LoginModel(
    SignInManager<UserAccount> signIn,
    UserManager<UserAccount> users,
    AccountFlow flow,
    IOptions<AuthOptions> options,
    TimeProvider clock,
    IStringLocalizer<AccountText> text) : PageModel
{
    [BindProperty]
    public string UserName { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    public bool RememberMe { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    /// <summary>changed = parola schimbata, signedout = iesire.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Message { get; set; }

    public string? Error { get; private set; }

    public string? Info => Message switch
    {
        "changed" => text["Login.PasswordChanged"].Value,
        "signedout" => text["Login.SignedOut"].Value,
        _ => null,
    };

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Message = null;
        var userName = UserName.Trim();
        if (userName.Length == 0 || Password.Length == 0)
        {
            Error = text["Login.Required"];
            return Page();
        }

        var user = await users.FindByNameAsync(userName);
        var wasLocked = await flow.IsLockedOutAsync(user);
        var result = user is null ? SignInResult.Failed : await signIn.PasswordSignInAsync(user, Password, RememberMe, lockoutOnFailure: true);
        Password = string.Empty;

        if (result.Succeeded)
        {
            await flow.RecordAsync(userName, user, success: true, null, lockedOut: false);
            return LocalRedirect(AccountFlow.NextStep(user!, ReturnUrl, Url));
        }

        if (result.RequiresTwoFactor)
        {
            return RedirectToPage("/Account/LoginWith2fa", new { ReturnUrl, RememberMe });
        }

        if (result.IsLockedOut)
        {
            await flow.RecordAsync(userName, user, success: false, LoginFailures.LockedOut, lockedOut: !wasLocked);
            // Minutele rămase, nu durata configurată: blocarea poate fi început mai devreme.
            var lockoutEnd = user is null ? null : await users.GetLockoutEndDateAsync(user);
            Error = text["Login.LockedOut", AccountFlow.RemainingLockoutMinutes(lockoutEnd, clock.GetUtcNow(), options.Value.LockoutMinutes)];
        }
        else if (result.IsNotAllowed)
        {
            await flow.RecordAsync(userName, user, success: false, LoginFailures.NotAllowed, lockedOut: false);
            Error = text["Login.NotAllowed"];
        }
        else
        {
            await flow.RecordAsync(userName, user, success: false, LoginFailures.InvalidCredentials, lockedOut: false);
            Error = text["Login.Invalid"];
        }

        return Page();
    }
}
