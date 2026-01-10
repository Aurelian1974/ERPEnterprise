using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Identity;
using Microsoft.JSInterop;
using System.ComponentModel.DataAnnotations;
using ValyanERP.Web.Features.Infrastructure.Sessions;
using ValyanERP.Web.Infrastructure.Identity;

namespace ValyanERP.Web.Components.Account.Pages;

/// <summary>
/// Code-behind for Login page.
/// Contains ALL authentication logic, session management, and event handlers.
/// </summary>
public partial class Login : ComponentBase
{
    [Inject]
    public SignInManager<ApplicationUser> SignInManager { get; set; } = default!;

    [Inject]
    public UserManager<ApplicationUser> UserManager { get; set; } = default!;

    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    public ISessionService SessionService { get; set; } = default!;

    [Inject]
    public IJSRuntime JSRuntime { get; set; } = default!;

    [Inject]
    public Microsoft.AspNetCore.Http.IHttpContextAccessor HttpContextAccessor { get; set; } = default!;

    [SupplyParameterFromForm]
    private LoginInputModel? LoginModel { get; set; }

    private string? errorMessage;
    private string? cookieToSet;
    private bool cookieSetAttempted = false;

    protected override void OnInitialized()
    {
        LoginModel ??= new();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Fallback: Set cookie via JSInterop if server-side cookie setting failed
        if (!string.IsNullOrWhiteSpace(cookieToSet) && !cookieSetAttempted)
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("eval",
                    $"document.cookie = 'ValyanERP.Session={cookieToSet}; path=/; Secure; SameSite=Lax;';");
                cookieSetAttempted = true;
                cookieToSet = null;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to set cookie via JSInterop: " + ex.Message);
            }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    private async Task HandleLogin()
    {
        if (LoginModel == null) return;
        errorMessage = null;

        try
        {
            // Find user by email
            var user = await UserManager.FindByEmailAsync(LoginModel.Email!);
            if (user == null)
            {
                errorMessage = "Email sau parolă incorectă.";
                return;
            }

            // Attempt sign in
            // ⚠️ SECURITY: isPersistent = false (session cookie, expires when browser closes)
            var result = await SignInManager.PasswordSignInAsync(
                user,
                LoginModel.Password!,
                isPersistent: false, // CRITICAL: Always false for security
                lockoutOnFailure: true);

            if (result.Succeeded)
            {
                // Create a server-side session
                var session = await SessionService.CreateSessionAsync(user.Id);
                var token = session.Token.ToString();

                // Prefer setting cookie server-side if HttpContext is available
                var httpContext = HttpContextAccessor?.HttpContext;
                if (httpContext != null)
                {
                    var cookieOptions = new Microsoft.AspNetCore.Http.CookieOptions
                    {
                        HttpOnly = true,
                        Secure = true,
                        SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax
                        // Session cookie (no Expires = browser session)
                    };
                    httpContext.Response.Cookies.Append("ValyanERP.Session", token, cookieOptions);
                }
                else
                {
                    // Fallback: set cookie on next render via JSInterop (OnAfterRenderAsync)
                    cookieToSet = token;
                }

                NavigationManager.NavigateTo("/", forceLoad: true);
            }
            else if (result.IsLockedOut)
            {
                errorMessage = "Contul este blocat temporar. Încercați mai târziu.";
            }
            else
            {
                errorMessage = "Email sau parolă incorectă.";
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"Eroare: {ex.Message}";
        }
    }

    /// <summary>
    /// Input model for login form.
    /// </summary>
    public class LoginInputModel
    {
        [Required(ErrorMessage = "Email-ul este obligatoriu")]
        [EmailAddress(ErrorMessage = "Email invalid")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Parola este obligatorie")]
        public string Password { get; set; } = "";

        public bool RememberMe { get; set; }
    }
}
