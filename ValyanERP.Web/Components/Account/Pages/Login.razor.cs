using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.ComponentModel.DataAnnotations;
using ValyanERP.Web.Infrastructure.Data;
using Dapper;

namespace ValyanERP.Web.Components.Account.Pages;

/// <summary>
/// Code-behind for Login page.
/// Uses API Controller for authentication to properly set Identity cookies.
/// Includes entity selection for multi-company/location access.
/// </summary>
public partial class Login : ComponentBase
{
    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    public IJSRuntime JSRuntime { get; set; } = default!;
    
    [Inject]
    public DapperContext DbContext { get; set; } = default!;

    [SupplyParameterFromForm]
    private LoginInputModel? LoginModel { get; set; }

    private string? errorMessage;
    
    // UI State
    private bool isLoading = false;
    private bool showPassword = false;
    private bool isLoadingEntities = false;
    
    // Entity selection state
    private List<UserEntityAccess> availableEntities = new();
    private string selectedEntityId = string.Empty;
    private string lastCheckedEmail = string.Empty;
    
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
    
    /// <summary>
    /// Called when email changes - loads available entities for user
    /// </summary>
    private async Task OnEmailChanged()
    {
        if (LoginModel == null) return;
        
        var email = LoginModel.Email?.Trim();
        
        // Skip if email is empty or same as last checked
        if (string.IsNullOrWhiteSpace(email) || email == lastCheckedEmail)
            return;
        
        // Basic email validation
        if (!email.Contains('@') || email.Length < 5)
            return;
        
        lastCheckedEmail = email;
        isLoadingEntities = true;
        availableEntities.Clear();
        selectedEntityId = string.Empty;
        StateHasChanged();
        
        try
        {
            using var connection = DbContext.CreateConnection();
            
            // Query user's accessible entities (locations with full hierarchy info)
            // We don't verify password here - just show what entities exist
            // Security: Only shows entities, not user existence
            var entities = await connection.QueryAsync<UserEntityAccess>(@"
                SELECT 
                    uoa.EntityType,
                    uoa.EntityId,
                    uoa.AccessLevel,
                    CASE uoa.EntityType
                        WHEN 'LOCATION' THEN l.Denumire + ' (' + cl.Denumire + ')'
                        WHEN 'WORKPLACE' THEN wp.Denumire + ' (' + cwp.Denumire + ')'
                        WHEN 'COMPANY' THEN c2.Denumire
                        WHEN 'GROUP' THEN g.Denumire
                    END AS DisplayName,
                    CASE uoa.EntityType
                        WHEN 'LOCATION' THEN l.CompanyId
                        WHEN 'WORKPLACE' THEN wp.CompanyId
                        WHEN 'COMPANY' THEN c2.Id
                        ELSE NULL
                    END AS CompanyId,
                    CASE uoa.EntityType
                        WHEN 'LOCATION' THEN l.WorkPlaceId
                        ELSE NULL
                    END AS WorkPlaceId
                FROM UserOrganizationalAccess uoa
                INNER JOIN Users u ON uoa.UserId = u.Id
                LEFT JOIN Locations l ON uoa.EntityType = 'LOCATION' AND uoa.EntityId = l.Id
                LEFT JOIN Companies cl ON l.CompanyId = cl.Id
                LEFT JOIN WorkPlaces wp ON uoa.EntityType = 'WORKPLACE' AND uoa.EntityId = wp.Id
                LEFT JOIN Companies cwp ON wp.CompanyId = cwp.Id
                LEFT JOIN Companies c2 ON uoa.EntityType = 'COMPANY' AND uoa.EntityId = c2.Id
                LEFT JOIN CompanyGroups g ON uoa.EntityType = 'GROUP' AND uoa.EntityId = g.Id
                WHERE u.NormalizedEmail = UPPER(@Email)
                  AND u.IsActive = 1
                  AND uoa.AccessLevel >= 1
                ORDER BY 
                    CASE uoa.EntityType 
                        WHEN 'LOCATION' THEN 1 
                        WHEN 'WORKPLACE' THEN 2 
                        WHEN 'COMPANY' THEN 3 
                        WHEN 'GROUP' THEN 4 
                    END,
                    DisplayName",
                new { Email = email });
            
            availableEntities = entities.Where(e => !string.IsNullOrEmpty(e.DisplayName)).ToList();
            
            // Auto-select if only one entity
            if (availableEntities.Count == 1)
            {
                selectedEntityId = availableEntities[0].EntityId.ToString();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading entities: {ex.Message}");
            // Don't show error to user - just proceed without entity selection
        }
        finally
        {
            isLoadingEntities = false;
            StateHasChanged();
        }
    }
    
    private async Task HandleLogin()
    {
        if (LoginModel == null) return;
        errorMessage = null;
        isLoading = true;
        StateHasChanged();

        try
        {
            // Validate entity selection if entities are available
            if (availableEntities.Count > 0 && string.IsNullOrEmpty(selectedEntityId))
            {
                errorMessage = "Selectați punctul de lucru.";
                isLoading = false;
                StateHasChanged();
                return;
            }
            
            // Get entity type for selected entity
            var entityType = availableEntities.FirstOrDefault(e => e.EntityId.ToString() == selectedEntityId)?.EntityType ?? "";
            
            // Call API Controller via JavaScript fetch
            // API Controller can properly set Identity cookies via HTTP response
            var result = await JSRuntime.InvokeAsync<LoginApiResult>("ValyanAuth.login", 
                LoginModel.Email, 
                LoginModel.Password,
                selectedEntityId,
                entityType);

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
    /// User's accessible entity for selection
    /// </summary>
    private class UserEntityAccess
    {
        public string EntityType { get; set; } = string.Empty;
        public Guid EntityId { get; set; }
        public int AccessLevel { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public Guid? CompanyId { get; set; }
        public Guid? WorkPlaceId { get; set; }
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
