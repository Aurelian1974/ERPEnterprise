using Erp.Auth.Data;
using Erp.Auth.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Erp.Auth.Pages.Account;

/// <summary>
/// Parola noua (02.01, 11.2; macheta 09): obligatorie la prima autentificare si dupa resetarea de catre administrator;
/// altfel cere parola curenta. Dupa salvare se inchid toate sesiunile, iar fisierul initial-admin.txt al instalarii se
/// sterge (08, 8.8).
/// </summary>
[Authorize]
public sealed class PasswordModel(
    SignInManager<UserAccount> signIn,
    UserManager<UserAccount> users,
    AuthSessionStore sessions,
    IOptions<AuthOptions> options,
    IStringLocalizer<AccountText> text,
    ILogger<PasswordModel> log) : PageModel
{
    [BindProperty]
    public string? CurrentPassword { get; set; }

    [BindProperty]
    public string NewPassword { get; set; } = string.Empty;

    [BindProperty]
    public string ConfirmPassword { get; set; } = string.Empty;

    public bool Forced { get; private set; }

    public string AccountUserName { get; private set; } = string.Empty;

    public List<string> Errors { get; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return RedirectToPage("/Account/Login");
        }

        Describe(user);
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
        var newPassword = NewPassword;
        var confirm = ConfirmPassword;
        var current = CurrentPassword;
        NewPassword = ConfirmPassword = string.Empty;
        CurrentPassword = null;

        if (newPassword.Length == 0 || confirm.Length == 0 || (!Forced && string.IsNullOrEmpty(current)))
        {
            Errors.Add(text["Password.Required"]);
            return Page();
        }

        if (!string.Equals(newPassword, confirm, StringComparison.Ordinal))
        {
            Errors.Add(text["Password.Mismatch"]);
            return Page();
        }

        // Indicatorul se salveaza odata cu parola noua (o singura actualizare a contului).
        user.MustChangePassword = false;
        var result = Forced
            ? await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), newPassword)
            : await users.ChangePasswordAsync(user, current!, newPassword);
        if (!result.Succeeded)
        {
            Errors.AddRange(result.Errors.Select(e => e.Description));
            return Page();
        }

        await sessions.RevokeAllAsync(user.Id, HttpContext.RequestAborted);
        DeleteInitialAdminFile(user);
        await signIn.SignOutAsync();
        return RedirectToPage("/Account/Login", new { Message = "changed" });
    }

    private void Describe(UserAccount user)
    {
        Forced = user.MustChangePassword;
        AccountUserName = user.UserName;
        ViewData["UserName"] = user.UserName;
    }

    // 08, 8.8: parola initiala scrisa la instalarea --unattended se sterge dupa prima schimbare reusita a administratorului.
    private void DeleteInitialAdminFile(UserAccount user)
    {
        var path = options.Value.InitialAdminFilePath;
        if (!user.IsSystem || string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
        {
            return;
        }

        try
        {
            System.IO.File.Delete(path);
            log.LogInformation("Fișierul cu parola inițială a administratorului a fost șters");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(ex, "Fișierul cu parola inițială a administratorului nu a putut fi șters");
        }
    }
}
