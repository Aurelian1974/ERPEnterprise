using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Erp.Auth.Pages.Account;

/// <summary>Iesirea: revoca sesiunea curenta si sterge cookie-ul. Doar POST, cu antiforgery.</summary>
[AllowAnonymous]
public sealed class LogoutModel(SignInManager<UserAccount> signIn) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Account/Login");

    public async Task<IActionResult> OnPostAsync()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            await signIn.SignOutAsync();
        }

        return RedirectToPage("/Account/Login", new { Message = "signedout" });
    }
}
