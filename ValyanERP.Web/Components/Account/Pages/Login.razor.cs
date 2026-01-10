using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.ComponentModel.DataAnnotations;

namespace ValyanERP.Web.Components.Account.Pages;

/// <summary>
/// Code-behind for Login page.
/// Uses API Controller for authentication to properly set Identity cookies.
/// </summary>
public partial class Login : ComponentBase
{
    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    public IJSRuntime JSRuntime { get; set; } = default!;

    [SupplyParameterFromForm]
    private LoginInputModel? LoginModel { get; set; }

    private string? errorMessage;
    
    // UI State
    private bool isLoading = false;
    private bool showPassword = false;
    
    // Password Toggle computed properties
    private string PasswordInputType => showPassword ? "text" : "password";
    private string PasswordToggleIcon => showPassword ? "bi-eye-slash" : "bi-eye";
    private string PasswordToggleAriaLabel => showPassword ? "Ascunde parola" : "Afișează parola";
    private string PasswordToggleTitle => showPassword ? "Ascunde parola" : "Afișează parola";

    protected override void OnInitialized()
    {
        LoginModel ??= new();
    }

    private void TogglePasswordVisibility()
    {
        showPassword = !showPassword;
    }
    
    private async Task HandleLogin()
    {
        if (LoginModel == null) return;
        errorMessage = null;
        isLoading = true;
        StateHasChanged();

        try
        {
            // Call API Controller via JavaScript fetch
            // API Controller can properly set Identity cookies via HTTP response
            var result = await JSRuntime.InvokeAsync<LoginApiResult>("ValyanAuth.login", 
                LoginModel.Email, 
                LoginModel.Password);

            if (result.Success)
            {
                // Redirect with force load to establish authentication context
                NavigationManager.NavigateTo(result.RedirectUrl ?? "/", forceLoad: true);
            }
            else
            {
                errorMessage = result.Message ?? "Email sau parolă incorectă.";
            }
        }
        catch (Exception ex)
        {
            errorMessage = "A apărut o eroare. Încercați din nou.";
            Console.WriteLine($"Login error: {ex.Message}");
        }
        finally
        {
            isLoading = false;
            StateHasChanged();
        }
    }

    /// <summary>
    /// Result from login API call
    /// </summary>
    private class LoginApiResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? RedirectUrl { get; set; }
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
