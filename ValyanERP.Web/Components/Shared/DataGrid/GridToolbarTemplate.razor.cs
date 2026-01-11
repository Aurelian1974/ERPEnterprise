using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace ValyanERP.Web.Components.Shared.DataGrid;

/// <summary>
/// Code-behind for GridToolbarTemplate component.
/// Provides reusable toolbar functionality for SfGrid.
/// </summary>
public partial class GridToolbarTemplate : ComponentBase
{
    #region Parameters - Button Visibility

    /// <summary>
    /// Whether to show Add button.
    /// </summary>
    [Parameter]
    public bool ShowAddButton { get; set; } = true;

    /// <summary>
    /// Whether to show Edit button.
    /// </summary>
    [Parameter]
    public bool ShowEditButton { get; set; } = true;

    /// <summary>
    /// Whether to show Delete button.
    /// </summary>
    [Parameter]
    public bool ShowDeleteButton { get; set; } = true;

    /// <summary>
    /// Whether to show Refresh button.
    /// </summary>
    [Parameter]
    public bool ShowRefreshButton { get; set; } = true;

    /// <summary>
    /// Whether to show Search box.
    /// </summary>
    [Parameter]
    public bool ShowSearch { get; set; } = true;

    /// <summary>
    /// Whether to show Excel Export button.
    /// </summary>
    [Parameter]
    public bool ShowExcelExport { get; set; } = true;

    /// <summary>
    /// Whether to show PDF Export button.
    /// </summary>
    [Parameter]
    public bool ShowPdfExport { get; set; } = false;

    /// <summary>
    /// Whether to show Print button.
    /// </summary>
    [Parameter]
    public bool ShowPrint { get; set; } = false;

    /// <summary>
    /// Whether to show Column Chooser button.
    /// </summary>
    [Parameter]
    public bool ShowColumnChooser { get; set; } = true;

    /// <summary>
    /// Whether to show button labels or just icons.
    /// </summary>
    [Parameter]
    public bool ShowButtonLabels { get; set; } = true;

    #endregion

    #region Parameters - State

    /// <summary>
    /// Whether there is a row selected.
    /// </summary>
    [Parameter]
    public bool HasSelection { get; set; }

    #endregion

    #region Parameters - Custom Content

    /// <summary>
    /// Custom content for left side of toolbar.
    /// </summary>
    [Parameter]
    public RenderFragment? LeftContent { get; set; }

    /// <summary>
    /// Custom content for right side of toolbar.
    /// </summary>
    [Parameter]
    public RenderFragment? RightContent { get; set; }

    #endregion

    #region Parameters - Event Callbacks

    /// <summary>
    /// Callback when Add button is clicked.
    /// </summary>
    [Parameter]
    public EventCallback OnAddClicked { get; set; }

    /// <summary>
    /// Callback when Edit button is clicked.
    /// </summary>
    [Parameter]
    public EventCallback OnEditClicked { get; set; }

    /// <summary>
    /// Callback when Delete button is clicked.
    /// </summary>
    [Parameter]
    public EventCallback OnDeleteClicked { get; set; }

    /// <summary>
    /// Callback when Refresh button is clicked.
    /// </summary>
    [Parameter]
    public EventCallback OnRefreshClicked { get; set; }

    /// <summary>
    /// Callback when Search is triggered.
    /// </summary>
    [Parameter]
    public EventCallback<string> OnSearchClicked { get; set; }

    /// <summary>
    /// Callback when Excel Export button is clicked.
    /// </summary>
    [Parameter]
    public EventCallback OnExcelExportClicked { get; set; }

    /// <summary>
    /// Callback when PDF Export button is clicked.
    /// </summary>
    [Parameter]
    public EventCallback OnPdfExportClicked { get; set; }

    /// <summary>
    /// Callback when Print button is clicked.
    /// </summary>
    [Parameter]
    public EventCallback OnPrintClicked { get; set; }

    /// <summary>
    /// Callback when Column Chooser button is clicked.
    /// </summary>
    [Parameter]
    public EventCallback OnColumnChooserClicked { get; set; }

    #endregion

    #region Private Fields

    private string searchText = string.Empty;

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles Add button click.
    /// </summary>
    public async Task OnAdd() => await OnAddClicked.InvokeAsync();

    /// <summary>
    /// Handles Edit button click.
    /// </summary>
    public async Task OnEdit() => await OnEditClicked.InvokeAsync();

    /// <summary>
    /// Handles Delete button click.
    /// </summary>
    public async Task OnDelete() => await OnDeleteClicked.InvokeAsync();

    /// <summary>
    /// Handles Refresh button click.
    /// </summary>
    public async Task OnRefresh() => await OnRefreshClicked.InvokeAsync();

    /// <summary>
    /// Handles Search button click.
    /// </summary>
    public async Task OnSearch() => await OnSearchClicked.InvokeAsync(searchText);

    /// <summary>
    /// Handles Excel Export button click.
    /// </summary>
    public async Task OnExcelExport() => await OnExcelExportClicked.InvokeAsync();

    /// <summary>
    /// Handles PDF Export button click.
    /// </summary>
    public async Task OnPdfExport() => await OnPdfExportClicked.InvokeAsync();

    /// <summary>
    /// Handles Print button click.
    /// </summary>
    public async Task OnPrint() => await OnPrintClicked.InvokeAsync();

    /// <summary>
    /// Handles Column Chooser button click.
    /// </summary>
    public async Task OnColumnChooser() => await OnColumnChooserClicked.InvokeAsync();

    /// <summary>
    /// Handles Enter key press in search box.
    /// </summary>
    public async Task OnSearchKeyUp(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await OnSearch();
        }
    }

    #endregion
}
