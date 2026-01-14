// ================================================================
// FILE: SystemParameters.razor.cs
// PURPOSE: Code-behind for SystemParameters admin page
// ARCHITECTURE: Blazor code-behind pattern with service layer
// ================================================================

using Microsoft.AspNetCore.Components;
using Syncfusion.Blazor.Grids;
using Syncfusion.Blazor.Notifications;
using Syncfusion.Blazor.Popups;
using ValyanERP.Web.Components.Shared.DataGrid;
using ValyanERP.Web.Features.Infrastructure.SystemParameters.Models;
using ValyanERP.Web.Features.Infrastructure.SystemParameters.Services;
using System.Linq;
using Microsoft.JSInterop;

namespace ValyanERP.Web.Components.Pages.Administrare;

/// <summary>
/// Admin page for managing system configuration parameters.
/// </summary>
public partial class SystemParameters : ComponentBase
{
    private SfGrid<SystemParameter>? grid;
    private GridStateManager? gridStateManager;
    private SfToast? ToastObj;
    private SfDialog? confirmDeleteDialog;
    private List<SystemParameter> parameters = new();

    // JS runtime to trigger short toast sounds
    private IJSRuntime JS { get; set; } = default!;

    // Toggle play sound on toast (default: enabled)
    private bool playToastSoundEnabled = true;
    private SystemParameter? parameterToDelete;
    private bool isDeleteDialogVisible = false;

    // UI state helpers (alerts/loading)
    private string? errorMessage;
    private string? successMessage;
    private bool isLoading = false;

    // Field-specific inline validation messages (per parameter Id)
    private readonly Dictionary<Guid, string> fieldValidationMessages = new();

    // Toolbar configuration (consistent with other pages)
    private List<object> toolbar = new()
    {
        "Add",
        new Syncfusion.Blazor.Navigations.ItemModel { Text = "Edit", PrefixIcon = "e-icons e-edit", Id = "Edit", CssClass = "e-disabled" },
        new Syncfusion.Blazor.Navigations.ItemModel { Text = "Delete", PrefixIcon = "e-icons e-delete", Id = "Delete" },
        new Syncfusion.Blazor.Navigations.ItemModel { Text = "Reîmprospătează", TooltipText = "Reîmprospătează datele", PrefixIcon = "e-icons e-refresh", Id = "Refresh" },
        "Search",
        new Syncfusion.Blazor.Navigations.ItemModel { Type = Syncfusion.Blazor.Navigations.ItemType.Separator },
        "ExcelExport",
        "PdfExport",
        new Syncfusion.Blazor.Navigations.ItemModel { Type = Syncfusion.Blazor.Navigations.ItemType.Separator },
        "ColumnChooser"
    };
    
    // Dialog parameters for edit form
    private readonly DialogSettings dialogParams = new()
    {
        Width = "800px",
        MinHeight = "500px"
    };

    // Dropdown data sources
    private readonly List<string> categoryList = new()
    {
        "Cache",
        "Validation",
        "Business",
        "Session",
        "Performance",
        "Security",
        "UI",
        "Email",
        "Enum"
    };

    private readonly List<string> dataTypeList = new()
    {
        "int",
        "string",
        "bool",
        "decimal",
        "json",
        "enum"
    };

    /// <summary>
    /// Initialize component and load parameters.
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        await LoadParametersAsync();
    }

    /// <summary>
    /// Load all system parameters from service.
    /// </summary>
    private async Task LoadParametersAsync()
    {
        isLoading = true;
        errorMessage = null;
        successMessage = null;
        StateHasChanged();

        try
        {
            var result = await ParametersService.GetAllAsync(includeReadOnly: true);
            parameters = result.ToList();
        }
        catch (Exception ex)
        {
            errorMessage = "Eroare la încărcarea parametrilor. Vă rugăm reîncercați.";
            await ShowToastAsync(
                errorMessage,
                "Eroare",
                "e-toast-danger"
            );
            Logger.LogError(ex, "Error loading system parameters");
        }
        finally
        {
            isLoading = false;
            StateHasChanged();
        }
    }

    /// <summary>
    /// Refresh grid data.
    /// </summary>
    private async Task RefreshGrid()
    {
        await LoadParametersAsync();
        if (grid != null)
        {
            await grid.Refresh();
        }
        
        await ShowToastAsync("Date reîmprospătate cu succes!", "Succes", "e-toast-success");
        StateHasChanged();
    }

    /// <summary>
    /// Handle row selection (for manual edit).
    /// </summary>
    private async Task OnRowSelected(RowSelectEventArgs<SystemParameter> args)
    {
        ToggleEditToolbar(true);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Header text for the edit dialog - matches Utilizatori pattern.
    /// </summary>
    private string GetEditDialogHeaderText(object context)
    {
        if (context is SystemParameter param)
        {
            var isNew = param.Id == default(Guid) || string.IsNullOrWhiteSpace(param.ParameterKey);
            return isNew ? "Creare parametru" : $"Editează: {param.DisplayName ?? param.ParameterKey}";
        }

        return "Editează parametru";
    }

    /// <summary>
    /// Show toast notification.
    /// </summary>
    private async Task ShowToastAsync(string content, string title, string cssClass)
    {
        if (ToastObj != null)
        {
            var toastModel = new ToastModel
            {
                Title = title,
                Content = content,
                CssClass = cssClass,
                Icon = cssClass switch
                {
                    "e-toast-success" => "e-success toast-icons",
                    "e-toast-danger" => "e-error toast-icons",
                    "e-toast-warning" => "e-warning toast-icons",
                    _ => "e-info toast-icons"
                },
                Timeout = 5000
            };
            await ToastObj.ShowAsync(toastModel);

            // Optionally play a short sound to draw attention (WebAudio API)
            if (playToastSoundEnabled)
            {
                var t = cssClass switch
                {
                    "e-toast-success" => "success",
                    "e-toast-danger" => "error",
                    "e-toast-warning" => "warning",
                    _ => "info"
                };
                try
                {
                    await JS.InvokeVoidAsync("playToastSound", t);
                }
                catch
                {
                    // ignore any JS errors
                }
            }
        }
    }

    /// <summary>
    /// Handle grid actions (Create, Update, Delete).
    /// </summary>
    private async Task ActionBeginHandler(ActionEventArgs<SystemParameter> args)
    {
        try
        {
            if (args.RequestType == Syncfusion.Blazor.Grids.Action.Save)
            {
                var param = args.Data;
                if (param == null)
                {
                    args.Cancel = true;
                    errorMessage = "Date invalide pentru parametrul transmis.";
                    await ShowToastAsync(errorMessage, "Eroare", "e-toast-danger");
                    StateHasChanged();
                    return;
                }

                // Clear previous inline validation for this parameter when attempting to save
                fieldValidationMessages.Remove(param.Id);
                
                // Check if read-only
                if (param.IsReadOnly)
                {
                    args.Cancel = true;
                    errorMessage = "Parametrii read-only nu pot fi modificați! Aceștia sunt critici pentru sistem.";
                    // show inline message next to field in dialog
                    fieldValidationMessages[param.Id] = errorMessage;
                    await ShowToastAsync(
                        errorMessage,
                        "Eroare",
                        "e-toast-danger"
                    );
                    StateHasChanged();
                    return;
                }

                // If creating, ensure ParameterKey is provided
                var isCreating = param.Id == default(Guid) || param.Id == Guid.Empty;
                if (isCreating && string.IsNullOrWhiteSpace(param.ParameterKey))
                {
                    args.Cancel = true;
                    errorMessage = "Cheia parametrului este obligatorie pentru crearea unui parametru.";
                    fieldValidationMessages[param.Id] = errorMessage;
                    await ShowToastAsync(errorMessage, "Validare eșuată", "e-toast-warning");
                    StateHasChanged();
                    return;
                }

                // Consolidated validation based on data type
                if (param.DataType != "json" && string.IsNullOrWhiteSpace(param.ParameterValue))
                {
                    args.Cancel = true;
                    errorMessage = "Valoarea parametrului este obligatorie!";
                    fieldValidationMessages[param.Id] = errorMessage;
                    await ShowToastAsync(errorMessage, "Validare eșuată", "e-toast-warning");
                    StateHasChanged();
                    return;
                }

                if (param.DataType == "int")
                {
                    if (!int.TryParse(param.ParameterValue, out _))
                    {
                        args.Cancel = true;
                        errorMessage = "Valoarea trebuie să fie un număr întreg!";
                        fieldValidationMessages[param.Id] = errorMessage;
                        await ShowToastAsync(errorMessage, "Validare eșuată", "e-toast-warning");
                        StateHasChanged();
                        return;
                    }
                }
                else if (param.DataType == "decimal")
                {
                    if (!decimal.TryParse(param.ParameterValue, out _))
                    {
                        args.Cancel = true;
                        errorMessage = "Valoarea trebuie să fie un număr zecimal valid!";
                        fieldValidationMessages[param.Id] = errorMessage;
                        await ShowToastAsync(errorMessage, "Validare eșuată", "e-toast-warning");
                        StateHasChanged();
                        return;
                    }
                }
                else if (param.DataType == "json" && !string.IsNullOrWhiteSpace(param.ParameterValue))
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(param.ParameterValue);
                    }
                    catch (System.Text.Json.JsonException jex)
                    {
                        args.Cancel = true;
                        errorMessage = "Valoarea JSON nu este validă: " + jex.Message;
                        fieldValidationMessages[param.Id] = errorMessage;
                        await ShowToastAsync(errorMessage, "Validare eșuată", "e-toast-warning");
                        StateHasChanged();
                        return;
                    }
                }

                try
                {
                    // Normalize values before persisting
                    param.ParameterValue = param.ParameterValue?.Trim() ?? string.Empty;
                    param.DefaultValue = param.DefaultValue?.Trim();

                    // Determine create vs update by Id
                    bool success;
                    bool isCreate = false;
                if (param.Id == default(Guid) || param.Id == Guid.Empty)
                {
                    // Create
                    var newId = await ParametersService.CreateAsync(param);
                    success = newId != default(Guid) && newId != Guid.Empty;
                    if (success)
                    {
                        param.Id = newId;
                        isCreate = true;
                    }
                }
                else
                {
                    // Update
                    success = await ParametersService.UpdateAsync(param);
                }
                
                if (success)
                {
                    // clear inline field errors after successful update/create
                    fieldValidationMessages.Remove(param.Id);
                    successMessage = isCreate ? $"Parametrul '{param.DisplayName}' a fost creat cu succes!" : $"Parametrul '{param.DisplayName}' a fost actualizat cu succes!";
                    errorMessage = null;
                    await ShowToastAsync(
                        successMessage,
                        "Succes",
                        "e-toast-success"
                    );
                    StateHasChanged();
                }
                else
                {
                    args.Cancel = true;
                    errorMessage = "Parametrul nu a putut fi actualizat. Verificați dacă există sau nu este read-only.";
                    await ShowToastAsync(
                        errorMessage,
                        "Eroare",
                        "e-toast-danger"
                    );
                    StateHasChanged();
                }
                }
                catch (Exception ex)
                {
                    args.Cancel = true;
                    await ShowToastAsync(
                        $"Eroare la actualizarea parametrului: {ex.Message}",
                        "Eroare",
                        "e-toast-danger"
                    );
                    Logger.LogError(ex, "❌ Error updating parameter: {Key}", param.ParameterKey);
                }
            }
            else if (args.RequestType == Syncfusion.Blazor.Grids.Action.Delete)
            {
                // Intercept delete - show confirmation dialog
                args.Cancel = true;
                parameterToDelete = args.Data;
                
                // Check if read-only
                if (parameterToDelete.IsReadOnly)
                {
                    await ShowToastAsync(
                        "Parametrii read-only nu pot fi șterși! Aceștia sunt critici pentru sistem.",
                        "Eroare",
                        "e-toast-danger"
                    );
                    parameterToDelete = null;
                    return;
                }
                
                isDeleteDialogVisible = true;
                StateHasChanged();
            }
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            await ShowToastAsync(
                "Eroare neașteptată. Vă rugăm contactați administratorul.",
                "Eroare",
                "e-toast-danger"
            );
            Logger.LogError(ex, "❌ Unexpected error in ActionBeginHandler");
        }
    }
    
    /// <summary>
    /// Confirm delete operation.
    /// </summary>
    private async Task ConfirmDelete()
    {
        if (parameterToDelete == null) return;
        
        try
        {
            var success = await ParametersService.DeleteAsync(parameterToDelete.Id);
            
            if (success)
            {
                successMessage = $"Parametrul '{parameterToDelete.DisplayName}' a fost șters cu succes!";
                errorMessage = null;
                await ShowToastAsync(
                    successMessage,
                    "Succes",
                    "e-toast-success"
                );
                
                // Reload data
                await LoadParametersAsync();
                if (grid != null)
                {
                    await grid.Refresh();
                }
            }
            else
            {
                errorMessage = "Parametrul nu a putut fi șters. Verificați dacă există sau nu este read-only.";
                await ShowToastAsync(
                    errorMessage,
                    "Eroare",
                    "e-toast-danger"
                );
            }
        }
        catch (Exception ex)
        {
            await ShowToastAsync(
                $"Eroare la ștergerea parametrului: {ex.Message}",
                "Eroare",
                "e-toast-danger"
            );
            Logger.LogError(ex, "❌ Error deleting parameter: {Id}", parameterToDelete.Id);
        }
        finally
        {
            isDeleteDialogVisible = false;
            parameterToDelete = null;
            StateHasChanged();
        }
    }
    
    /// <summary>
    /// Cancel delete operation.
    /// </summary>
    private void CancelDelete()
    {
        isDeleteDialogVisible = false;
        parameterToDelete = null;
        StateHasChanged();
    }

    /// <summary>
    /// Handle action completion (refresh data after CUD operations).
    /// </summary>
    private async Task ActionCompleteHandler(ActionEventArgs<SystemParameter> args)
    {
        if (args.RequestType == Syncfusion.Blazor.Grids.Action.Save)
        {
            await LoadParametersAsync();
            if (grid != null)
            {
                await grid.Refresh();
            }
        }
    }
    
    /// <summary>
    /// Handle action failures.
    /// </summary>
    private async Task ActionFailureHandler(FailureEventArgs args)
    {
        errorMessage = $"Operație eșuată: {args.Error}";
        await ShowToastAsync(
            errorMessage,
            "Eroare",
            "e-toast-danger"
        );
        Logger.LogError("Grid action failed: {Error}", args.Error);
        StateHasChanged();
    }
    
    /// <summary>
    /// Handle toolbar button clicks (Excel export).
    /// </summary>
    private async Task ToolbarClickHandler(Syncfusion.Blazor.Navigations.ClickEventArgs args)
    {
        // Handle Edit - ensure a row is selected otherwise show a toast instead of the default blocking dialog
        if (args.Item.Id == "Edit" || args.Item.Text == "Edit")
        {
            if (grid != null)
            {
                var selectedObjs = await grid.GetSelectedRecordsAsync();
                var selected = selectedObjs?.Cast<SystemParameter>().ToList();
                if (selected == null || !selected.Any())
                {
                    await ShowToastAsync("Selectați un rând pentru editare.", "Atenție", "e-toast-warning");
                    return;
                }

                // Start edit on the currently selected row
                await grid.StartEditAsync();
                return;
            }
        }

        if (args.Item.Id == "grid_excelexport")
        {
            if (grid != null)
            {
                var excelExportProperties = new ExcelExportProperties
                {
                    FileName = $"SystemParameters_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
                };
                await grid.ExportToExcelAsync(excelExportProperties);
                
                await ShowToastAsync(
                    "Export Excel finalizat cu succes!",
                    "Succes",
                    "e-toast-success"
                );
            }
        }
    }

    /// <summary>
    /// Grid state events from GridStateManager.
    /// </summary>
    private async Task OnGridStateSaved(string configName)
    {
        successMessage = $"Configurația '{configName}' a fost salvată.";
        await ShowToastAsync(successMessage, "Succes", "e-toast-success");
        StateHasChanged();
    }

    private async Task OnGridStateLoaded(string configName)
    {
        successMessage = $"Configurația '{configName}' a fost încărcată.";
        await ShowToastAsync(successMessage, "Succes", "e-toast-success");
        StateHasChanged();
    }

    /// <summary>
    /// Returns a short hint for a data type to show as a tooltip.
    /// </summary>
    private string GetDataTypeHint(string? dataType)
    {
        return dataType switch
        {
            "int" => "Număr întreg (ex: 42)",
            "string" => "Text liber",
            "bool" => "Valoare adevărat/fals",
            "decimal" => "Număr zecimal (ex: 3.14)",
            "json" => "JSON (formatat)",
            "enum" => "Valoare dintr-o listă predefinită",
            _ => ""
        };
    }

    private async Task OnGridStateReset()
    {
        successMessage = "Configurația a fost resetată la implicit.";
        await ShowToastAsync(successMessage, "Succes", "e-toast-success");
        StateHasChanged();
    }

    /// <summary>
    /// Handle row deselection to update toolbar state.
    /// </summary>
    private async Task OnRowDeselected(RowDeselectEventArgs<SystemParameter> args)
    {
        ToggleEditToolbar(false);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Update the Edit toolbar item enabled/disabled state.
    /// </summary>
    private void ToggleEditToolbar(bool enabled)
    {
        var item = toolbar.OfType<Syncfusion.Blazor.Navigations.ItemModel>().FirstOrDefault(i => i.Id == "Edit" || i.Text == "Edit");
        if (item != null)
        {
            // Syncfusion ItemModel doesn't expose an Enabled property; use CssClass to visually disable
            item.CssClass = enabled ? string.Empty : "e-disabled";
            StateHasChanged();
        }
    }

    /// <summary>
    /// Clear transient alerts.
    /// </summary>
    private void ClearError()
    {
        errorMessage = null;
        StateHasChanged();
    }

    private void ClearSuccess()
    {
        successMessage = null;
        StateHasChanged();
    }
}
