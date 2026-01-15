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
using ValyanERP.Web.Features.Administrare.Parteneri.Models;
using ValyanERP.Web.Features.Administrare.Articole.Models;

namespace ValyanERP.Web.Components.Pages.Achizitii;

public partial class FacturiAchizitie : ComponentBase
{
    #region Injected Services

    [Inject] private IAchizitiiService AchizitiiService { get; set; } = null!;
    [Inject] private IAchizitiiRepository AchizitiiRepository { get; set; } = null!;
    [Inject] private ILogger<FacturiAchizitie> Logger { get; set; } = null!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;

    // TODO: Inject services for Partners and Articole when available
    // [Inject] private IParteneriService ParteneriService { get; set; } = null!;
    // [Inject] private IArticoleService ArticoleService { get; set; } = null!;

    #endregion

    #region Component References

    private SfGrid<Invoice>? grid;
    private GridStateManager? gridStateManager;
    private SfDialog? dialog;

    #endregion

    #region Data Properties

    private IEnumerable<Invoice> invoices = Array.Empty<Invoice>();
    private IEnumerable<DocumentState> documentStates = Array.Empty<DocumentState>();
    private IEnumerable<Partner> partners = Array.Empty<Partner>();
    private IEnumerable<Articol> articole = Array.Empty<Articol>();

    private AchizitiiAdaptor? invoiceAdaptor;

    #endregion

    #region Event Callbacks

    private EventCallback<Invoice> ViewInvoiceCallback => EventCallback.Factory.Create<Invoice>(this, ViewInvoice);
    private EventCallback<Invoice> EditInvoiceCallback => EventCallback.Factory.Create<Invoice>(this, EditInvoice);

    #endregion

    #region UI State Properties

    private int totalRecords;
    private string? errorMessage;
    private string? successMessage;
    private bool isDialogVisible;
    private bool isEditMode;
    private bool isLoading = true;
    private PurchaseInvoiceCreateDto? purchaseInvoiceDto;
    private DocumentState? selectedDocumentState;
    private Partner? selectedPartner;

    #endregion

    #region Lifecycle Methods

    protected override async Task OnInitializedAsync()
    {
        try
        {
            // Initialize adaptor
            invoiceAdaptor = new AchizitiiAdaptor(AchizitiiRepository);

            // Load reference data
            await LoadReferenceData();

            // Load initial data
            await LoadDataAsync();
            
            isLoading = false;
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
            // Load document states
            documentStates = await AchizitiiService.GetDocumentStatesAsync();

            // TODO: Load partners and articole when services are available
            // partners = await ParteneriService.GetAllPartnersAsync();
            // articole = await ArticoleService.GetAllArticoleAsync();

            // For now, create mock data
            partners = new List<Partner>
            {
                new Partner { Id = Guid.NewGuid(), Nume = "SC EXEMPLU SRL" },
                new Partner { Id = Guid.NewGuid(), Nume = "SC TEST SA" }
            };

            articole = new List<Articol>
            {
                new Articol { Id = Guid.NewGuid(), ArticolName = "Articol 1", UnitateMasura = "buc" },
                new Articol { Id = Guid.NewGuid(), ArticolName = "Articol 2", UnitateMasura = "kg" }
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading reference data");
            errorMessage = "Eroare la încărcarea datelor de referință";
        }
    }

    private async Task LoadDataAsync()
    {
        try
        {
            ClearMessages();
            invoices = await AchizitiiService.GetAllInvoicesAsync();
            invoices = invoices.Select((i, index) => { i.RowIndex = index; return i; }).ToList();
            totalRecords = invoices.Count();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading invoices data");
            errorMessage = "Eroare la încărcarea datelor";
        }
    }

    #endregion

    #region UI Event Handlers

    private async Task OnGridStateSaved()
    {
        successMessage = "Configurația grid-ului a fost salvată";
        StateHasChanged();
    }

    private async Task OnGridStateLoaded()
    {
        successMessage = "Configurația grid-ului a fost încărcată";
        StateHasChanged();
    }

    private async Task OnGridStateReset()
    {
        successMessage = "Configurația grid-ului a fost resetată";
        StateHasChanged();
    }

    private async Task OnActionBegin(ActionEventArgs<Invoice> args)
    {
        // Handle grid actions if needed
    }

    private async Task OnActionComplete(ActionEventArgs<Invoice> args)
    {
        // Handle grid actions if needed
    }

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

    private void ClearMessages()
    {
        errorMessage = null;
        successMessage = null;
    }

    #endregion

    #region CRUD Operations

    private async Task OpenCreateDialog()
    {
        try
        {
            Logger.LogInformation("OpenCreateDialog called");
            Logger.LogInformation($"DocumentStates count: {documentStates?.Count() ?? 0}");
            
            if (isLoading)
            {
                Logger.LogWarning("OpenCreateDialog called while loading");
                return;
            }
            
            isEditMode = false;
            purchaseInvoiceDto = new PurchaseInvoiceCreateDto
            {
                DocumentDate = DateTime.Now,
                DocumentStateCode = "C", // Draft
                LineItems = new List<InvoiceLineItemDto>
                {
                    new InvoiceLineItemDto() // Add one empty line
                }
            };

            selectedDocumentState = documentStates.FirstOrDefault(ds => ds.CodStare == "C");
            selectedPartner = null;

            isDialogVisible = true;
            StateHasChanged();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error opening create dialog");
            errorMessage = "Eroare la deschiderea dialogului de creare";
        }
    }

    private async Task ViewInvoice(Invoice invoice)
    {
        // TODO: Implement view functionality
        successMessage = $"Vizualizare factură {invoice.Document?.DocumentNumber}";
    }

    private async Task EditInvoice(Invoice invoice)
    {
        // TODO: Implement edit functionality
        successMessage = $"Editare factură {invoice.Document?.DocumentNumber}";
    }

    private async Task SaveInvoice()
    {
        if (purchaseInvoiceDto == null)
            return;

        try
        {
            // Set selected values
            if (selectedDocumentState != null)
            {
                purchaseInvoiceDto.DocumentStateCode = selectedDocumentState.CodStare;
            }

            if (selectedPartner != null)
            {
                purchaseInvoiceDto.PartnerId = selectedPartner.Id;
            }

            // Get current user
            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;
            var userIdClaim = user.FindFirst("sub") ?? user.FindFirst("UserId");
            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
            {
                errorMessage = "Utilizatorul nu este autentificat corespunzător";
                return;
            }

            // Create the purchase invoice
            var result = await AchizitiiService.CreatePurchaseInvoiceAsync(purchaseInvoiceDto, userId);

            if (result.Success)
            {
                successMessage = "Factura de achiziție a fost creată cu succes";
                isDialogVisible = false;
                await LoadDataAsync();
            }
            else
            {
                errorMessage = string.Join(", ", result.Errors);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error saving invoice");
            errorMessage = "Eroare la salvarea facturii";
        }
    }

    private void CloseDialog()
    {
        isDialogVisible = false;
        purchaseInvoiceDto = null;
        selectedDocumentState = null;
        selectedPartner = null;
        StateHasChanged();
    }

    private async Task HandleValidSubmit()
    {
        await SaveInvoice();
    }

    private async Task ViewInvoiceByIndex(int index)
    {
        var invoice = invoices.ElementAtOrDefault(index);
        if (invoice != null)
        {
            await ViewInvoice(invoice);
        }
    }

    private async Task EditInvoiceByIndex(int index)
    {
        var invoice = invoices.ElementAtOrDefault(index);
        if (invoice != null)
        {
            await EditInvoice(invoice);
        }
    }

    private async Task RefreshData()
    {
        await LoadDataAsync();
    }

    #endregion

    #region Line Items Management

    private void AddLineItem()
    {
        if (purchaseInvoiceDto != null)
        {
            purchaseInvoiceDto.LineItems.Add(new InvoiceLineItemDto());
            StateHasChanged();
        }
    }

    private void RemoveLineItem(int index)
    {
        if (purchaseInvoiceDto != null && index >= 0 && index < purchaseInvoiceDto.LineItems.Count)
        {
            purchaseInvoiceDto.LineItems.RemoveAt(index);
            StateHasChanged();
        }
    }

    private decimal CalculateLineTotal(InvoiceLineItemDto item)
    {
        var subtotal = item.Quantity * item.UnitPrice;
        var vatAmount = subtotal * (item.VATRate / 100);
        return subtotal + vatAmount;
    }

    #endregion
}