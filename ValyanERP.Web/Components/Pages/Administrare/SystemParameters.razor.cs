// ================================================================
// FILE: SystemParameters.razor.cs
// PURPOSE: Code-behind for SystemParameters admin page
// ARCHITECTURE: Blazor code-behind pattern with service layer
// ================================================================

using Microsoft.AspNetCore.Components;
using Syncfusion.Blazor.Grids;
using Syncfusion.Blazor.Notifications;
using Syncfusion.Blazor.Popups;
using ValyanERP.Web.Features.Infrastructure.SystemParameters.Models;
using ValyanERP.Web.Features.Infrastructure.SystemParameters.Services;

namespace ValyanERP.Web.Components.Pages.Administrare;

/// <summary>
/// Admin page for managing system configuration parameters.
/// </summary>
public partial class SystemParameters : ComponentBase
{
    private SfGrid<SystemParameter>? grid;
    private SfToast? ToastObj;
    private SfDialog? confirmDeleteDialog;
    private List<SystemParameter> parameters = new();
    private SystemParameter? parameterToDelete;
    private bool isDeleteDialogVisible = false;
    
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
        try
        {
            var result = await ParametersService.GetAllAsync(includeReadOnly: true);
            parameters = result.ToList();
            
            Logger.LogInformation("Loaded {Count} system parameters", parameters.Count);
        }
        catch (Exception ex)
        {
            await ShowToastAsync(
                "Eroare la încărcarea parametrilor. Vă rugăm reîncercați.",
                "Eroare",
                "e-toast-danger"
            );
            Logger.LogError(ex, "Error loading system parameters");
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
    private void OnRowSelected(RowSelectEventArgs<SystemParameter> args)
    {
        // Row selected - we can trigger edit manually here if needed
        Logger.LogDebug("Row selected: {Key}", args.Data.ParameterKey);
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
        }
    }

    /// <summary>
    /// Handle grid actions (Create, Update, Delete).
    /// </summary>
    private async Task ActionBeginHandler(ActionEventArgs<SystemParameter> args)
    {
        Logger.LogDebug("🔥 ActionBeginHandler: RequestType={RequestType}, Action={Action}", 
            args.RequestType, args.Action);
        
        try
        {
            if (args.RequestType == Syncfusion.Blazor.Grids.Action.Save)
            {
                var param = args.Data;
                
                // Check if read-only
                if (param.IsReadOnly)
                {
                    args.Cancel = true;
                    await ShowToastAsync(
                        "Parametrii read-only nu pot fi modificați! Aceștia sunt critici pentru sistem.",
                        "Eroare",
                        "e-toast-danger"
                    );
                    return;
                }

                // Validation
                if (string.IsNullOrWhiteSpace(param.ParameterValue))
                {
                    args.Cancel = true;
                    await ShowToastAsync(
                        "Valoarea parametrului este obligatorie!",
                        "Validare eșuată",
                        "e-toast-warning"
                    );
                    return;
                }

                // Additional validation based on data type
                if (param.DataType == "int" && !int.TryParse(param.ParameterValue, out _))
                {
                    args.Cancel = true;
                    await ShowToastAsync(
                        "Valoarea trebuie să fie un număr întreg!",
                        "Validare eșuată",
                        "e-toast-warning"
                    );
                    return;
                }
                
                if (param.DataType == "decimal" && !decimal.TryParse(param.ParameterValue, out _))
                {
                    args.Cancel = true;
                    await ShowToastAsync(
                        "Valoarea trebuie să fie un număr zecimal valid!",
                        "Validare eșuată",
                        "e-toast-warning"
                    );
                    return;
                }

                try
                {
                    var success = await ParametersService.UpdateAsync(param);
                    
                    if (success)
                    {
                        await ShowToastAsync(
                            $"Parametrul '{param.DisplayName}' a fost actualizat cu succes!",
                            "Succes",
                            "e-toast-success"
                        );
                        Logger.LogInformation("✅ Updated parameter: {Key}", param.ParameterKey);
                    }
                    else
                    {
                        args.Cancel = true;
                        await ShowToastAsync(
                            "Parametrul nu a putut fi actualizat. Verificați dacă există sau nu este read-only.",
                            "Eroare",
                            "e-toast-danger"
                        );
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
                await ShowToastAsync(
                    $"Parametrul '{parameterToDelete.DisplayName}' a fost șters cu succes!",
                    "Succes",
                    "e-toast-success"
                );
                Logger.LogInformation("✅ Deleted parameter: {Key}", parameterToDelete.ParameterKey);
                
                // Reload data
                await LoadParametersAsync();
                if (grid != null)
                {
                    await grid.Refresh();
                }
            }
            else
            {
                await ShowToastAsync(
                    "Parametrul nu a putut fi șters. Verificați dacă există sau nu este read-only.",
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
            Logger.LogDebug("Grid refreshed after {Action} operation", args.RequestType);
        }
    }
    
    /// <summary>
    /// Handle action failures.
    /// </summary>
    private async Task ActionFailureHandler(FailureEventArgs args)
    {
        await ShowToastAsync(
            $"Operație eșuată: {args.Error}",
            "Eroare",
            "e-toast-danger"
        );
        Logger.LogError("Grid action failed: {Error}", args.Error);
    }
    
    /// <summary>
    /// Handle toolbar button clicks (Excel export).
    /// </summary>
    private async Task ToolbarClickHandler(Syncfusion.Blazor.Navigations.ClickEventArgs args)
    {
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
                Logger.LogInformation("Exported system parameters to Excel");
            }
        }
    }
}
