using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Syncfusion.Blazor.Grids;
using Syncfusion.Blazor.DropDowns;
using Syncfusion.Blazor.Navigations;
using Syncfusion.Blazor.Popups;
using Syncfusion.XlsIO;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using System.Drawing;
using ValyanERP.Web.Components.Shared.DataGrid;
using ValyanERP.Web.Features.Administrare.TipuriArticole.Models;
using ValyanERP.Web.Features.Administrare.TipuriArticole.Services;
using FluentValidation;
using FluentValidation.Results;

namespace ValyanERP.Web.Components.Pages.Administrare;

/// <summary>
/// Code-behind for TipuriArticole page.
/// Implements advanced SfDataGrid features:
/// - Server-side pagination, sorting, filtering, grouping
/// - Column reordering, resizing, visibility persistence
/// - User-specific grid configuration saving
/// - Excel/PDF export
/// - Custom cell templates
/// </summary>
public partial class TipuriArticole : ComponentBase, IDisposable
{
    #region Injected Services

    [Inject]
    public IItemTypesService ItemTypesService { get; set; } = default!;

    [Inject]
    private ILogger<TipuriArticole> Logger { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    #endregion

    #region Grid References

    /// <summary>
    /// Reference to the main SfGrid component.
    /// </summary>
    private SfGrid<ValyanERP.Web.Features.Administrare.TipuriArticole.Models.ItemType>? grid;

    /// <summary>
    /// Reference to the GridStateManager component.
    /// </summary>
    private GridStateManager? gridStateManager;

    /// <summary>
    /// Reference to the view dialog.
    /// </summary>
    private SfDialog? viewDialog;

    /// <summary>
    /// Reference to the delete confirmation dialog.
    /// </summary>
    private SfDialog? deleteDialog;

    #endregion

    #region State Variables

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
    /// Currently selected item type (for operations).
    /// </summary>
    private ValyanERP.Web.Features.Administrare.TipuriArticole.Models.ItemType? selectedItemType;

    /// <summary>
    /// Selected item type name for delete confirmation.
    /// </summary>
    private string selectedItemTypeName = string.Empty;

    /// <summary>
    /// Whether to show delete confirmation dialog.
    /// </summary>
    private bool showDeleteConfirm = false;

    /// <summary>
    /// Whether to show view dialog.
    /// </summary>
    private bool showViewDialog = false;

    /// <summary>
    /// Whether to show add dialog.
    /// </summary>
    private bool showAddDialog = false;

    /// <summary>
    /// New item type being created in add dialog.
    /// </summary>
    private ValyanERP.Web.Features.Administrare.TipuriArticole.Models.ItemType newItemType = new();

    /// <summary>
    /// Whether to show edit dialog.
    /// </summary>
    private bool showEditDialog = false;

    /// <summary>
    /// Item type being edited in edit dialog.
    /// </summary>
    private ValyanERP.Web.Features.Administrare.TipuriArticole.Models.ItemType? editingItemType;

    /// <summary>
    /// Item type being viewed in view dialog (read-only).
    /// </summary>
    private ValyanERP.Web.Features.Administrare.TipuriArticole.Models.ItemType? viewedItemType;

    /// <summary>
    /// Whether the page is currently loading.
    /// </summary>
    private bool isLoading = false;

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
        new Syncfusion.Blazor.Navigations.ItemModel { Text = "Adăugare", TooltipText = "Adăugare tip articol nou", PrefixIcon = "e-icons e-add", Id = "AddCustom" },
        new Syncfusion.Blazor.Navigations.ItemModel { Text = "Editare", TooltipText = "Editare tip articol selectat", PrefixIcon = "e-icons e-edit", Id = "EditCustom" },
        new Syncfusion.Blazor.Navigations.ItemModel { Text = "Vizualizare", TooltipText = "Vizualizează detaliile tipului de articol selectat", PrefixIcon = "e-icons e-eye", Id = "View" },
        "Delete",
        new Syncfusion.Blazor.Navigations.ItemModel { Text = "Reîmprospătează", TooltipText = "Reîmprospătează datele", PrefixIcon = "e-icons e-refresh", Id = "Refresh" },
        "Search",
        new Syncfusion.Blazor.Navigations.ItemModel { Type = Syncfusion.Blazor.Navigations.ItemType.Separator },
        "ExcelExport",
        "PdfExport",
        new Syncfusion.Blazor.Navigations.ItemModel { Type = Syncfusion.Blazor.Navigations.ItemType.Separator },
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
        isLoading = true;
        try
        {
            Logger.LogInformation("TipuriArticole page initializing");
            errorMessage = null;

            // Initialize any required data
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error initializing TipuriArticole page");
            errorMessage = "Eroare la inițializarea paginii.";
        }
        finally
        {
            isLoading = false;
        }
    }

    public void Dispose()
    {
        // Cleanup if needed
    }

    #endregion

    #region Grid Event Handlers

    /// <summary>
    /// Handles grid action begin events (add, edit, delete).
    /// Performs validation and custom logic before actions.
    /// </summary>
    public async Task ActionBeginHandler(ActionEventArgs<ValyanERP.Web.Features.Administrare.TipuriArticole.Models.ItemType> args)
    {
        try
        {
            if (args.RequestType == Syncfusion.Blazor.Grids.Action.Add)
            {
                Logger.LogDebug("ActionBegin: Add operation started for ItemType");
                Logger.LogDebug("ActionBegin: Add - Data is null: {IsNull}, Data values: {@Data}", args.Data == null, args.Data);
            }
            else if (args.RequestType == Syncfusion.Blazor.Grids.Action.Save)
            {
                Logger.LogDebug("ActionBegin: Save for ItemType");

                // Validate required fields only when saving
                if (args.Data != null)
                {
                    var validationResult = await ValidateItemTypeAsync(args.Data);
                    if (!validationResult.IsValid)
                    {
                        args.Cancel = true;
                        errorMessage = string.Join(", ", validationResult.Errors.Select(e => e.ErrorMessage));
                        return;
                    }
                }
            }
            else if (args.RequestType == Syncfusion.Blazor.Grids.Action.Delete)
            {
                Logger.LogDebug("ActionBegin: Delete for ItemType {Code}", args.Data?.ItemTypeCode);
                // Delete is handled via custom confirmation dialog
                args.Cancel = true;
                await ShowDeleteConfirmationAsync(args.Data);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in ActionBeginHandler");
            args.Cancel = true;
            errorMessage = "Eroare la procesarea acțiunii.";
        }
    }

    /// <summary>
    /// Handles grid action complete events.
    /// Updates UI state and shows success messages.
    /// </summary>
    public async Task ActionCompleteHandler(ActionEventArgs<ValyanERP.Web.Features.Administrare.TipuriArticole.Models.ItemType> args)
    {
        try
        {
            if (args.RequestType == Syncfusion.Blazor.Grids.Action.Add)
            {
                Logger.LogInformation("ActionComplete: ItemType added successfully");
                successMessage = "Tipul de articol a fost adăugat cu succes.";
                await RefreshGridAsync();
            }
            else if (args.RequestType == Syncfusion.Blazor.Grids.Action.BeginEdit)
            {
                Logger.LogInformation("ActionComplete: ItemType updated successfully");
                successMessage = "Tipul de articol a fost actualizat cu succes.";
                await RefreshGridAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in ActionCompleteHandler");
            errorMessage = "Eroare la finalizarea acțiunii.";
        }
    }

    /// <summary>
    /// Handles row selection events.
    /// </summary>
    public void OnRowSelected(RowSelectEventArgs<ValyanERP.Web.Features.Administrare.TipuriArticole.Models.ItemType> args)
    {
        selectedItemType = args.Data;
        Logger.LogDebug("Row selected: ItemType {Code}", selectedItemType?.ItemTypeCode);
    }

    /// <summary>
    /// Handles row deselection events.
    /// </summary>
    public void OnRowDeselected(RowDeselectEventArgs<ValyanERP.Web.Features.Administrare.TipuriArticole.Models.ItemType> args)
    {
        selectedItemType = null;
        Logger.LogDebug("Row deselected");
    }

    /// <summary>
    /// Handles row data bound events for custom styling.
    /// </summary>
    public void OnRowDataBound(RowDataBoundEventArgs<ValyanERP.Web.Features.Administrare.TipuriArticole.Models.ItemType> args)
    {
        if (args.Data != null && !args.Data.IsActive)
        {
            // Add custom CSS class for inactive items
            args.Row.AddClass(new[] { "inactive-row" });
        }
    }

    /// <summary>
    /// Handles data bound events to update record count.
    /// </summary>
    public void OnDataBound(object? args)
    {
        // Update total records count if available
        if (grid != null)
        {
            // This would be updated from the adaptor, but for now we'll leave it
            Logger.LogDebug("Data bound event triggered");
        }
    }

    #endregion

    #region Toolbar Methods

    /// <summary>
    /// Handles toolbar button clicks.
    /// Note: Built-in Syncfusion toolbar items (Add, Edit, Delete) are handled automatically by the grid.
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
                case "TipuriArticoleGrid_add":
                case "TipuriArticoleGrid_edit":
                case "TipuriArticoleGrid_delete":
                case "TipuriArticoleGrid_search":
                case "TipuriArticoleGrid_columnchooser":
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
    /// Shows the view dialog for the selected item type.
    /// </summary>
    private async Task ShowViewDialogAsync()
    {
        if (selectedItemType == null)
        {
            errorMessage = "Selectați un tip de articol pentru vizualizare.";
            return;
        }

        viewedItemType = selectedItemType;
        showViewDialog = true;
        Logger.LogDebug("View dialog shown for ItemType {Code}", selectedItemType.ItemTypeCode);
    }

    /// <summary>
    /// Closes the view dialog.
    /// </summary>
    private void CloseViewDialog()
    {
        showViewDialog = false;
        viewedItemType = null;
        Logger.LogDebug("View dialog closed");
    }

    /// <summary>
    /// Shows the add dialog for creating a new item type.
    /// </summary>
    private void ShowAddDialog()
    {
        newItemType = new ValyanERP.Web.Features.Administrare.TipuriArticole.Models.ItemType
        {
            IsActive = true // Default to active
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
        newItemType = new ValyanERP.Web.Features.Administrare.TipuriArticole.Models.ItemType();
        Logger.LogDebug("Add dialog closed");
    }

    /// <summary>
    /// Saves the new item type from the add dialog.
    /// </summary>
    private async Task SaveNewItemType()
    {
        try
        {
            // Validate required fields
            if (string.IsNullOrWhiteSpace(newItemType.ItemTypeCode))
            {
                errorMessage = "Codul tipului de articol este obligatoriu.";
                return;
            }

            if (string.IsNullOrWhiteSpace(newItemType.ItemTypeName))
            {
                errorMessage = "Numele tipului de articol este obligatoriu.";
                return;
            }

            await ItemTypesService.CreateItemTypeAsync(new ValyanERP.Web.Features.Administrare.TipuriArticole.Models.ItemTypeCreateDto
            {
                ItemTypeCode = newItemType.ItemTypeCode,
                ItemTypeName = newItemType.ItemTypeName,
                Description = newItemType.Description
            });

            successMessage = $"Tipul de articol '{newItemType.ItemTypeCode}' a fost adăugat cu succes.";
            await RefreshGridAsync();

            CloseAddDialog();
            Logger.LogInformation("New ItemType created: {Code}", newItemType.ItemTypeCode);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error creating new ItemType");
            errorMessage = "Eroare la adăugarea tipului de articol.";
        }
    }

    /// <summary>
    /// Shows the edit dialog for the selected item type.
    /// </summary>
    private async Task ShowEditDialogAsync()
    {
        if (selectedItemType == null)
        {
            errorMessage = "Selectați un tip de articol pentru editare.";
            return;
        }

        // Create a copy of the selected item for editing
        editingItemType = new ValyanERP.Web.Features.Administrare.TipuriArticole.Models.ItemType
        {
            Id = selectedItemType.Id,
            ItemTypeCode = selectedItemType.ItemTypeCode,
            ItemTypeName = selectedItemType.ItemTypeName,
            Description = selectedItemType.Description,
            IsActive = selectedItemType.IsActive,
            CreatedAt = selectedItemType.CreatedAt,
            UpdatedAt = selectedItemType.UpdatedAt,
            OwnerCompanyId = selectedItemType.OwnerCompanyId,
            OwnerWorkPlaceId = selectedItemType.OwnerWorkPlaceId,
            OwnerLocationId = selectedItemType.OwnerLocationId,
            OwnerCompanyName = selectedItemType.OwnerCompanyName
        };

        showEditDialog = true;
        Logger.LogDebug("Edit dialog shown for ItemType {Code}", editingItemType.ItemTypeCode);
    }

    /// <summary>
    /// Closes the edit dialog.
    /// </summary>
    private void CloseEditDialog()
    {
        showEditDialog = false;
        editingItemType = null;
        Logger.LogDebug("Edit dialog closed");
    }

    /// <summary>
    /// Saves the edited item type from the edit dialog.
    /// </summary>
    private async Task SaveEditedItemType()
    {
        try
        {
            if (editingItemType == null)
            {
                errorMessage = "Nu există date pentru salvare.";
                return;
            }

            // Validate required fields
            if (string.IsNullOrWhiteSpace(editingItemType.ItemTypeCode))
            {
                errorMessage = "Codul tipului de articol este obligatoriu.";
                return;
            }

            if (string.IsNullOrWhiteSpace(editingItemType.ItemTypeName))
            {
                errorMessage = "Numele tipului de articol este obligatoriu.";
                return;
            }

            await ItemTypesService.UpdateItemTypeAsync(new ValyanERP.Web.Features.Administrare.TipuriArticole.Models.ItemTypeUpdateDto
            {
                Id = editingItemType.Id,
                ItemTypeCode = editingItemType.ItemTypeCode,
                ItemTypeName = editingItemType.ItemTypeName,
                Description = editingItemType.Description
            });

            var updatedCode = editingItemType.ItemTypeCode;
            successMessage = $"Tipul de articol '{updatedCode}' a fost actualizat cu succes.";
            await RefreshGridAsync();
            CloseEditDialog();
            Logger.LogInformation("ItemType updated: {Code}", updatedCode);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating ItemType");
            errorMessage = "Eroare la actualizarea tipului de articol.";
        }
    }

    /// <summary>
    /// Shows the delete confirmation dialog.
    /// </summary>
    private async Task ShowDeleteConfirmationAsync(ValyanERP.Web.Features.Administrare.TipuriArticole.Models.ItemType? itemType)
    {
        if (itemType == null)
        {
            errorMessage = "Selectați un tip de articol pentru ștergere.";
            return;
        }

        selectedItemType = itemType;
        selectedItemTypeName = $"{itemType.ItemTypeCode} - {itemType.ItemTypeName}";
        showDeleteConfirm = true;
        Logger.LogDebug("Delete confirmation shown for ItemType {Code}", itemType.ItemTypeCode);
    }

    /// <summary>
    /// Cancels the delete operation.
    /// </summary>
    private void CancelDelete()
    {
        showDeleteConfirm = false;
        selectedItemType = null;
        selectedItemTypeName = string.Empty;
        Logger.LogDebug("Delete cancelled");
    }

    /// <summary>
    /// Confirms and executes the delete operation.
    /// </summary>
    private async Task ConfirmDelete()
    {
        if (selectedItemType == null)
        {
            errorMessage = "Niciun tip de articol selectat pentru ștergere.";
            return;
        }

        try
        {
            await ItemTypesService.DeleteItemTypeAsync(selectedItemType.Id);
            successMessage = $"Tipul de articol '{selectedItemTypeName}' a fost șters cu succes.";
            await RefreshGridAsync();

            Logger.LogInformation("ItemType deleted: {Id}", selectedItemType.Id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting ItemType {Id}", selectedItemType?.Id);
            errorMessage = "Eroare la ștergerea tipului de articol.";
        }
        finally
        {
            showDeleteConfirm = false;
            selectedItemType = null;
            selectedItemTypeName = string.Empty;
        }
    }

    #endregion

    #region Export Methods

    /// <summary>
    /// Exports grid data to Excel.
    /// </summary>
    private async Task ExportToExcelAsync()
    {
        try
        {
            if (grid == null) return;

            Logger.LogInformation("Starting Excel export");

            // Get all data for export
            var allData = await ItemTypesService.GetAllItemTypesAsync();

            // Use Syncfusion Excel export
            var excelEngine = new Syncfusion.XlsIO.ExcelEngine();
            var workbook = excelEngine.Excel.Workbooks.Create();
            var worksheet = workbook.Worksheets[0];
            worksheet.Name = "TipuriArticole";

            // Add headers
            worksheet.Range["A1"].Text = "Cod Tip";
            worksheet.Range["B1"].Text = "Denumire Tip";
            worksheet.Range["C1"].Text = "Descriere";
            worksheet.Range["D1"].Text = "Activ";
            worksheet.Range["E1"].Text = "Creat La";

            // Style headers
            var headerRange = worksheet.Range["A1:E1"];
            headerRange.CellStyle.Font.Bold = true;

            // Add data
            for (int i = 0; i < allData.Count(); i++)
            {
                var item = allData.ElementAt(i);
                worksheet.Range[$"A{i + 2}"].Text = item.ItemTypeCode;
                worksheet.Range[$"B{i + 2}"].Text = item.ItemTypeName;
                worksheet.Range[$"C{i + 2}"].Text = item.Description ?? "";
                worksheet.Range[$"D{i + 2}"].Text = item.IsActive ? "Da" : "Nu";
                worksheet.Range[$"E{i + 2}"].Text = item.CreatedAt.ToString("dd.MM.yyyy HH:mm");
            }

            // Auto-fit columns
            worksheet.UsedRange.AutofitColumns();

            // Save and download
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            workbook.Close();

            var fileName = $"TipuriArticole_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            await JS.InvokeVoidAsync("downloadFile", fileName, Convert.ToBase64String(stream.ToArray()));

            successMessage = "Export Excel realizat cu succes.";
            Logger.LogInformation("Excel export completed successfully");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error exporting to Excel");
            errorMessage = "Eroare la exportul Excel.";
        }
    }

    /// <summary>
    /// Exports grid data to PDF.
    /// </summary>
    private async Task ExportToPdfAsync()
    {
        try
        {
            if (grid == null) return;

            Logger.LogInformation("Starting PDF export");

            // Get all data for export
            var allData = await ItemTypesService.GetAllItemTypesAsync();

            // Use Syncfusion PDF export
            var pdfDocument = new Syncfusion.Pdf.PdfDocument();
            var page = pdfDocument.Pages.Add();

            // Create PDF table
            var pdfTable = new Syncfusion.Pdf.Grid.PdfGrid();
            pdfTable.DataSource = allData.Select(item => new
            {
                CodTip = item.ItemTypeCode,
                DenumireTip = item.ItemTypeName,
                Descriere = item.Description ?? "",
                Activ = item.IsActive ? "Da" : "Nu",
                CreatLa = item.CreatedAt.ToString("dd.MM.yyyy HH:mm")
            }).ToList();

            // Style table
            pdfTable.Headers[0].Style.BackgroundBrush = new Syncfusion.Pdf.Graphics.PdfSolidBrush(new Syncfusion.Pdf.Graphics.PdfColor(93, 193, 253));
            pdfTable.Headers[0].Style.TextBrush = Syncfusion.Pdf.Graphics.PdfBrushes.White;
            pdfTable.Headers[0].Style.Font = new Syncfusion.Pdf.Graphics.PdfStandardFont(Syncfusion.Pdf.Graphics.PdfFontFamily.Helvetica, 12, Syncfusion.Pdf.Graphics.PdfFontStyle.Bold);

            // Draw table
            pdfTable.Draw(page, new Syncfusion.Drawing.PointF(10, 10));

            // Save and download
            using var stream = new MemoryStream();
            pdfDocument.Save(stream);
            pdfDocument.Close(true);

            var fileName = $"TipuriArticole_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            await JS.InvokeVoidAsync("downloadFile", fileName, Convert.ToBase64String(stream.ToArray()));

            successMessage = "Export PDF realizat cu succes.";
            Logger.LogInformation("PDF export completed successfully");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error exporting to PDF");
            errorMessage = "Eroare la exportul PDF.";
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Refreshes the grid data.
    /// </summary>
    private async Task RefreshGridAsync()
    {
        if (grid != null)
        {
            await grid.Refresh();
            Logger.LogDebug("Grid refreshed");
        }
    }

    /// <summary>
    /// Validates an item type using FluentValidation.
    /// </summary>
    private async Task<ValidationResult> ValidateItemTypeAsync(ValyanERP.Web.Features.Administrare.TipuriArticole.Models.ItemType itemType)
    {
        // Create a validator for ItemType
        var validator = new InlineValidator<ValyanERP.Web.Features.Administrare.TipuriArticole.Models.ItemType>();
        validator.RuleFor(x => x.ItemTypeCode)
            .NotEmpty().WithMessage("Codul tipului este obligatoriu.")
            .MaximumLength(10).WithMessage("Codul tipului nu poate depăși 10 caractere.");

        validator.RuleFor(x => x.ItemTypeName)
            .NotEmpty().WithMessage("Denumirea tipului este obligatorie.")
            .MaximumLength(100).WithMessage("Denumirea tipului nu poate depăși 100 caractere.");

        return await validator.ValidateAsync(itemType);
    }

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

    #region Grid State Management

    /// <summary>
    /// Handles grid state saved event.
    /// </summary>
    private void OnGridStateSaved()
    {
        successMessage = "Configurația grilei a fost salvată.";
        Logger.LogDebug("Grid state saved");
    }

    /// <summary>
    /// Handles grid state loaded event.
    /// </summary>
    private void OnGridStateLoaded()
    {
        successMessage = "Configurația grilei a fost încărcată.";
        Logger.LogDebug("Grid state loaded");
    }

    /// <summary>
    /// Handles grid state reset event.
    /// </summary>
    private void OnGridStateReset()
    {
        successMessage = "Configurația grilei a fost resetată.";
        Logger.LogDebug("Grid state reset");
    }

    #endregion

    /// <summary>
    /// Status item for filtering.
    /// </summary>
    private class StatusItem
    {
        public string Text { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}