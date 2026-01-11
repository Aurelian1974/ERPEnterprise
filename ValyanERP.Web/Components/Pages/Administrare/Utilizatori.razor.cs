using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Syncfusion.Blazor.Grids;
using Syncfusion.Blazor.DropDowns;
using Syncfusion.Blazor.Navigations;
using ValyanERP.Web.Components.Shared.DataGrid;
using ValyanERP.Web.Features.Administrare.Persoane.Models;
using ValyanERP.Web.Features.Administrare.Utilizatori.Models;
using ValyanERP.Web.Features.Administrare.Utilizatori.Services;

namespace ValyanERP.Web.Components.Pages.Administrare;

/// <summary>
/// Code-behind for Utilizatori page.
/// Implements advanced SfDataGrid features:
/// - Server-side pagination, sorting, filtering, grouping
/// - Column reordering, resizing, visibility persistence
/// - User-specific grid configuration saving
/// - Excel/PDF export
/// - Custom cell templates
/// </summary>
public partial class Utilizatori : ComponentBase, IDisposable
{
    #region Injected Services
    
    [Inject] 
    public IUsersService UsersService { get; set; } = default!;
    
    [Inject] 
    private ILogger<Utilizatori> Logger { get; set; } = default!;
    
    [Inject]
    private IJSRuntime JS { get; set; } = default!;
    
    #endregion

    #region Grid References
    
    /// <summary>
    /// Reference to the main SfGrid component.
    /// </summary>
    private SfGrid<User>? grid;
    
    /// <summary>
    /// Reference to the GridStateManager component.
    /// </summary>
    private GridStateManager? gridStateManager;
    
    #endregion

    #region State Variables
    
    /// <summary>
    /// List of persons for dropdown in edit dialog.
    /// </summary>
    private IEnumerable<Persoana> persoaneList = new List<Persoana>();
    
    /// <summary>
    /// Error message to display to user.
    /// </summary>
    private string? errorMessage;
    
    /// <summary>
    /// Success message to display to user.
    /// </summary>
    private string? successMessage;
    
    /// <summary>
    /// Total number of records (for display).
    /// </summary>
    private int totalRecords = 0;
    
    /// <summary>
    /// Currently selected user (for operations).
    /// </summary>
    private User? selectedUser;
    
    /// <summary>
    /// Selected user name for delete confirmation.
    /// </summary>
    private string selectedUserName = string.Empty;
    
    /// <summary>
    /// Whether to show delete confirmation dialog.
    /// </summary>
    private bool showDeleteConfirm = false;
    
    /// <summary>
    /// Password for new user creation.
    /// </summary>
    private string newUserPassword = string.Empty;
    
    /// <summary>
    /// Whether there is a row selected.
    /// </summary>
    private bool hasSelection = false;
    
    #endregion

    #region Grid Configuration
    
    /// <summary>
    /// Default page size.
    /// </summary>
    private int pageSize = 20;
    
    /// <summary>
    /// Available page size options.
    /// </summary>
    private int[] pageSizeOptions = new[] { 10, 20, 50, 100, 200 };
    
    /// <summary>
    /// Toolbar items.
    /// </summary>
    private List<object> toolbar = new()
    {
        "Add",
        "Edit", 
        "Delete",
        new ItemModel { Text = "Reîmprospătează", TooltipText = "Reîmprospătează datele", PrefixIcon = "e-icons e-refresh", Id = "Refresh" },
        "Search",
        new ItemModel { Type = ItemType.Separator },
        "ExcelExport",
        "PdfExport",
        new ItemModel { Type = ItemType.Separator },
        "ColumnChooser"
    };
    
    /// <summary>
    /// Status filter options for IsActive column.
    /// </summary>
    private List<StatusItem> statusOptions = new()
    {
        new StatusItem { Text = "Toate", Value = "" },
        new StatusItem { Text = "Activi", Value = "true" },
        new StatusItem { Text = "Inactivi", Value = "false" }
    };
    
    #endregion

    #region Lifecycle Methods
    
    protected override async Task OnInitializedAsync()
    {
        try
        {
            Logger.LogInformation("Utilizatori page initializing");
            errorMessage = null;
            
            // Load available persons for dropdown
            persoaneList = await UsersService.GetAvailablePersonsAsync();
            
            Logger.LogInformation("Loaded {Count} persons for dropdown", persoaneList.Count());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading persons: {Message}", ex.Message);
            errorMessage = "Eroare la încărcarea listei de persoane. Vă rugăm reîncărcați pagina.";
            persoaneList = new List<Persoana>();
        }
    }
    
    public void Dispose()
    {
        // Cleanup if needed
        Logger.LogDebug("Utilizatori page disposing");
    }
    
    #endregion

    #region Grid Event Handlers
    
    /// <summary>
    /// Handles grid action events before they execute (Save, Delete, etc.)
    /// </summary>
    public async Task ActionBeginHandler(ActionEventArgs<User> args)
    {
        try
        {
            ClearMessages();
            
            Logger.LogDebug("ActionBeginHandler: RequestType={RequestType}", args.RequestType);
            
            switch (args.RequestType)
            {
                case Syncfusion.Blazor.Grids.Action.Save:
                    // Validation before save
                    if (args.Data != null)
                    {
                        var user = args.Data;
                        if (user.PersoanaId == Guid.Empty)
                        {
                            errorMessage = "Selectați o persoană asociată.";
                            args.Cancel = true;
                            return;
                        }
                        
                        if (string.IsNullOrWhiteSpace(user.UserName))
                        {
                            errorMessage = "Numele de utilizator este obligatoriu.";
                            args.Cancel = true;
                            return;
                        }
                        
                        if (string.IsNullOrWhiteSpace(user.Email))
                        {
                            errorMessage = "Email-ul este obligatoriu.";
                            args.Cancel = true;
                            return;
                        }
                    }
                    break;
                    
                case Syncfusion.Blazor.Grids.Action.Delete:
                    // Delete will be handled by adaptor
                    Logger.LogInformation("Delete action initiated for user");
                    break;
                    
                case Syncfusion.Blazor.Grids.Action.Add:
                    // Reset password field for new user
                    newUserPassword = string.Empty;
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in ActionBeginHandler: {Message}", ex.Message);
            errorMessage = $"Eroare: {ex.Message}";
            args.Cancel = true;
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
            Logger.LogDebug("ActionCompleteHandler: RequestType={RequestType}", args.RequestType);
            
            switch (args.RequestType)
            {
                case Syncfusion.Blazor.Grids.Action.Save:
                    successMessage = args.Data?.Id == Guid.Empty 
                        ? "Utilizator creat cu succes!" 
                        : "Utilizator actualizat cu succes!";
                    break;
                    
                case Syncfusion.Blazor.Grids.Action.Delete:
                    successMessage = "Utilizator șters cu succes!";
                    break;
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
    
    /// <summary>
    /// Handles row selection.
    /// </summary>
    public void OnRowSelected(RowSelectEventArgs<User> args)
    {
        selectedUser = args.Data;
        hasSelection = true;
        Logger.LogDebug("Row selected: UserId={UserId}", selectedUser?.Id);
    }
    
    /// <summary>
    /// Handles row deselection.
    /// </summary>
    public void OnRowDeselected(RowDeselectEventArgs<User> args)
    {
        selectedUser = null;
        hasSelection = false;
        Logger.LogDebug("Row deselected");
    }
    
    /// <summary>
    /// Handles data bound event - updates total records count.
    /// </summary>
    public async Task OnDataBound(object args)
    {
        if (grid != null)
        {
            try
            {
                totalRecords = await grid.GetCurrentViewRecordsAsync() is { } records 
                    ? records.Count() 
                    : 0;
            }
            catch
            {
                // Ignore errors when getting count
            }
        }
    }
    
    /// <summary>
    /// Handles toolbar button clicks.
    /// </summary>
    public async Task OnToolbarClick(Syncfusion.Blazor.Navigations.ClickEventArgs args)
    {
        try
        {
            Logger.LogDebug("Toolbar clicked: Item={Item}", args.Item?.Id);
            
            switch (args.Item?.Id)
            {
                case "Refresh":
                case "UtilizatoriGrid_refresh":
                    await RefreshGrid();
                    break;
                    
                case "UtilizatoriGrid_excelexport":
                    await ExportToExcel();
                    break;
                    
                case "UtilizatoriGrid_pdfexport":
                    await ExportToPdf();
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in OnToolbarClick: {Message}", ex.Message);
            errorMessage = $"Eroare: {ex.Message}";
        }
    }
    
    #endregion

    #region Grid Operations
    
    /// <summary>
    /// Refreshes the grid data.
    /// </summary>
    private async Task RefreshGrid()
    {
        if (grid != null)
        {
            await grid.Refresh();
            successMessage = "Date reîmprospătate!";
            Logger.LogInformation("Grid refreshed");
        }
    }
    
    /// <summary>
    /// Exports grid data to Excel.
    /// </summary>
    private async Task ExportToExcel()
    {
        if (grid != null)
        {
            var excelExportProperties = new ExcelExportProperties
            {
                FileName = $"Utilizatori_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                IncludeHiddenColumn = false
            };
            await grid.ExportToExcelAsync(excelExportProperties);
            Logger.LogInformation("Exported to Excel");
        }
    }
    
    /// <summary>
    /// Exports grid data to PDF.
    /// </summary>
    private async Task ExportToPdf()
    {
        if (grid != null)
        {
            var pdfExportProperties = new PdfExportProperties
            {
                FileName = $"Utilizatori_{DateTime.Now:yyyyMMdd_HHmmss}.pdf",
                IncludeHiddenColumn = false
            };
            await grid.ExportToPdfAsync(pdfExportProperties);
            Logger.LogInformation("Exported to PDF");
        }
    }
    
    #endregion

    #region Filter Helpers
    
    /// <summary>
    /// Gets the current filter value for status column.
    /// </summary>
    private string GetFilterValue(object context)
    {
        // Return empty string for no filter
        return string.Empty;
    }
    
    /// <summary>
    /// Handles status filter change.
    /// </summary>
    private async Task OnStatusFilterChange(ChangeEventArgs<string, StatusItem> args, object context)
    {
        if (grid == null) return;
        
        try
        {
            if (string.IsNullOrEmpty(args.Value))
            {
                // Clear filter
                await grid.ClearFilteringAsync("IsActive");
            }
            else
            {
                // Apply filter
                var isActive = args.Value == "true";
                await grid.FilterByColumnAsync("IsActive", "equal", isActive);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error applying status filter: {Message}", ex.Message);
        }
    }
    
    #endregion

    #region Delete Confirmation
    
    /// <summary>
    /// Confirms and executes the delete operation.
    /// </summary>
    private async Task ConfirmDelete()
    {
        showDeleteConfirm = false;
        
        if (selectedUser != null && grid != null)
        {
            try
            {
                await UsersService.DeleteUserAsync(selectedUser.Id);
                await grid.Refresh();
                successMessage = $"Utilizatorul {selectedUserName} a fost șters!";
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error deleting user: {Message}", ex.Message);
                errorMessage = $"Eroare la ștergere: {ex.Message}";
            }
        }
    }
    
    #endregion

    #region Grid State Management
    
    /// <summary>
    /// Handles grid state saved event.
    /// </summary>
    private void OnGridStateSaved(string configName)
    {
        successMessage = $"Configurația '{configName}' a fost salvată!";
        Logger.LogInformation("Grid configuration saved: {ConfigName}", configName);
    }
    
    /// <summary>
    /// Handles grid state loaded event.
    /// </summary>
    private void OnGridStateLoaded(string configName)
    {
        successMessage = $"Configurația '{configName}' a fost încărcată!";
        Logger.LogInformation("Grid configuration loaded: {ConfigName}", configName);
    }
    
    /// <summary>
    /// Handles grid state reset event.
    /// </summary>
    private void OnGridStateReset()
    {
        successMessage = "Configurația grid-ului a fost resetată la valorile implicite!";
        Logger.LogInformation("Grid configuration reset");
    }
    
    #endregion

    #region Dialog Handlers
    
    /// <summary>
    /// Handles save button click in edit dialog.
    /// </summary>
    private async Task OnDialogSave()
    {
        if (grid != null)
        {
            await grid.EndEditAsync();
        }
    }
    
    /// <summary>
    /// Handles cancel button click in edit dialog.
    /// </summary>
    private async Task OnDialogCancel()
    {
        if (grid != null)
        {
            await grid.CloseEditAsync();
        }
    }
    
    #endregion

    #region Helper Methods
    
    /// <summary>
    /// Clears error message.
    /// </summary>
    private void ClearError()
    {
        errorMessage = null;
    }
    
    /// <summary>
    /// Clears success message.
    /// </summary>
    private void ClearSuccess()
    {
        successMessage = null;
    }
    
    /// <summary>
    /// Clears all messages.
    /// </summary>
    private void ClearMessages()
    {
        errorMessage = null;
        successMessage = null;
    }
    
    #endregion

    #region Helper Classes
    
    /// <summary>
    /// Model for status filter dropdown.
    /// </summary>
    public class StatusItem
    {
        public string Text { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
    
    #endregion
}
