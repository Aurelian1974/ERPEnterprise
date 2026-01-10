using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Syncfusion.Blazor.Grids;
using ValyanERP.Web.Features.Administrare.Persoane.Models;
using ValyanERP.Web.Features.Administrare.Utilizatori.Models;
using ValyanERP.Web.Features.Administrare.Utilizatori.Services;
using ValyanERP.Web.Features.Administrare.Utilizatori;

namespace ValyanERP.Web.Components.Pages.Administrare;

/// <summary>
/// Code-behind for Utilizatori page.
/// Contains ALL business logic, state management, and event handlers.
/// </summary>
public partial class Utilizatori : ComponentBase
{
    [Inject] public IUsersService UsersService { get; set; } = default!;
    [Inject] private ILogger<Utilizatori> Logger { get; set; } = default!;

    private SfGrid<User>? grid;
    private IEnumerable<Persoana> persoaneList = new List<Persoana>();
    private string? errorMessage;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            errorMessage = null;
            
            // Load available persons for dropdown
            persoaneList = await UsersService.GetAvailablePersonsAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading persons: {Message}", ex.Message);
            errorMessage = "Eroare la încărcarea listei de persoane. Vă rugăm reîncărcați pagina.";
            persoaneList = new List<Persoana>();
        }
    }
    
    /// <summary>
    /// Handles grid action events (Save, Delete, etc.)
    /// </summary>
    public async Task ActionBeginHandler(ActionEventArgs<User> args)
    {
        try
        {
            errorMessage = null; // Clear previous errors
            
            if (args.RequestType == Syncfusion.Blazor.Grids.Action.Save)
            {
                // Grid will handle save via adaptor
            }

            if (args.RequestType == Syncfusion.Blazor.Grids.Action.Delete)
            {
                // Grid will handle delete via adaptor
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in ActionBeginHandler: {Message}", ex.Message);
            errorMessage = $"Eroare: {ex.Message}";
            args.Cancel = true; // Cancel the operation
        }

        await Task.CompletedTask;
    }
    
    /// <summary>
    /// Handles grid action completion events.
    /// </summary>
    public void ActionCompleteHandler(ActionEventArgs<User> args)
    {
        try
        {
            if (args.RequestType == Syncfusion.Blazor.Grids.Action.Save)
            {
                errorMessage = null;
            }

            if (args.RequestType == Syncfusion.Blazor.Grids.Action.Delete)
            {
                errorMessage = null;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in ActionCompleteHandler: {Message}", ex.Message);
            errorMessage = $"Eroare: {ex.Message}";
        }
    }
    
    /// <summary>
    /// Handles grid action failure events.
    /// </summary>
    public void ActionFailureHandler(Syncfusion.Blazor.Grids.FailureEventArgs args)
    {
        Logger.LogError("Grid action failed: {Error}", args.Error);
        errorMessage = "A apărut o eroare. Vă rugăm încercați din nou.";
    }
}
