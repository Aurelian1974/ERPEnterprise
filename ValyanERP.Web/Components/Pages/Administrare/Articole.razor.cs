using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using ValyanERP.Web.Components.Shared.DataGrid;
using ValyanERP.Web.Features.Administrare.Articole.Models;
using ValyanERP.Web.Features.Administrare.Articole.Services;
using ValyanERP.Web.Features.Administrare.TipuriArticole.Models;
using ValyanERP.Web.Features.Administrare.TipuriArticole.Services;
using Syncfusion.Blazor.Grids;
using Syncfusion.Blazor.Navigations;

namespace ValyanERP.Web.Components.Pages.Administrare;

public partial class Articole : ComponentBase
{
    #region Injected Services

    [Inject]
    public IArticoleService ArticoleService { get; set; } = default!;

    [Inject]
    public IItemTypesService ItemTypesService { get; set; } = default!;

    [Inject]
    private ILogger<Articole> Logger { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    #endregion

    #region Grid References

    /// <summary>
    /// Reference to the main SfGrid component.
    /// </summary>
    private SfGrid<ValyanERP.Web.Features.Administrare.Articole.Models.Articol>? grid;

    /// <summary>
    /// Reference to the grid state manager.
    /// </summary>
    private GridStateManager? gridStateManager;

    #endregion

    #region Grid Configuration

    /// <summary>
    /// Current page size for pagination.
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
        new ItemModel { Text = "Adăugare", TooltipText = "Adăugare articol nou", PrefixIcon = "e-icons e-add", Id = "AddCustom" },
        new ItemModel { Text = "Editare", TooltipText = "Editare articol selectat", PrefixIcon = "e-icons e-edit", Id = "EditCustom" },
        new ItemModel { Text = "Vizualizare", TooltipText = "Vizualizează detaliile articolului selectat", PrefixIcon = "e-icons e-eye", Id = "View" },
        "Delete",
        new ItemModel { Text = "Reîmprospătează", TooltipText = "Reîmprospătează datele", PrefixIcon = "e-icons e-refresh", Id = "Refresh" },
        "Search",
        new Syncfusion.Blazor.Navigations.ItemModel { Type = Syncfusion.Blazor.Navigations.ItemType.Separator },
        "ExcelExport",
        "PdfExport",
        new Syncfusion.Blazor.Navigations.ItemModel { Type = Syncfusion.Blazor.Navigations.ItemType.Separator },
        "ColumnChooser"
    };

    #endregion

    #region Data Properties

    /// <summary>
    /// Total number of records for display.
    /// </summary>
    private int totalRecords;

    /// <summary>
    /// Currently selected articol in the grid.
    /// </summary>
    private ValyanERP.Web.Features.Administrare.Articole.Models.Articol? selectedArticol;

    /// <summary>
    /// List of available tipuri articole for dropdowns.
    /// </summary>
    private IEnumerable<ValyanERP.Web.Features.Administrare.TipuriArticole.Models.ItemType> tipuriArticole = new List<ValyanERP.Web.Features.Administrare.TipuriArticole.Models.ItemType>();

    #endregion

    #region Dialog State

    /// <summary>
    /// Whether to show view dialog.
    /// </summary>
    private bool showViewDialog = false;

    /// <summary>
    /// Whether to show add dialog.
    /// </summary>
    private bool showAddDialog = false;

    /// <summary>
    /// New articol being created in add dialog.
    /// </summary>
    private ValyanERP.Web.Features.Administrare.Articole.Models.Articol newArticol = new();

    /// <summary>
    /// Whether to show edit dialog.
    /// </summary>
    private bool showEditDialog = false;

    /// <summary>
    /// Articol being edited in edit dialog.
    /// </summary>
    private ValyanERP.Web.Features.Administrare.Articole.Models.Articol? editingArticol;

    /// <summary>
    /// Articol being viewed in view dialog (read-only).
    /// </summary>
    private ValyanERP.Web.Features.Administrare.Articole.Models.Articol? viewedArticol;

    #endregion

    #region UI State

    /// <summary>
    /// Whether the page is currently loading data.
    /// </summary>
    private bool isLoading = false;

    /// <summary>
    /// Error message to display to the user.
    /// </summary>
    private string? errorMessage;

    /// <summary>
    /// Success message to display to the user.
    /// </summary>
    private string? successMessage;

    #endregion

    #region Lifecycle Methods

    /// <summary>
    /// Initializes the component.
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        Logger.LogInformation("Articole page initializing");

        try
        {
            await LoadTipuriArticoleAsync();
            await LoadTotalRecordsAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error initializing Articole page");
            errorMessage = "Eroare la inițializarea paginii.";
        }
    }

    #endregion

    #region Data Loading Methods

    /// <summary>
    /// Loads the list of available tipuri articole for dropdowns.
    /// </summary>
    private async Task LoadTipuriArticoleAsync()
    {
        try
        {
            tipuriArticole = await ItemTypesService.GetAllItemTypesAsync();
            Logger.LogDebug("Loaded {Count} tipuri articole for dropdowns", tipuriArticole.Count());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading tipuri articole");
            errorMessage = "Eroare la încărcarea tipurilor de articole.";
        }
    }

    /// <summary>
    /// Loads the total number of articole records.
    /// </summary>
    private async Task LoadTotalRecordsAsync()
    {
        try
        {
            totalRecords = await ArticoleService.GetTotalArticoleCountAsync();
            Logger.LogDebug("Loaded total records count: {Count}", totalRecords);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading total records count");
            totalRecords = 0; // Fallback to 0
        }
    }

    /// <summary>
    /// Refreshes the grid data.
    /// </summary>
    private async Task RefreshGridAsync()
    {
        try
        {
            if (grid != null)
            {
                await grid.Refresh();
                Logger.LogDebug("Grid refreshed");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error refreshing grid");
            errorMessage = "Eroare la reîmprospătarea datelor.";
        }
    }

    #endregion

    #region Grid Event Handlers

    /// <summary>
    /// Handles grid data binding completion.
    /// </summary>
    private void OnDataBound()
    {
        Logger.LogDebug("Data bound event triggered");
    }

    /// <summary>
    /// Handles row selection in the grid.
    /// </summary>
    private void OnRowSelected(RowSelectEventArgs<ValyanERP.Web.Features.Administrare.Articole.Models.Articol> args)
    {
        selectedArticol = args.Data;
        Logger.LogDebug("Row selected: Articol {Code}", selectedArticol?.ArticolCode);
    }

    /// <summary>
    /// Handles row deselection in the grid.
    /// </summary>
    private void OnRowDeselected(RowDeselectEventArgs<ValyanERP.Web.Features.Administrare.Articole.Models.Articol> args)
    {
        selectedArticol = null;
        Logger.LogDebug("Row deselected");
    }

    /// <summary>
    /// Handles toolbar button clicks.
    /// Note: Built-in Syncfusion toolbar items (Delete) are handled automatically by the grid.
    /// This method only handles custom toolbar items.
    /// </summary>
    public async Task OnToolbarClick(ClickEventArgs args)
    {
        try
        {
            Logger.LogDebug("Toolbar click: {Id}", args.Item.Id);

            // Handle custom toolbar items only
            switch (args.Item.Id)
            {
                case "AddCustom":
                    ShowAddDialog();
                    break;

                case "EditCustom":
                    await ShowEditDialogAsync();
                    break;

                case "View":
                    await ShowViewDialogAsync();
                    break;

                case "Refresh":
                    await RefreshGridAsync();
                    successMessage = "Datele au fost reîmprospătate.";
                    break;

                case "ExcelExport":
                    await ExportToExcelAsync();
                    break;

                case "PdfExport":
                    await ExportToPdfAsync();
                    break;

                // Built-in Syncfusion toolbar items - let the grid handle them automatically
                case "ArticoleGrid_delete":
                    // These are built-in items, don't handle them here - return immediately
                    return;

                default:
                    Logger.LogDebug("Unhandled toolbar click: {Id}", args.Item.Id);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in OnToolbarClick: {Id}", args.Item.Id);
            errorMessage = "Eroare la executarea acțiunii.";
        }
    }

    #endregion

    #region Dialog Methods

    /// <summary>
    /// Shows the view dialog for the selected articol.
    /// </summary>
    private async Task ShowViewDialogAsync()
    {
        if (selectedArticol == null)
        {
            errorMessage = "Selectați un articol pentru vizualizare.";
            return;
        }

        viewedArticol = selectedArticol;
        showViewDialog = true;
        Logger.LogDebug("View dialog shown for Articol {Code}", selectedArticol.ArticolCode);
    }

    /// <summary>
    /// Closes the view dialog.
    /// </summary>
    private void CloseViewDialog()
    {
        showViewDialog = false;
        viewedArticol = null;
        Logger.LogDebug("View dialog closed");
    }

    /// <summary>
    /// Shows the add dialog for creating a new articol.
    /// </summary>
    private void ShowAddDialog()
    {
        newArticol = new ValyanERP.Web.Features.Administrare.Articole.Models.Articol
        {
            IsActive = true, // Default to active
            IsStockable = true // Default to stockable
        };
        showAddDialog = true;
        Logger.LogDebug("Add dialog shown");
    }

    /// <summary>
    /// Closes the add dialog.
    /// </summary>
    private void CloseAddDialog()
    {
        showAddDialog = false;
        newArticol = new ValyanERP.Web.Features.Administrare.Articole.Models.Articol();
        Logger.LogDebug("Add dialog closed");
    }

    /// <summary>
    /// Saves the new articol from the add dialog.
    /// </summary>
    private async Task SaveNewArticol()
    {
        try
        {
            // Validate required fields
            if (string.IsNullOrWhiteSpace(newArticol.ArticolCode))
            {
                errorMessage = "Codul articolului este obligatoriu.";
                return;
            }

            if (string.IsNullOrWhiteSpace(newArticol.ArticolName))
            {
                errorMessage = "Numele articolului este obligatoriu.";
                return;
            }

            if (newArticol.TipArticolId == Guid.Empty)
            {
                errorMessage = "Tipul de articol este obligatoriu.";
                return;
            }

            await ArticoleService.CreateArticolAsync(new ValyanERP.Web.Features.Administrare.Articole.Models.ArticolCreateDto
            {
                ArticolCode = newArticol.ArticolCode,
                ArticolName = newArticol.ArticolName,
                Description = newArticol.Description,
                TipArticolId = newArticol.TipArticolId,
                IsStockable = newArticol.IsStockable
            });

            successMessage = $"Articolul '{newArticol.ArticolCode}' a fost adăugat cu succes.";
            await RefreshGridAsync();
            CloseAddDialog();
            Logger.LogInformation("New Articol created: {Code}", newArticol.ArticolCode);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error creating new Articol");
            errorMessage = "Eroare la adăugarea articolului.";
        }
    }

    /// <summary>
    /// Shows the edit dialog for the selected articol.
    /// </summary>
    private async Task ShowEditDialogAsync()
    {
        if (selectedArticol == null)
        {
            errorMessage = "Selectați un articol pentru editare.";
            return;
        }

        // Create a copy of the selected articol for editing
        editingArticol = new ValyanERP.Web.Features.Administrare.Articole.Models.Articol
        {
            Id = selectedArticol.Id,
            ArticolCode = selectedArticol.ArticolCode,
            ArticolName = selectedArticol.ArticolName,
            Description = selectedArticol.Description,
            TipArticolId = selectedArticol.TipArticolId,
            IsActive = selectedArticol.IsActive,
            CreatedAt = selectedArticol.CreatedAt,
            UpdatedAt = selectedArticol.UpdatedAt,
            OwnerCompanyId = selectedArticol.OwnerCompanyId,
            OwnerWorkPlaceId = selectedArticol.OwnerWorkPlaceId,
            OwnerLocationId = selectedArticol.OwnerLocationId,
            TipArticolCode = selectedArticol.TipArticolCode,
            TipArticolName = selectedArticol.TipArticolName,
            OwnerCompanyName = selectedArticol.OwnerCompanyName
        };

        showEditDialog = true;
        Logger.LogDebug("Edit dialog shown for Articol {Code}", editingArticol.ArticolCode);
    }

    /// <summary>
    /// Closes the edit dialog.
    /// </summary>
    private void CloseEditDialog()
    {
        showEditDialog = false;
        editingArticol = null;
        Logger.LogDebug("Edit dialog closed");
    }

    /// <summary>
    /// Saves the edited articol from the edit dialog.
    /// </summary>
    private async Task SaveEditedArticol()
    {
        try
        {
            if (editingArticol == null)
            {
                errorMessage = "Nu există date pentru salvare.";
                return;
            }

            // Validate required fields
            if (string.IsNullOrWhiteSpace(editingArticol.ArticolCode))
            {
                errorMessage = "Codul articolului este obligatoriu.";
                return;
            }

            if (string.IsNullOrWhiteSpace(editingArticol.ArticolName))
            {
                errorMessage = "Numele articolului este obligatoriu.";
                return;
            }

            if (editingArticol.TipArticolId == Guid.Empty)
            {
                errorMessage = "Tipul de articol este obligatoriu.";
                return;
            }

            var updatedCode = editingArticol.ArticolCode;
            await ArticoleService.UpdateArticolAsync(new ValyanERP.Web.Features.Administrare.Articole.Models.ArticolUpdateDto
            {
                Id = editingArticol.Id,
                ArticolCode = editingArticol.ArticolCode,
                ArticolName = editingArticol.ArticolName,
                Description = editingArticol.Description,
                TipArticolId = editingArticol.TipArticolId,
                IsStockable = editingArticol.IsStockable
            });

            successMessage = $"Articolul '{updatedCode}' a fost actualizat cu succes.";
            await RefreshGridAsync();
            CloseEditDialog();
            Logger.LogInformation("Articol updated: {Code}", updatedCode);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating Articol");
            errorMessage = "Eroare la actualizarea articolului.";
        }
    }

    #endregion

    #region Dropdown Event Handlers

    /// <summary>
    /// Handles TipArticol selection change in add dialog.
    /// </summary>
    private void OnTipArticolChanged(Syncfusion.Blazor.DropDowns.ChangeEventArgs<Guid, ValyanERP.Web.Features.Administrare.TipuriArticole.Models.ItemType> args)
    {
        newArticol.TipArticolId = args.Value;
        Logger.LogDebug("TipArticol changed in add dialog: {Id}", args.Value);
    }

    /// <summary>
    /// Handles TipArticol selection change in edit dialog.
    /// </summary>
    private void OnEditTipArticolChanged(Syncfusion.Blazor.DropDowns.ChangeEventArgs<Guid, ValyanERP.Web.Features.Administrare.TipuriArticole.Models.ItemType> args)
    {
        if (editingArticol != null)
        {
            editingArticol.TipArticolId = args.Value;
            Logger.LogDebug("TipArticol changed in edit dialog: {Id}", args.Value);
        }
    }

    #endregion

    #region Export Methods

    /// <summary>
    /// Exports the grid data to Excel.
    /// </summary>
    private async Task ExportToExcelAsync()
    {
        try
        {
            if (grid == null) return;

            Logger.LogInformation("Starting Excel export for Articole");

            // Get all data for export
            var allData = await ArticoleService.GetAllArticoleAsync();

            // Use Syncfusion Excel export
            var excelEngine = new Syncfusion.XlsIO.ExcelEngine();
            var workbook = excelEngine.Excel.Workbooks.Create();
            var worksheet = workbook.Worksheets[0];
            worksheet.Name = "Articole";

            // Add headers
            worksheet.Range["A1"].Text = "Cod Articol";
            worksheet.Range["B1"].Text = "Denumire Articol";
            worksheet.Range["C1"].Text = "Descriere";
            worksheet.Range["D1"].Text = "Tip Articol";
            worksheet.Range["E1"].Text = "Unitate Masura";
            worksheet.Range["F1"].Text = "Pret Achizitie";
            worksheet.Range["G1"].Text = "Pret Vanzare";
            worksheet.Range["H1"].Text = "Stoc Minim";
            worksheet.Range["I1"].Text = "Stoc Maxim";
            worksheet.Range["J1"].Text = "Activ";
            worksheet.Range["K1"].Text = "Creat La";

            // Style headers
            var headerRange = worksheet.Range["A1:K1"];
            headerRange.CellStyle.Font.Bold = true;
            headerRange.CellStyle.Color = Syncfusion.Drawing.Color.FromArgb(93, 173, 253); // Light blue
            headerRange.CellStyle.Font.Color = Syncfusion.XlsIO.ExcelKnownColors.White;

            // Add data
            for (int i = 0; i < allData.Count(); i++)
            {
                var item = allData.ElementAt(i);
                var row = i + 2; // Start from row 2

                worksheet.Range[$"A{row}"].Text = item.ArticolCode;
                worksheet.Range[$"B{row}"].Text = item.ArticolName;
                worksheet.Range[$"C{row}"].Text = item.Description ?? "";
                worksheet.Range[$"D{row}"].Text = item.TipArticolName ?? "";
                worksheet.Range[$"E{row}"].Text = item.UnitateMasura;
                worksheet.Range[$"F{row}"].Number = (double)(item.PretAchizitie ?? 0);
                worksheet.Range[$"G{row}"].Number = (double)(item.PretVanzare ?? 0);
                worksheet.Range[$"H{row}"].Number = (double)(item.StocMinim ?? 0);
                worksheet.Range[$"I{row}"].Number = (double)(item.StocMaxim ?? 0);
                worksheet.Range[$"J{row}"].Text = item.IsActive ? "Da" : "Nu";
                worksheet.Range[$"K{row}"].DateTime = item.CreatedAt;
            }

            // Auto-fit columns
            worksheet.UsedRange.AutofitColumns();

            // Format numeric columns
            worksheet.Range["F2:F1000"].NumberFormat = "#,##0.00";
            worksheet.Range["G2:G1000"].NumberFormat = "#,##0.00";
            worksheet.Range["H2:H1000"].NumberFormat = "#,##0.00";
            worksheet.Range["I2:I1000"].NumberFormat = "#,##0.00";
            worksheet.Range["K2:K1000"].NumberFormat = "dd/mm/yyyy hh:mm";

            // Save and download
            var stream = new MemoryStream();
            workbook.SaveAs(stream);
            stream.Position = 0;

            await JS.InvokeVoidAsync("downloadFile", "Articole.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Convert.ToBase64String(stream.ToArray()));

            excelEngine.Dispose();
            workbook.Close();

            Logger.LogInformation("Excel export completed for {Count} articole", allData.Count());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error exporting Articole to Excel");
            errorMessage = "Eroare la exportul Excel.";
        }
    }

    /// <summary>
    /// Exports the grid data to PDF.
    /// </summary>
    private async Task ExportToPdfAsync()
    {
        try
        {
            if (grid == null) return;

            Logger.LogInformation("Starting PDF export for Articole");

            // Get all data for export
            var allData = await ArticoleService.GetAllArticoleAsync();

            // Use Syncfusion PDF export
            var pdfDocument = new Syncfusion.Pdf.PdfDocument();
            var page = pdfDocument.Pages.Add();

            // Create PDF table
            var pdfTable = new Syncfusion.Pdf.Grid.PdfGrid();
            pdfTable.DataSource = allData.Select(item => new
            {
                CodArticol = item.ArticolCode,
                DenumireArticol = item.ArticolName,
                Descriere = item.Description ?? "",
                TipArticol = item.TipArticolName ?? "",
                UnitateMasura = item.UnitateMasura,
                PretAchizitie = item.PretAchizitie?.ToString("N2") ?? "0.00",
                PretVanzare = item.PretVanzare?.ToString("N2") ?? "0.00",
                StocMinim = item.StocMinim?.ToString("N2") ?? "0.00",
                StocMaxim = item.StocMaxim?.ToString("N2") ?? "0.00",
                Activ = item.IsActive ? "Da" : "Nu",
                CreatLa = item.CreatedAt.ToString("dd.MM.yyyy HH:mm")
            }).ToList();

            // Style table
            pdfTable.Headers[0].Style.BackgroundBrush = new Syncfusion.Pdf.Graphics.PdfSolidBrush(new Syncfusion.Pdf.Graphics.PdfColor(93, 193, 253));
            pdfTable.Headers[0].Style.TextBrush = Syncfusion.Pdf.Graphics.PdfBrushes.White;
            pdfTable.Headers[0].Style.Font = new Syncfusion.Pdf.Graphics.PdfStandardFont(Syncfusion.Pdf.Graphics.PdfFontFamily.Helvetica, 10, Syncfusion.Pdf.Graphics.PdfFontStyle.Bold);

            // Draw table
            pdfTable.Draw(page, new Syncfusion.Drawing.PointF(10, 10));

            // Save and download
            using var stream = new MemoryStream();
            pdfDocument.Save(stream);
            pdfDocument.Close(true);

            var fileName = $"Articole_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            await JS.InvokeVoidAsync("downloadFile", fileName, "application/pdf", Convert.ToBase64String(stream.ToArray()));

            successMessage = "Export PDF realizat cu succes.";
            Logger.LogInformation("PDF export completed for {Count} articole", allData.Count());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error exporting Articole to PDF");
            errorMessage = "Eroare la exportul PDF.";
        }
    }

    #endregion

    #region Grid State Management

    /// <summary>
    /// Handles grid state saved event.
    /// </summary>
    private void OnGridStateSaved()
    {
        Logger.LogDebug("Grid state saved");
    }

    /// <summary>
    /// Handles grid state loaded event.
    /// </summary>
    private void OnGridStateLoaded()
    {
        Logger.LogDebug("Grid state loaded");
    }

    /// <summary>
    /// Handles grid state reset event.
    /// </summary>
    private void OnGridStateReset()
    {
        Logger.LogDebug("Grid state reset");
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// Clears the error message.
    /// </summary>
    private void ClearError()
    {
        errorMessage = null;
    }

    /// <summary>
    /// Clears the success message.
    /// </summary>
    private void ClearSuccess()
    {
        successMessage = null;
    }

    #endregion
}