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
using Microsoft.AspNetCore.Components.Forms;

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
    
    private string? cnpCustomError;
    // ValidationMessageStore reused for the active EditContext to avoid orphaned messages
    private ValidationMessageStore? _validationMessageStore;
    private EditContext? _activeEditContext;
    private EditContext? _currentEditContext; // Stores current EditContext from EditForm

    /// <summary>
    /// Handles grid action events (Save, Delete, etc.)
    /// </summary>
    public async Task ActionBeginHandler(ActionEventArgs<Persoana> args)
    {
        try
        {
            // Don't clear errors/success messages here - let them stay visible during edit
            
            if (args.RequestType == Syncfusion.Blazor.Grids.Action.Save)
            {
                // Additional validation before server call
                var persoana = args.Data;
                if (persoana != null && _currentEditContext != null)
                {
                    // Clear previous validation messages
                    if (_validationMessageStore != null)
                    {
                        _validationMessageStore.Clear();
                    }
                    else
                    {
                        _validationMessageStore = new ValidationMessageStore(_currentEditContext);
                    }
                    
                    bool hasErrors = false;
                    
                    // Validate required fields
                    if (string.IsNullOrWhiteSpace(persoana.Nume))
                    {
                        var fi = new FieldIdentifier(persoana, nameof(Persoana.Nume));
                        _validationMessageStore.Add(fi, "Numele este obligatoriu.");
                        hasErrors = true;
                    }
                    
                    if (string.IsNullOrWhiteSpace(persoana.Prenume))
                    {
                        var fi = new FieldIdentifier(persoana, nameof(Persoana.Prenume));
                        _validationMessageStore.Add(fi, "Prenumele este obligatoriu.");
                        hasErrors = true;
                    }
                    
                    // Validate CNP
                    if (!string.IsNullOrWhiteSpace(persoana.CNP))
                    {
                        if (!ValyanERP.Web.Features.Administrare.Persoane.Services.PersoaneService.ValidateCNP(persoana.CNP))
                        {
                            var fi = new FieldIdentifier(persoana, nameof(Persoana.CNP));
                            _validationMessageStore.Add(fi, "CNP-ul introdus nu este valid. Verificați formatul (13 cifre cu checksum corect).");
                            hasErrors = true;
                        }
                    }
                    
                    // Validate Email format
                    if (!string.IsNullOrWhiteSpace(persoana.Email))
                    {
                        var emailRegex = new System.Text.RegularExpressions.Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
                        if (!emailRegex.IsMatch(persoana.Email))
                        {
                            var fi = new FieldIdentifier(persoana, nameof(Persoana.Email));
                            _validationMessageStore.Add(fi, "Adresa de email nu este validă.");
                            hasErrors = true;
                        }
                    }
                    
                    if (hasErrors)
                    {
                        _currentEditContext.NotifyValidationStateChanged();
                        args.Cancel = true;
                        await InvokeAsync(StateHasChanged);
                        return;
                    }
                }
                
                // Clear any page-level errors before attempting save
                ClearError();
                ClearSuccess();
                
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
    public async Task ActionCompleteHandler(ActionEventArgs<Persoana> args)
    {
        try
        {
            if (args.RequestType == Syncfusion.Blazor.Grids.Action.Save)
            {
                ClearError();
                
                // Clear validation messages after successful save
                if (_validationMessageStore != null && _currentEditContext != null)
                {
                    _validationMessageStore.Clear();
                    _currentEditContext.NotifyValidationStateChanged();
                }
                
                // Reset EditContext references
                _currentEditContext = null;
                _validationMessageStore = null;
                _activeEditContext = null;
                
                successMessage = args.Action == "add" 
                    ? "Persoana a fost adăugată cu succes!" 
                    : "Persoana a fost actualizată cu succes!";
                await LoadTotalRecordsAsync();
                await InvokeAsync(StateHasChanged);
            }

            if (args.RequestType == Syncfusion.Blazor.Grids.Action.Delete)
            {
                ClearError();
                successMessage = "Persoana a fost ștearsă cu succes!";
                await LoadTotalRecordsAsync();
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in ActionCompleteHandler: {Message}", ex.Message);
            errorMessage = $"Eroare: {ex.Message}";
            await InvokeAsync(StateHasChanged);
        }
    }
    
    /// <summary>
    /// Handles grid action failure events.
    /// </summary>
    public async Task ActionFailureHandler(Syncfusion.Blazor.Grids.FailureEventArgs args)
    {
        Logger.LogError("Grid action failed: {Error}", args.Error);
        
        ClearSuccess();
        
        // Parse error message to provide user-friendly feedback
        var errorText = args.Error?.ToString() ?? string.Empty;
        
        // If we have a current edit context, add error to ValidationMessageStore (in dialog)
        if (_currentEditContext != null)
        {
            if (_validationMessageStore == null)
            {
                _validationMessageStore = new ValidationMessageStore(_currentEditContext);
            }
            else
            {
                _validationMessageStore.Clear();
            }
            
            var model = _currentEditContext.Model as Persoana;
            if (model != null)
            {
                // Determine which field has the error and add specific validation message
                if (errorText.Contains("CNP", StringComparison.OrdinalIgnoreCase))
                {
                    var fi = new FieldIdentifier(model, nameof(Persoana.CNP));
                    _validationMessageStore.Add(fi, "CNP-ul introdus nu este valid. Verificați formatul (13 cifre cu checksum corect).");
                }
                else if (errorText.Contains("Email", StringComparison.OrdinalIgnoreCase) && errorText.Contains("există", StringComparison.OrdinalIgnoreCase))
                {
                    var fi = new FieldIdentifier(model, nameof(Persoana.Email));
                    var extractedMessage = ExtractErrorMessage(errorText);
                    _validationMessageStore.Add(fi, extractedMessage);
                }
                else if (errorText.Contains("Email", StringComparison.OrdinalIgnoreCase))
                {
                    var fi = new FieldIdentifier(model, nameof(Persoana.Email));
                    _validationMessageStore.Add(fi, "Adresa de email nu este validă.");
                }
                else if (errorText.Contains("Nume", StringComparison.OrdinalIgnoreCase))
                {
                    var fi = new FieldIdentifier(model, nameof(Persoana.Nume));
                    _validationMessageStore.Add(fi, "Numele este obligatoriu.");
                }
                else if (errorText.Contains("Prenume", StringComparison.OrdinalIgnoreCase))
                {
                    var fi = new FieldIdentifier(model, nameof(Persoana.Prenume));
                    _validationMessageStore.Add(fi, "Prenumele este obligatoriu.");
                }
                else
                {
                    // Generic error - add as model-level error
                    var fi = new FieldIdentifier(model, string.Empty);
                    var extractedMessage = ExtractErrorMessage(errorText);
                    _validationMessageStore.Add(fi, !string.IsNullOrWhiteSpace(extractedMessage) 
                        ? extractedMessage 
                        : "A apărut o eroare la salvare. Vă rugăm verificați datele introduse.");
                }
                
                _currentEditContext.NotifyValidationStateChanged();
            }
        }
        else
        {
            // Fallback to page-level error if dialog is not open
            if (errorText.Contains("CNP-ul introdus nu este valid", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "❌ CNP-ul introdus nu este valid. Verificați formatul (13 cifre valide cu checksum corect).";
            }
            else if (errorText.Contains("CNP", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "❌ CNP-ul introdus nu este valid. Verificați formatul (13 cifre valide).";
            }
            else
            {
                var extractedMessage = ExtractErrorMessage(errorText);
                errorMessage = !string.IsNullOrWhiteSpace(extractedMessage) 
                    ? $"❌ {extractedMessage}" 
                    : "❌ A apărut o eroare la salvare. Vă rugăm verificați datele introduse și încercați din nou.";
            }
        }
        
        // Show full error in logs for debugging
        Logger.LogError("Detailed error: {ErrorDetails}", args.Error);
        
        // Force UI update
        await InvokeAsync(StateHasChanged);
    }
    
    /// <summary>
    /// Extracts meaningful error message from exception string.
    /// </summary>
    private string ExtractErrorMessage(string errorText)
    {
        // Try to extract the message between quotes or after certain patterns
        var lines = errorText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            // Look for Romanian error messages
            if (trimmed.Contains("persoană", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("CNP", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("email", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("există", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("invalid", StringComparison.OrdinalIgnoreCase))
            {
                // Clean up the message
                var cleaned = trimmed.Replace("System.InvalidOperationException:", "")
                                    .Replace("Exception:", "")
                                    .Trim();
                if (!string.IsNullOrWhiteSpace(cleaned) && cleaned.Length < 200)
                {
                    return cleaned;
                }
            }
        }
        return string.Empty;
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
    
    /// <summary>
    /// Handles valid form submission.
    /// Validates CNP before allowing the form to submit.
    /// </summary>
    private async Task HandleValidSubmit()
    {
        // Use the stored EditContext
        var editContext = _currentEditContext;
        if (editContext == null) return;
        
        // Validate CNP one more time before submit
        var persoana = editContext.Model as Persoana;
        if (persoana != null && !string.IsNullOrWhiteSpace(persoana.CNP))
        {
            ValidateCnpField(persoana.CNP, editContext);
            
            // If validation added errors, prevent submit
            if (editContext.GetValidationMessages().Any())
            {
                errorMessage = "Vă rugăm corectați erorile din formular înainte de salvare.";
                await InvokeAsync(StateHasChanged);
                return;
            }
        }
        
        // Validate required fields
        if (string.IsNullOrWhiteSpace(persoana?.Nume))
        {
            errorMessage = "Numele este obligatoriu.";
            await InvokeAsync(StateHasChanged);
            return;
        }
        
        if (string.IsNullOrWhiteSpace(persoana?.Prenume))
        {
            errorMessage = "Prenumele este obligatoriu.";
            await InvokeAsync(StateHasChanged);
            return;
        }
        
        // Form is valid, grid will proceed with save
        ClearError();
    }
    
    /// <summary>
    /// Handles invalid form submission.
    /// Shows error message and logs validation errors.
    /// </summary>
    private async Task HandleInvalidSubmit()
    {
        var editContext = _currentEditContext;
        if (editContext != null)
        {
            var errors = editContext.GetValidationMessages().ToList();
            Logger.LogWarning("Form validation failed with {Count} errors: {Errors}", errors.Count, string.Join(", ", errors));
        }
        errorMessage = "Vă rugăm corectați erorile din formular înainte de salvare.";
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Returns combined CSS class for a field based on the EditContext's FieldCssClass and validation state.
    /// Adds 'is-invalid' when the field contains validation messages so CSS can highlight it.
    /// </summary>
    private string CombineFieldCss(EditContext? editContext, object model, string fieldName)
    {
        if (editContext == null) return string.Empty;
        var fi = new FieldIdentifier(model, fieldName);
        var baseClass = editContext.FieldCssClass(fi) ?? string.Empty;
        var invalid = editContext.GetValidationMessages(fi).Any() ? "is-invalid" : string.Empty;
        return string.Join(' ', new[] { baseClass, invalid }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }
    
    /// <summary>
    /// Returns 'has-error' class if the field has validation errors.
    /// Used to style the entire form-group container.
    /// </summary>
    private string GetValidationClass(EditContext? editContext, object model, string fieldName)
    {
        if (editContext == null) return string.Empty;
        var fi = new FieldIdentifier(model, fieldName);
        return editContext.GetValidationMessages(fi).Any() ? "has-error" : string.Empty;
    }
    
    /// <summary>
    /// Validates CNP field on blur (when user leaves the field).
    /// Provides immediate feedback without waiting for submit.
    /// </summary>
    private void ValidateCnpOnBlur(string? cnp, EditContext? editContext)
    {
        if (editContext == null) return;
        
        // Use the same validation logic as ValidateCnpField
        ValidateCnpField(cnp, editContext);
        
        // Force UI update to show validation message immediately
        InvokeAsync(StateHasChanged);
    }

    private void ValidateCnpField(string? cnp, EditContext? editContext = null)
    {
        cnpCustomError = null;

        // If we have an EditContext, use a single ValidationMessageStore tied to it.
        if (editContext != null)
        {
            // Recreate store when a new EditContext is used (e.g., when opening a new dialog)
            if (!ReferenceEquals(editContext, _activeEditContext))
            {
                _validationMessageStore = new ValidationMessageStore(editContext);
                _activeEditContext = editContext;
            }

            var fi = new FieldIdentifier(editContext.Model, nameof(Persoana.CNP));

            // Clear previous messages for this field from our store
            _validationMessageStore?.Clear(fi);

            if (!string.IsNullOrWhiteSpace(cnp))
            {
                if (!ValyanERP.Web.Features.Administrare.Persoane.Services.PersoaneService.ValidateCNP(cnp))
                {
                    if (cnp.Length != 13 || !cnp.All(char.IsDigit))
                    {
                        cnpCustomError = "CNP-ul trebuie să conțină exact 13 cifre.";
                    }
                    else if (!int.TryParse(cnp[0].ToString(), out int first) || first < 1 || first > 8)
                    {
                        cnpCustomError = "CNP-ul nu are un format valid (prima cifră).";
                    }
                    else
                    {
                        cnpCustomError = "CNP-ul introdus nu este valid (checksum).";
                    }

                    if (!string.IsNullOrEmpty(cnpCustomError))
                    {
                        _validationMessageStore?.Add(fi, cnpCustomError);
                    }
                }
            }

            // Notify Blazor to re-evaluate validation state and re-render
            editContext.NotifyValidationStateChanged();
            return;
        }

        // Fallback: previous behavior when EditContext not available (rare case)
        if (!string.IsNullOrWhiteSpace(cnp))
        {
            if (!ValyanERP.Web.Features.Administrare.Persoane.Services.PersoaneService.ValidateCNP(cnp))
            {
                if (cnp.Length != 13 || !cnp.All(char.IsDigit))
                {
                    cnpCustomError = "CNP-ul trebuie să conțină exact 13 cifre.";
                }
                else if (int.Parse(cnp[0].ToString()) < 1 || int.Parse(cnp[0].ToString()) > 8)
                {
                    cnpCustomError = "CNP-ul nu are un format valid (prima cifră).";
                }
                else
                {
                    cnpCustomError = "CNP-ul introdus nu este valid (checksum).";
                }
            }
        }
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
