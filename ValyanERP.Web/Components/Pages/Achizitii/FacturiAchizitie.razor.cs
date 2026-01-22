using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using Syncfusion.Blazor;
using Syncfusion.Blazor.Data;
using Syncfusion.Blazor.Popups;
using Syncfusion.Blazor.Grids;
using ValyanERP.Web.Components.Shared.DataGrid;
using ValyanERP.Web.Features.Achizitii;
using ValyanERP.Web.Features.Achizitii.Models;
using ValyanERP.Web.Features.Achizitii.Services;
using ValyanERP.Web.Features.Achizitii.Repositories;
using ValyanERP.Web.Features.Administrare.Parteneri.Repositories;
using ValyanERP.Web.Features.Administrare.Parteneri.Models;
using ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums;
using ValyanERP.Web.Features.Administrare.Articole.Repositories;
using System.Security.Claims;
using ValyanERP.Web.Features.Administrare.Articole.Models;

namespace ValyanERP.Web.Components.Pages.Achizitii;

public partial class FacturiAchizitie : ComponentBase
{
    #region Injected Services

    [Inject] private IAchizitiiService AchizitiiService { get; set; } = null!;
    [Inject] private IAchizitiiRepository AchizitiiRepository { get; set; } = null!;
    [Inject] private ILogger<FacturiAchizitie> Logger { get; set; } = null!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;
    [Inject] private IPartnerRepository ParteneriRepository { get; set; } = null!;
    [Inject] private IArticoleRepository ArticoleRepository { get; set; } = null!;

    #endregion

    #region Component References

    private SfGrid<Invoice>? grid;
    private GridStateManager? gridStateManager;

    #endregion

    #region Data Properties

    private List<Invoice> invoices = new();
    private List<DocumentState> documentStates = new();
    private List<Partner> partners = new();
    private List<Articol> articole = new();

    private AchizitiiAdaptor? invoiceAdaptor;

    #endregion

    #region UI State Properties

    private int totalRecords;
    private string? errorMessage;
    private string? successMessage;
    private bool isLoading = true;

    // Invoice Form Dialog State
    private bool isInvoiceDialogVisible;
    private bool isEditMode;
    private Guid? documentIdToEdit;
    private Guid userId;
    private Guid? currentLocationId;

    // View Dialog State
    private bool showViewDialog;
    private Invoice? viewInvoice;
    private List<InvoiceDetail> viewInvoiceDetails = new();

    #endregion

    #region Lifecycle Methods

    protected override async Task OnInitializedAsync()
    {
        try
        {
            Logger.LogInformation("Initializing FacturiAchizitie component");

            // Get user ID
            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var userIdClaim = authState.User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var parsedUserId))
            {
                userId = parsedUserId;
            }

            // Get user's current location
            currentLocationId = await AchizitiiService.GetUserCurrentLocationAsync(userId);

            // Initialize adaptor
            invoiceAdaptor = new AchizitiiAdaptor(AchizitiiRepository);

            // Load reference data
            await LoadReferenceData();

            // Load initial data
            await LoadDataAsync();

            isLoading = false;
            Logger.LogInformation("FacturiAchizitie component initialized successfully");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error initializing FacturiAchizitie component");
            errorMessage = "Eroare la inițializarea componentei";
            isLoading = false;
        }
    }

    #endregion

    #region Data Loading Methods

    private async Task LoadReferenceData()
    {
        try
        {
            Logger.LogInformation("Loading reference data");

            // Load document states
            var docStates = await AchizitiiService.GetDocumentStatesAsync();
            documentStates = docStates.ToList();
            Logger.LogInformation("Loaded {Count} document states", documentStates.Count);

            // Load partners
            var partnersList = await ParteneriRepository.GetAllAsync();
            partners = partnersList.Where(p => p.IsActive).ToList();
            Logger.LogInformation("Loaded {Count} active partners", partners.Count);

            // Load articole
            var articoleList = await ArticoleRepository.GetAllArticoleAsync();
            articole = articoleList.Where(a => a.IsActive).ToList();
            Logger.LogInformation("Loaded {Count} active articole", articole.Count);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading reference data");
            throw;
        }
    }

    private async Task LoadDataAsync()
    {
        try
        {
            Logger.LogInformation("Loading invoices data");
            var allInvoices = await AchizitiiService.GetAllInvoicesAsync();
            invoices = allInvoices.ToList();

            // Add row index to each invoice for action buttons
            int rowIndex = 0;
            foreach (var invoice in invoices)
            {
                invoice.RowIndex = rowIndex++;
            }

            totalRecords = invoices.Count;
            Logger.LogInformation("Loaded {Count} invoices", totalRecords);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading data");
            errorMessage = "Eroare la încărcarea datelor";
        }
    }

    private async Task RefreshData()
    {
        isLoading = true;
        StateHasChanged();

        try
        {
            await LoadDataAsync();
            successMessage = "Datele au fost reîmprospătate cu succes";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error refreshing data");
            errorMessage = "Eroare la reîmprospătarea datelor";
        }
        finally
        {
            isLoading = false;
            StateHasChanged();
        }
    }

    #endregion

    #region Dialog Handlers - Create/Edit

    private void OpenCreateDialog()
    {
        Logger.LogInformation("Opening create invoice dialog");
        isEditMode = false;
        documentIdToEdit = null;
        isInvoiceDialogVisible = true;
    }

    private async Task EditInvoiceByIndex(int rowIndex)
    {
        try
        {
            var invoice = invoices[rowIndex];
            Logger.LogInformation("Opening edit dialog for invoice {DocumentId}", invoice.DocumentId);

            isEditMode = true;
            documentIdToEdit = invoice.DocumentId;
            isInvoiceDialogVisible = true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error opening edit dialog");
            errorMessage = "Eroare la deschiderea dialogului de editare";
        }
    }

    private async Task HandleDialogVisibilityChanged(bool visible)
    {
        isInvoiceDialogVisible = visible;
        StateHasChanged();
    }

    private async Task HandleInvoiceSaved(bool success)
    {
        if (success)
        {
            successMessage = isEditMode ? "Factura a fost actualizată cu succes" : "Factura a fost creată cu succes";
            await RefreshData();
        }
        else
        {
            errorMessage = isEditMode ? "Eroare la actualizarea facturii" : "Eroare la crearea facturii";
        }

        StateHasChanged();
    }

    #endregion

    #region Dialog Handlers - View

    private async Task ViewInvoiceByIndex(int rowIndex)
    {
        try
        {
            var invoice = invoices[rowIndex];
            Logger.LogInformation("Opening view dialog for invoice {InvoiceId}", invoice.Id);

            viewInvoice = invoice;

            // Load invoice details
            var details = await AchizitiiService.GetInvoiceWithDetailsAsync(invoice.Id);
            viewInvoiceDetails = details?.Details ?? new List<InvoiceDetail>();

            showViewDialog = true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error opening view dialog");
            errorMessage = "Eroare la deschiderea dialogului de vizualizare";
        }
    }

    private void CloseViewDialog()
    {
        showViewDialog = false;
        viewInvoice = null;
        viewInvoiceDetails.Clear();
    }

    #endregion

    #region Validate Invoice

    private async Task ValidateInvoiceByIndex(int rowIndex)
    {
        try
        {
            var invoice = invoices[rowIndex];
            Logger.LogInformation("Validating invoice {DocumentId}", invoice.DocumentId);

            var success = await AchizitiiService.ValidateDocumentAsync(invoice.DocumentId, userId);

            if (success)
            {
                successMessage = "Factura a fost validată cu succes";
                await RefreshData();
            }
            else
            {
                errorMessage = "Eroare la validarea facturii";
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error validating invoice");
            errorMessage = "Eroare la validarea facturii";
        }
    }

    #endregion

    #region Grid Events

    private void OnActionBegin(ActionEventArgs<Invoice> args)
    {
        // Handle grid actions if needed
    }

    private void OnActionComplete(ActionEventArgs<Invoice> args)
    {
        // Handle grid actions completion if needed
    }

    #endregion

    #region Grid State Events

    private void OnGridStateSaved(string stateName)
    {
        Logger.LogInformation("Grid state saved: {StateName}", stateName);
        successMessage = $"Configurația '{stateName}' a fost salvată";
    }

    private void OnGridStateLoaded(string stateName)
    {
        Logger.LogInformation("Grid state loaded: {StateName}", stateName);
        successMessage = $"Configurația '{stateName}' a fost încărcată";
    }

    private void OnGridStateReset()
    {
        Logger.LogInformation("Grid state reset");
        successMessage = "Configurația grid-ului a fost resetată";
    }

    #endregion

    #region Message Handlers

    private void ClearError()
    {
        errorMessage = null;
    }

    private void ClearSuccess()
    {
        successMessage = null;
    }

    #endregion
}
