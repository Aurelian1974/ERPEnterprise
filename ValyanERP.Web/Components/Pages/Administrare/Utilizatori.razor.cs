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
    /// Whether to show view dialog.
    /// </summary>
    private bool showViewDialog = false;
    
    /// <summary>
    /// User being viewed in view dialog (read-only).
    /// </summary>
    private User? viewUser;
    
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
        new ItemModel { Text = "Vizualizare", TooltipText = "Vizualizează detaliile utilizatorului selectat", PrefixIcon = "e-icons e-eye", Id = "View" },
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
    /// Handles row data bound event - applies styling for inactive users.
    /// </summary>
    public void OnRowDataBound(RowDataBoundEventArgs<User> args)
    {
        if (args.Data != null && !args.Data.IsActive)
        {
            // Add CSS class for inactive (soft deleted) users
            args.Row.AddClass(new string[] { "inactive-user-row" });
        }
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
                case "View":
                case "UtilizatoriGrid_view":
                    await OpenViewDialog();
                    break;
                    
                case "Refresh":
                case "UtilizatoriGrid_refresh":
                    await RefreshGrid();
                    break;
                    
                case "UtilizatoriGrid_excelexport":
                    args.Cancel = true; // Prevent default export behavior
                    await ExportToExcel();
                    break;
                    
                case "UtilizatoriGrid_pdfexport":
                    args.Cancel = true; // Prevent default export behavior
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
    
    /// <summary>
    /// Opens the view dialog to display user details (read-only).
    /// </summary>
    private async Task OpenViewDialog()
    {
        if (selectedUser == null)
        {
            errorMessage = "Selectați un utilizator pentru a-i vedea detaliile.";
            return;
        }
        
        viewUser = selectedUser;
        showViewDialog = true;
        Logger.LogDebug("Opened view dialog for user: {UserName}", selectedUser.UserName);
        await Task.CompletedTask;
    }
    
    /// <summary>
    /// Closes the view dialog.
    /// </summary>
    private void CloseViewDialog()
    {
        showViewDialog = false;
        viewUser = null;
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
    /// Exports grid data to Excel using XlsIO directly.
    /// This approach bypasses the adaptor and avoids issues with CustomAdaptor.
    /// </summary>
    private async Task ExportToExcel()
    {
        if (grid == null) return;
        
        try
        {
            await grid.ShowSpinnerAsync();
            
            // Get all users for export
            var allUsers = (await UsersService.GetAllUsersAsync()).ToList();
            
            using var excelEngine = new Syncfusion.XlsIO.ExcelEngine();
            var application = excelEngine.Excel;
            application.DefaultVersion = Syncfusion.XlsIO.ExcelVersion.Xlsx;
            
            var workbook = application.Workbooks.Create(1);
            var worksheet = workbook.Worksheets[0];
            worksheet.Name = "Utilizatori";
            
            // Headers
            worksheet.Range["A1"].Text = "Utilizator";
            worksheet.Range["B1"].Text = "Nume Persoană";
            worksheet.Range["C1"].Text = "Email";
            worksheet.Range["D1"].Text = "Creat La";
            worksheet.Range["E1"].Text = "Activ";
            
            // Header style
            var headerRange = worksheet.Range["A1:E1"];
            headerRange.CellStyle.Font.Bold = true;
            headerRange.CellStyle.Color = Syncfusion.Drawing.Color.FromArgb(96, 165, 250);
            headerRange.CellStyle.Font.Color = Syncfusion.XlsIO.ExcelKnownColors.White;
            
            // Data rows
            int row = 2;
            foreach (var user in allUsers)
            {
                worksheet.Range[$"A{row}"].Text = user.UserName ?? "";
                worksheet.Range[$"B{row}"].Text = user.NumeComplet ?? "";
                worksheet.Range[$"C{row}"].Text = user.Email ?? "";
                worksheet.Range[$"D{row}"].DateTime = user.CreatedAt;
                worksheet.Range[$"D{row}"].NumberFormat = "dd.MM.yyyy HH:mm";
                worksheet.Range[$"E{row}"].Text = user.IsActive ? "Da" : "Nu";
                row++;
            }
            
            // Auto-fit columns
            worksheet.UsedRange.AutofitColumns();
            
            // Save to stream
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            stream.Position = 0;
            
            var fileName = $"Utilizatori_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            var base64 = Convert.ToBase64String(stream.ToArray());
            
            // Download file via JS
            await JS.InvokeVoidAsync("downloadFile", fileName, base64, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            
            Logger.LogInformation("Exported {Count} users to Excel", allUsers.Count);
            successMessage = $"Exportat {allUsers.Count} utilizatori în Excel.";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error exporting to Excel");
            errorMessage = $"Eroare la export Excel: {ex.Message}";
        }
        finally
        {
            await grid.HideSpinnerAsync();
        }
    }
    
    /// <summary>
    /// Exports grid data to PDF using PdfExport directly.
    /// </summary>
    private async Task ExportToPdf()
    {
        if (grid == null) return;
        
        try
        {
            await grid.ShowSpinnerAsync();
            
            // Get all users for export
            var allUsers = (await UsersService.GetAllUsersAsync()).ToList();
            
            using var document = new Syncfusion.Pdf.PdfDocument();
            var page = document.Pages.Add();
            
            var graphics = page.Graphics;
            var font = new Syncfusion.Pdf.Graphics.PdfStandardFont(Syncfusion.Pdf.Graphics.PdfFontFamily.Helvetica, 12);
            var headerFont = new Syncfusion.Pdf.Graphics.PdfStandardFont(Syncfusion.Pdf.Graphics.PdfFontFamily.Helvetica, 14, Syncfusion.Pdf.Graphics.PdfFontStyle.Bold);
            
            // Title
            graphics.DrawString("Lista Utilizatori", headerFont, Syncfusion.Pdf.Graphics.PdfBrushes.DarkBlue, new Syncfusion.Drawing.PointF(10, 10));
            
            // Create table
            var pdfGrid = new Syncfusion.Pdf.Grid.PdfGrid();
            pdfGrid.Columns.Add(5);
            
            // Headers
            var headerRow = pdfGrid.Headers.Add(1)[0];
            headerRow.Cells[0].Value = "Utilizator";
            headerRow.Cells[1].Value = "Nume Persoană";
            headerRow.Cells[2].Value = "Email";
            headerRow.Cells[3].Value = "Creat La";
            headerRow.Cells[4].Value = "Activ";
            
            // Style headers
            foreach (Syncfusion.Pdf.Grid.PdfGridCell cell in headerRow.Cells)
            {
                cell.Style.Font = new Syncfusion.Pdf.Graphics.PdfStandardFont(Syncfusion.Pdf.Graphics.PdfFontFamily.Helvetica, 10, Syncfusion.Pdf.Graphics.PdfFontStyle.Bold);
                cell.Style.BackgroundBrush = new Syncfusion.Pdf.Graphics.PdfSolidBrush(Syncfusion.Drawing.Color.FromArgb(255, 96, 165, 250));
                cell.Style.TextBrush = Syncfusion.Pdf.Graphics.PdfBrushes.White;
            }
            
            // Data rows
            foreach (var user in allUsers)
            {
                var dataRow = pdfGrid.Rows.Add();
                dataRow.Cells[0].Value = user.UserName ?? "";
                dataRow.Cells[1].Value = user.NumeComplet ?? "";
                dataRow.Cells[2].Value = user.Email ?? "";
                dataRow.Cells[3].Value = user.CreatedAt.ToString("dd.MM.yyyy HH:mm");
                dataRow.Cells[4].Value = user.IsActive ? "Da" : "Nu";
            }
            
            // Draw table
            pdfGrid.Draw(page, new Syncfusion.Drawing.PointF(10, 40));
            
            // Save to stream
            using var stream = new MemoryStream();
            document.Save(stream);
            stream.Position = 0;
            
            var fileName = $"Utilizatori_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            var base64 = Convert.ToBase64String(stream.ToArray());
            
            // Download file via JS
            await JS.InvokeVoidAsync("downloadFile", fileName, base64, "application/pdf");
            
            Logger.LogInformation("Exported {Count} users to PDF", allUsers.Count);
            successMessage = $"Exportat {allUsers.Count} utilizatori în PDF.";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error exporting to PDF");
            errorMessage = $"Eroare la export PDF: {ex.Message}";
        }
        finally
        {
            await grid.HideSpinnerAsync();
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
