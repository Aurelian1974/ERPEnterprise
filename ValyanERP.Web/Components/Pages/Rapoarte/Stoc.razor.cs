
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Syncfusion.Blazor.Grids;
using Syncfusion.Blazor.Data;
using Syncfusion.Blazor.Navigations;
using ValyanERP.Web.Features.Rapoarte.Stoc.Models;
using ValyanERP.Web.Features.Rapoarte.Stoc;
using ValyanERP.Web.Components.Shared.DataGrid;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace ValyanERP.Web.Components.Pages.Rapoarte;

public partial class Stoc : ComponentBase
{
    #region Injected Services

    [Inject]
    private ILogger<Stoc> Logger { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [Inject]
    private StocAdaptor stocAdaptor { get; set; } = default!;

    #endregion

    #region Grid References
    private SfGrid<StocReportItemDto>? grid;
    private GridStateManager? gridStateManager;
    #endregion

    #region State Variables
    private string? errorMessage;
    private int totalRecords = 0;
    private bool isLoading = false;
    #endregion

    #region Grid Configuration
    private List<object> ToolbarItems = new()
    {
        "ExcelExport",
        "PdfExport",
        new { Text = "Reîmprospătează", TooltipText = "Reîmprospătează datele", PrefixIcon = "e-icons e-refresh", Id = "Refresh" }
    };

    // Use real Syncfusion types for PageSettings and DataManager to avoid runtime binder errors
    // DataManager and PageSettings are configured in markup using SfDataManager/GridPageSettings
    #endregion

    #region Lifecycle
    protected override async Task OnInitializedAsync()
    {
        isLoading = true;
        errorMessage = null;
        await Task.Delay(10); // Simulează loading scurt
        isLoading = false;
    }
    #endregion

    #region Grid Event Handlers
    public async Task OnToolbarClick(ClickEventArgs args)
    {
        try
        {
            ClearError();
            switch (args.Item.Id)
            {
                case "ExcelExport":
                    await ExportToExcel();
                    break;
                case "PdfExport":
                    await ExportToPdf();
                    break;
                case "Refresh":
                    await RefreshGrid();
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in OnToolbarClick: {Id}", args.Item.Id);
            errorMessage = $"Eroare: {ex.Message}";
        }
    }

    // Manual export methods (to be implemented)
    private async Task ExportToExcel()
    {
        // TODO: Implement manual export using Syncfusion.XlsIO, similar to Utilizatori
        await Task.CompletedTask;
    }
    private async Task ExportToPdf()
    {
        // TODO: Implement manual export using Syncfusion.Pdf, similar to Utilizatori
        await Task.CompletedTask;
    }

    public async Task OnDataBound(object args)
    {
        if (grid != null)
        {
            try
            {
                var records = await grid.GetCurrentViewRecordsAsync();
                totalRecords = records?.Count() ?? 0;
            }
            catch
            {
                // Ignore errors
            }
        }
    }

    private void OnGridStateSaved(string configName)
    {
        // Optionally show a message
        Logger.LogInformation("Grid configuration saved: {ConfigName}", configName);
    }
    private void OnGridStateLoaded(string configName)
    {
        Logger.LogInformation("Grid configuration loaded: {ConfigName}", configName);
    }
    private void OnGridStateReset()
    {
        Logger.LogInformation("Grid configuration reset");
    }
    #endregion

    #region Grid Operations
    private async Task RefreshGrid()
    {
        if (grid != null)
        {
            await grid.Refresh();
        }
    }
    #endregion

    #region Helper Methods
    private void ClearError()
    {
        errorMessage = null;
    }
    #endregion
}
