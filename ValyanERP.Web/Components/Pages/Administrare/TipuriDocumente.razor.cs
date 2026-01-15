using Microsoft.AspNetCore.Components;
using Syncfusion.Blazor.Grids;
using Syncfusion.Blazor.Navigations;
using ValyanERP.Web.Components.Shared.DataGrid;
using ValyanERP.Web.Features.Administrare.TipuriDocumente.Models;
using ValyanERP.Web.Features.Administrare.TipuriDocumente.Services;

namespace ValyanERP.Web.Components.Pages.Administrare;

/// <summary>
/// Code-behind for TipuriDocumente page.
/// Implements CRUD operations for document types management.
/// </summary>
public partial class TipuriDocumente : ComponentBase
{
    #region Injected Services

    [Inject]
    public ITipuriDocumenteService TipuriDocumenteService { get; set; } = default!;

    #endregion

    #region Grid References

    /// <summary>
    /// Reference to the main SfGrid component.
    /// </summary>
    private SfGrid<TipDocument>? grid;

    /// <summary>
    /// Reference to the GridStateManager component.
    /// </summary>
    private GridStateManager? gridStateManager;

    #endregion

    #region Data Properties

    /// <summary>
    /// Collection of document types displayed in the grid.
    /// </summary>
    private IEnumerable<TipDocument> tipuriDocumente = new List<TipDocument>();

    /// <summary>
    /// Loading state indicator.
    /// </summary>
    private bool isLoading = true;

    /// <summary>
    /// Error message to display to the user.
    /// </summary>
    private string? errorMessage;

    /// <summary>
    /// Total number of records for display.
    /// </summary>
    private int totalRecords;

    /// <summary>
    /// Available page size options for the grid.
    /// </summary>
    private readonly List<object> pageSizeOptions = new() { 10, 20, 50, 100 };

    #endregion

    #region Lifecycle Methods

    /// <summary>
    /// Initializes the component and loads data.
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        await LoadDataAsync();
    }

    #endregion

    #region Data Operations

    /// <summary>
    /// Loads document types data from the service.
    /// </summary>
    private async Task LoadDataAsync()
    {
        try
        {
            isLoading = true;
            errorMessage = null;

            tipuriDocumente = await TipuriDocumenteService.GetAllAsync();
            totalRecords = tipuriDocumente.Count();
        }
        catch (Exception ex)
        {
            errorMessage = $"Eroare la încărcarea datelor: {ex.Message}";
        }
        finally
        {
            isLoading = false;
            StateHasChanged();
        }
    }

    #endregion

    #region Grid Event Handlers

    /// <summary>
    /// Handles toolbar click events for grid operations.
    /// </summary>
    private async Task OnToolbarClick(Syncfusion.Blazor.Navigations.ClickEventArgs args)
    {
        if (args.Item.Id == "Add")
        {
            await grid!.AddRecordAsync();
        }
        else if (args.Item.Id == "Edit")
        {
            await grid!.StartEditAsync();
        }
        else if (args.Item.Id == "Delete")
        {
            await grid!.DeleteRecordAsync();
        }
        else if (args.Item.Id == "ExcelExport")
        {
            await ExportToExcelAsync();
        }
        else if (args.Item.Id == "PdfExport")
        {
            await ExportToPdfAsync();
        }
    }

    /// <summary>
    /// Handles row data bound events for styling inactive rows.
    /// </summary>
    private void RowDataBound(RowDataBoundEventArgs<TipDocument> args)
    {
        if (args.Data != null && !args.Data.IsActive)
        {
            args.Row.AddClass(new[] { "inactive-row" });
        }
    }

    #endregion

    #region Export Methods

    /// <summary>
    /// Exports grid data to Excel format.
    /// </summary>
    private async Task ExportToExcelAsync()
    {
        try
        {
            var excelExport = new ExcelExportProperties
            {
                FileName = $"TipuriDocumente_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                IncludeTemplateColumn = false
            };

            await grid!.ExportToExcelAsync(excelExport);
        }
        catch (Exception ex)
        {
            errorMessage = $"Eroare la export Excel: {ex.Message}";
        }
    }

    /// <summary>
    /// Exports grid data to PDF format.
    /// </summary>
    private async Task ExportToPdfAsync()
    {
        try
        {
            var pdfExport = new PdfExportProperties
            {
                FileName = $"TipuriDocumente_{DateTime.Now:yyyyMMdd_HHmmss}.pdf",
                IncludeTemplateColumn = false
            };

            await grid!.ExportToPdfAsync(pdfExport);
        }
        catch (Exception ex)
        {
            errorMessage = $"Eroare la export PDF: {ex.Message}";
        }
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// Clears the current error message.
    /// </summary>
    private void ClearError()
    {
        errorMessage = null;
    }

    /// <summary>
    /// Handles code input changes for validation.
    /// </summary>
    private void OnCodeChanged(string value)
    {
        // Validation logic can be added here
    }

    /// <summary>
    /// Handles name input changes for validation.
    /// </summary>
    private void OnNameChanged(string value)
    {
        // Validation logic can be added here
    }

    #endregion

    #region Grid State Management

    /// <summary>
    /// Handles grid state saved event.
    /// </summary>
    private async Task OnGridStateSaved()
    {
        await LoadDataAsync();
    }

    /// <summary>
    /// Handles grid state loaded event.
    /// </summary>
    private async Task OnGridStateLoaded()
    {
        await LoadDataAsync();
    }

    /// <summary>
    /// Handles grid state reset event.
    /// </summary>
    private async Task OnGridStateReset()
    {
        await LoadDataAsync();
    }

    #endregion
}