using Microsoft.AspNetCore.Components;
using ValyanERP.Web.Features.Infrastructure.Audit.Models;
using ValyanERP.Web.Features.Infrastructure.Audit.Services;
using Syncfusion.Blazor.Popups;
using Syncfusion.Blazor.Grids;
using Syncfusion.Blazor.Notifications;

namespace ValyanERP.Web.Components.Pages.Administrare;

/// <summary>
/// Code-behind for Audit Logs viewer page.
/// Displays comprehensive audit trail with filtering, pagination, and detail view.
/// </summary>
public partial class AuditLogs : ComponentBase
{
    [Inject] private IAuditService AuditService { get; set; } = default!;
    [Inject] private ILogger<AuditLogs> Logger { get; set; } = default!;

    // Component References
    private SfGrid<AuditLog>? auditGrid;
    private SfDialog? detailsDialog;
    private SfToast? ToastObj;

    // Data
    private PagedAuditResult<AuditLog>? auditLogs;
    private AuditLog? selectedLog;

    // UI State
    private bool isLoading = false;

    // Filters
    private string? filterEntityType = "Toate";
    private string? filterOperationType = "Toate";
    private DateTime? filterStartDate;
    private DateTime? filterEndDate;
    private string? filterSearchTerm;
    
    // Filter Data Sources
    private readonly List<string> entityTypes = new()
    {
        "Toate",
        "Persoane",
        "SystemParameters",
        "Users",
        "Roles"
    };
    
    private readonly List<string> operationTypes = new()
    {
        "Toate",
        "Create",
        "Update",
        "Delete"
    };

    // Pagination
    private int currentPage = 1;
    private int pageSize = 20;

    // Auto-refresh tracking
    private bool hasRendered = false;

    protected override async Task OnInitializedAsync()
    {
        // Reset dialog state on initialization
        selectedLog = null;
        
        // Load last 7 days by default
        filterStartDate = DateTime.Now.AddDays(-7);
        filterEndDate = DateTime.Now;
        await LoadAuditLogsAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            hasRendered = true;
        }
        
        // Refresh data when component becomes visible again (after navigation)
        if (!firstRender && !hasRendered)
        {
            hasRendered = true;
            selectedLog = null; // Reset selected log
            await LoadAuditLogsAsync();
        }
    }

    private async Task LoadAuditLogsAsync()
    {
        isLoading = true;
        StateHasChanged();

        try
        {
            // Convert "Toate" to null for filter
            var entityFilter = filterEntityType == "Toate" ? null : filterEntityType;
            var operationFilter = filterOperationType == "Toate" ? null : filterOperationType;
            
            auditLogs = await AuditService.SearchAsync(
                entityType: entityFilter,
                userId: null, // Show all users (admin view)
                operationType: operationFilter,
                startDate: filterStartDate,
                endDate: filterEndDate,
                searchTerm: filterSearchTerm,
                pageNumber: currentPage,
                pageSize: pageSize
            );
                
            if (auditLogs.TotalCount == 0)
            {
                await ShowToastAsync(
                    "Nu au fost găsite înregistrări pentru criteriile selectate.",
                    "Informație",
                    "e-toast-info"
                );
            }
        }
        catch (Exception ex)
        {
            await ShowToastAsync(
                "Eroare la încărcarea înregistrărilor audit. Vă rugăm încercați din nou.",
                "Eroare",
                "e-toast-danger"
            );
            Logger.LogError(ex, "Failed to load audit logs");
        }
        finally
        {
            isLoading = false;
            StateHasChanged();
        }
    }

    private async Task RefreshDataAsync()
    {
        hasRendered = false; // Reset flag to allow refresh
        await LoadAuditLogsAsync();
    }

    private async Task GoToPage(int page)
    {
        if (page < 1 || (auditLogs != null && page > auditLogs.TotalPages))
            return;

        currentPage = page;
        await LoadAuditLogsAsync();
    }

    private async Task ShowDetailsAsync(Guid logId)
    {
        try
        {
            selectedLog = await AuditService.GetByIdAsync(logId);
            
            if (selectedLog != null)
            {
                // Force UI update before showing dialog
                StateHasChanged();
                await Task.Delay(50);
                
                // Show dialog
                if (detailsDialog != null)
                {
                    await detailsDialog.ShowAsync();
                }
            }
            else
            {
                Logger.LogWarning("Audit log not found: {LogId}", logId);
                await ShowToastAsync(
                    "Log-ul audit nu a fost găsit.",
                    "Eroare",
                    "e-toast-danger"
                );
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load audit log details: {LogId}", logId);
            await ShowToastAsync(
                "Eroare la încărcarea detaliilor log-ului.",
                "Eroare",
                "e-toast-danger"
            );
        }
    }

    private async Task CloseDetailsModal()
    {
        if (detailsDialog != null)
        {
            await detailsDialog.HideAsync();
        }
        selectedLog = null;
    }

    private void OnDialogClose(CloseEventArgs args)
    {
        selectedLog = null;
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
                    "e-toast-info" => "e-info toast-icons",
                    _ => "e-info toast-icons"
                },
                Timeout = 5000
            };
            await ToastObj.ShowAsync(toastModel);
        }
    }
    
    /// <summary>
    /// Handle toolbar button clicks (Excel/PDF export).
    /// </summary>
    private async Task ToolbarClickHandler(Syncfusion.Blazor.Navigations.ClickEventArgs args)
    {
        if (auditGrid == null) return;
        
        try
        {
            if (args.Item.Id == "auditGrid_excelexport" || args.Item.Text == "Excel Export")
            {
                var excelExportProperties = new ExcelExportProperties
                {
                    FileName = $"AuditLogs_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
                };
                await auditGrid.ExportToExcelAsync(excelExportProperties);
                
                await ShowToastAsync(
                    "Export Excel finalizat cu succes!",
                    "Succes",
                    "e-toast-success"
                );
            }
            else if (args.Item.Id == "auditGrid_pdfexport" || args.Item.Text == "PDF Export")
            {
                var pdfExportProperties = new PdfExportProperties
                {
                    FileName = $"AuditLogs_{DateTime.Now:yyyyMMdd_HHmmss}.pdf"
                };
                await auditGrid.ExportToPdfAsync(pdfExportProperties);
                
                await ShowToastAsync(
                    "Export PDF finalizat cu succes!",
                    "Succes",
                    "e-toast-success"
                );
            }
        }
        catch (Exception ex)
        {
            await ShowToastAsync(
                $"Eroare la export: {ex.Message}",
                "Eroare",
                "e-toast-danger"
            );
            Logger.LogError(ex, "Failed to export audit logs");
        }
    }
}
