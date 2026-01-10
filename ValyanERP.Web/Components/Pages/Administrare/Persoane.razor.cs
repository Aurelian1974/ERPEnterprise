using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Syncfusion.Blazor.Grids;
using ValyanERP.Web.Features.Administrare.Persoane.Models;
using ValyanERP.Web.Features.Administrare.Persoane;

namespace ValyanERP.Web.Components.Pages.Administrare;

/// <summary>
/// Code-behind for Persoane page.
/// Contains ALL business logic, state management, and event handlers.
/// </summary>
public partial class Persoane : ComponentBase
{
    [Inject] private ILogger<Persoane> Logger { get; set; } = default!;
    
    private SfGrid<Persoana>? grid;
    private string? errorMessage;

    /// <summary>
    /// Handles grid action events (Save, Delete, etc.)
    /// </summary>
    public async Task ActionBeginHandler(ActionEventArgs<Persoana> args)
    {
        try
        {
            errorMessage = null; // Clear previous errors
            
            if (args.RequestType == Syncfusion.Blazor.Grids.Action.Save)
            {
                Logger.LogDebug("Grid Save action initiated for Persoana");
                
                // Validation is handled by:
                // 1. FluentValidation (PersoanaValidator)
                // 2. EditForm in Grid Template
                // 3. Server-side validation in Repository/Service
                
                // The grid will automatically call the adaptor's Insert/Update methods
                // which will use the repository with stored procedures
            }

            if (args.RequestType == Syncfusion.Blazor.Grids.Action.Delete)
            {
                Logger.LogDebug("Grid Delete action initiated for Persoana Id={Id}", args.Data?.Id);
                
                // Soft delete is handled by sp_Persoane_Delete stored procedure
                // via the repository
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
    public void ActionCompleteHandler(ActionEventArgs<Persoana> args)
    {
        try
        {
            if (args.RequestType == Syncfusion.Blazor.Grids.Action.Save)
            {
                Logger.LogInformation("Persoana saved successfully");
                errorMessage = null;
            }

            if (args.RequestType == Syncfusion.Blazor.Grids.Action.Delete)
            {
                Logger.LogInformation("Persoana deleted successfully");
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
