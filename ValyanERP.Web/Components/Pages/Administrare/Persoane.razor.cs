using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Syncfusion.Blazor.Grids;
using ValyanERP.Web.Features.Administrare.Persoane.Models;
using ValyanERP.Web.Features.Administrare.Persoane;
using ValyanERP.Web.Components.Shared.DataGrid;
using ValyanERP.Web.Features.Administrare.Persoane.Services;
using ValyanERP.Web.Features.Administrare.Persoane.Repositories;
using ValyanERP.Web.Features.Administrare.Persoane.Validators;
using ValyanERP.Web.Features.Infrastructure.SystemParameters.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Syncfusion.Blazor.Navigations;

namespace ValyanERP.Web.Components.Pages.Administrare;

/// <summary>
/// Code-behind for Persoane page.
/// Contains ALL business logic, state management, and event handlers.
/// </summary>
public partial class Persoane : ComponentBase, IDisposable
{
    [Inject] private ILogger<Persoane> Logger { get; set; } = default!;
    [Inject] private IPersoaneService PersoaneService { get; set; } = default!;
    [Inject] private ISystemParametersService SystemParametersService { get; set; } = default!;
    [Inject] private ISystemParametersNotifier SystemParametersNotifier { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;
    
    private SfGrid<Persoana>? grid;
    private GridStateManager? gridStateManager;
    private string? errorMessage;
    private string? successMessage;
    private int totalRecords = 0;
    private bool isLoading = false;
    private bool showViewDialog = false;
    private bool showDeleteConfirm = false;
    private Persoana? viewPersoana;
    private string? selectedPersoanaName;
    private Guid? selectedPersoanaId;
    
    private List<object> toolbar = new()
    {
        "Add", "Edit", 
        new ItemModel { Text = "Vizualizare", PrefixIcon = "e-icons e-eye", Id = "View" },
        "Delete",
        new ItemModel { Text = "Reîmprospătează", PrefixIcon = "e-icons e-refresh", Id = "Refresh" },
        "Search",
        new ItemModel { Type = ItemType.Separator },
        "ExcelExport", "PdfExport",
        new ItemModel { Type = ItemType.Separator },
        "ColumnChooser"
    };
    
    private int pageSize = 20;
    private List<int> pageSizeOptions = new() { 20, 50, 100 };
    
    private List<StatusItem> statusOptions = new()
    {
        new StatusItem { Text = "Toate", Value = "" },
        new StatusItem { Text = "Active", Value = "true" },
        new StatusItem { Text = "Inactive", Value = "false" }
    };

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
                // Validation is handled by:
                // 1. FluentValidation (PersoanaValidator)
                // 2. EditForm in Grid Template
                // 3. Server-side validation in Repository/Service
                
                // The grid will automatically call the adaptor's Insert/Update methods
                // which will use the repository with stored procedures
            }

            if (args.RequestType == Syncfusion.Blazor.Grids.Action.Delete)
            {
                // Prevent automatic delete and show confirmation dialog
                args.Cancel = true;
                ShowDeleteConfirm();
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
    
    /// <summary>
    /// Lifecycle method - initialize component state.
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        isLoading = true;
        try
        {
            // Load system parameters for configuration
            pageSize = await SystemParametersService.GetIntAsync("Business.Pagination.DefaultPageSize", 20);
            
            // Subscribe to parameter changes so we can apply them live (both scoped and application-wide notifier)
            SystemParametersService.ParameterChanged += OnSystemParameterChanged;
            SystemParametersNotifier.ParameterChanged += OnSystemParameterChanged;
            
            // Generate page size options: 5, 10, 15, ..., 200
            pageSizeOptions = Enumerable.Range(1, 40).Select(i => i * 5).ToList();
            
            // Load initial data if needed
            await LoadTotalRecordsAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error initializing Persoane page");
            errorMessage = "Eroare la inițializarea paginii.";
        }
        finally
        {
            isLoading = false;
        }
    }
    
    /// <summary>
    /// Performance optimization - prevent unnecessary renders during loading.
    /// </summary>
    protected override bool ShouldRender()
    {
        if (isLoading) return false;
        return base.ShouldRender();
    }
    
    /// <summary>
    /// Cleanup resources.
    /// </summary>
    public void Dispose()
    {
        // Cleanup any subscriptions or resources
        try
        {
            SystemParametersService.ParameterChanged -= OnSystemParameterChanged;
            SystemParametersNotifier.ParameterChanged -= OnSystemParameterChanged;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error while unsubscribing from ParameterChanged");
        }
    }
    
    /// <summary>
    /// Loads total record count for display.
    /// </summary>
    private async Task LoadTotalRecordsAsync()
    {
        try
        {
            // Fetch authoritative count from DB via service so header/pager match DB state
            totalRecords = await PersoaneService.GetTotalCountAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading total records");
            // Fallback to adaptor count if DB call fails
            totalRecords = PersoaneAdaptor.LastTotalCount;
        }
    }
    
    /// <summary>
    /// Handles toolbar click events.
    /// </summary>
    private async Task OnToolbarClick(Syncfusion.Blazor.Navigations.ClickEventArgs args)
    {
        try
        {
            switch (args.Item.Id)
            {
                case "View":
                    await ShowViewDialog();
                    break;
                case "Refresh":
                    await RefreshGrid();
                    break;
                case "ExcelExport":
                    await ExportToExcel();
                    break;
                case "PdfExport":
                    await ExportToPdf();
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error handling toolbar click: {Id}", args.Item.Id);
            errorMessage = "Eroare la executarea acțiunii.";
        }
    }
    
    /// <summary>
    /// Shows the view dialog for selected person.
    /// </summary>
    private async Task ShowViewDialog()
    {
        if (grid?.SelectedRecords?.FirstOrDefault() is Persoana selectedPersoana)
        {
            viewPersoana = selectedPersoana;
            showViewDialog = true;
        }
        else
        {
            errorMessage = "Vă rugăm selectați o persoană pentru vizualizare.";
        }
    }
    
    /// <summary>
    /// Closes the view dialog.
    /// </summary>
    private void CloseViewDialog()
    {
        showViewDialog = false;
        viewPersoana = null;
    }
    
    /// <summary>
    /// Refreshes the grid data.
    /// </summary>
    private async Task RefreshGrid()
    {
        if (grid != null)
        {
            await grid.Refresh();
            successMessage = "Date reîmprospătate cu succes.";
            await LoadTotalRecordsAsync();
        }
    }
    
    /// <summary>
    /// Exports grid data to Excel.
    /// </summary>
    private async Task ExportToExcel()
    {
        if (grid != null)
        {
            try
            {
                // TODO: Implement Excel export
                // await grid.ExcelExport();
                successMessage = "Funcționalitatea de export Excel va fi implementată în curând.";
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error exporting to Excel");
                errorMessage = "Eroare la exportul Excel.";
            }
        }
    }
    
    /// <summary>
    /// Exports grid data to PDF.
    /// </summary>
    private async Task ExportToPdf()
    {
        if (grid != null)
        {
            try
            {
                // TODO: Implement PDF export
                // await grid.PdfExport();
                successMessage = "Funcționalitatea de export PDF va fi implementată în curând.";
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error exporting to PDF");
                errorMessage = "Eroare la exportul PDF.";
            }
        }
    }
    
    /// <summary>
    /// Handles row selection changes.
    /// </summary>
    private void OnRowSelected(RowSelectEventArgs<Persoana> args)
    {
        // Update selected person info for dialogs
        if (args.Data != null)
        {
            selectedPersoanaId = args.Data.Id;
            selectedPersoanaName = $"{args.Data.Nume} {args.Data.Prenume}";
        }
    }
    
    /// <summary>
    /// Handles row deselection.
    /// </summary>
    private void OnRowDeselected(RowDeselectEventArgs<Persoana> args)
    {
        if (grid?.SelectedRecords?.Count == 0)
        {
            selectedPersoanaId = null;
            selectedPersoanaName = null;
        }
    }
    
    /// <summary>
    /// Handles data binding completion.
    /// </summary>
    private async Task OnDataBound()
    {
        await LoadTotalRecordsAsync();
    }

    /// <summary>
    /// Handles parameter change notifications (from SystemParametersService).
    /// Applies relevant parameter changes live (no page reload required).
    /// </summary>
    private async void OnSystemParameterChanged(object? sender, SystemParameterChangedEventArgs e)
    {
        try
        {
            // If Key is null => generic clear; otherwise check relevant key
            if (e == null) return;

            if (e.ParameterKey == null || e.ParameterKey == "Business.Pagination.DefaultPageSize")
            {
                var newPageSize = await SystemParametersService.GetIntAsync("Business.Pagination.DefaultPageSize", 20);
                if (newPageSize != pageSize)
                {
                    pageSize = newPageSize;

                    // Refresh the grid so it applies the new page size and re-reads data
                    if (grid != null)
                    {
                        try
                        {
                            await grid.Refresh();
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning(ex, "Grid refresh failed after pageSize change");
                        }
                    }

                    // Re-render to update UI controls bound to pageSize (page size dropdown, etc.)
                    await InvokeAsync(StateHasChanged);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error handling system parameter change event");
        }
    }
    
    /// <summary>
    /// Handles row data binding for custom styling.
    /// </summary>
    private void OnRowDataBound(RowDataBoundEventArgs<Persoana> args)
    {
        if (args.Data?.IsActive == false)
        {
            args.Row.AddClass(new string[] { "inactive-row" });
        }
    }
    
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
    /// Handles grid state saved event.
    /// </summary>
    private void OnGridStateSaved()
    {
        successMessage = "Configurația grilei a fost salvată.";
    }
    
    /// <summary>
    /// Handles grid state loaded event.
    /// </summary>
    private void OnGridStateLoaded()
    {
        successMessage = "Configurația grilei a fost încărcată.";
    }
    
    /// <summary>
    /// Handles grid state reset event.
    /// </summary>
    private void OnGridStateReset()
    {
        successMessage = "Configurația grilei a fost resetată.";
    }
    
    /// <summary>
    /// Gets filter value for status dropdown.
    /// </summary>
    private string GetFilterValue(object context)
    {
        // Implementation for custom filter template
        return "";
    }
    
    /// <summary>
    /// Handles status filter change.
    /// </summary>
    private void OnStatusFilterChange(Syncfusion.Blazor.DropDowns.ChangeEventArgs<string, StatusItem> args, object context)
    {
        // Implementation for custom filter
    }
    
    /// <summary>
    /// Confirms delete operation.
    /// </summary>
    private async Task ConfirmDelete()
    {
        if (selectedPersoanaId.HasValue)
        {
            try
            {
                await PersoaneService.DeleteAsync(selectedPersoanaId.Value);
                successMessage = "Persoana a fost ștearsă cu succes.";
                showDeleteConfirm = false;
                await RefreshGrid();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error deleting person {Id}", selectedPersoanaId);
                errorMessage = "Eroare la ștergerea persoanei.";
            }
        }
    }
    
    /// <summary>
    /// Shows delete confirmation dialog.
    /// </summary>
    private void ShowDeleteConfirm()
    {
        if (selectedPersoanaId.HasValue)
        {
            showDeleteConfirm = true;
        }
        else
        {
            errorMessage = "Vă rugăm selectați o persoană pentru ștergere.";
        }
    }
    
    /// <summary>
    /// Gets the header text for the edit dialog based on operation.
    /// </summary>
    private string GetEditDialogHeaderText(object context)
    {
        // This would be determined by the grid's edit mode
        // For now, return a generic header
        return "Editare Persoană";
    }
}

/// <summary>
/// Status item for filter dropdowns.
/// </summary>
public class StatusItem
{
    public string Text { get; set; } = "";
    public string Value { get; set; } = "";
}
