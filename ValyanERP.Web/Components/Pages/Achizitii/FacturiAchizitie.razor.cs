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

    // Inject Partner Repository for dropdown data
    [Inject] private IPartnerRepository PartnerRepository { get; set; } = null!;

    // Inject Articole Repository for dropdown data
    [Inject] private IArticoleRepository ArticoleRepository { get; set; } = null!;

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
    // Partner addresses for the create dialog
    private IEnumerable<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerAddress> partnerAddresses = Array.Empty<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerAddress>();
    private Dictionary<ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums.TipAdresa, List<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerAddress>> partnerAddressesByType = new();
    private Dictionary<ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums.TipAdresa, Guid?> selectedAddressByType = new();
    // Partner contacts and bank accounts for the create dialog
    private IEnumerable<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerContact> partnerContacts = Array.Empty<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerContact>();
    private IEnumerable<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerBankAccount> partnerBankAccounts = Array.Empty<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerBankAccount>();
    private Guid? selectedPartnerContactId;
    private Guid? selectedPartnerBankAccountId;

    // View Dialog
    private bool showViewDialog;
    private Invoice? viewInvoice;
    private List<InvoiceDetail> viewInvoiceDetails = new();

    // Edit Dialog
    private bool showEditDialog;
    private SfDialog? editDialog;
    private PurchaseInvoiceEditDto? editInvoiceDto;
    private DocumentState? editSelectedDocumentState;
    private Partner? editSelectedPartner;
    // Partner addresses for the edit dialog
    private IEnumerable<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerAddress> editPartnerAddresses = Array.Empty<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerAddress>();
    private Dictionary<ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums.TipAdresa, List<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerAddress>> editPartnerAddressesByType = new();
    private Dictionary<ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums.TipAdresa, Guid?> editSelectedAddressByType = new();
    // Partner contacts and bank accounts for the edit dialog
    private IEnumerable<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerContact> editPartnerContacts = Array.Empty<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerContact>();
    private IEnumerable<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerBankAccount> editPartnerBankAccounts = Array.Empty<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerBankAccount>();
    private Guid? editSelectedPartnerContactId;
    private Guid? editSelectedPartnerBankAccountId;

    #endregion

    #region Lifecycle Methods

    protected override async Task OnInitializedAsync()
    {
        try
        {
            Logger.LogWarning("OnInitializedAsync started");
            // Initialize adaptor
            invoiceAdaptor = new AchizitiiAdaptor(AchizitiiRepository);

            // Load reference data
            await LoadReferenceData();

            // Load initial data
            await LoadDataAsync();
            
            isLoading = false;
            Logger.LogWarning("OnInitializedAsync completed successfully");
            
            // TEMP: Test if dialog works
            // isDialogVisible = true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error initializing FacturiAchizitie component");
            errorMessage = "Eroare la inițializarea componentei";
            isLoading = false;
            Logger.LogWarning("OnInitializedAsync failed");
        }
    }

    #endregion

    #region Data Loading Methods

    private async Task LoadReferenceData()
    {
        try
        {
            Logger.LogWarning("LoadReferenceData started");
            // Load document states
            documentStates = await AchizitiiService.GetDocumentStatesAsync();
            Logger.LogWarning($"DocumentStates loaded: {documentStates?.Count() ?? 0} items");

            // TODO: Load partners and articole when services are available
            // partners = await ParteneriService.GetAllPartnersAsync();
            // articole = await ArticoleService.GetAllArticoleAsync();

            // Load partners from database
            partners = await PartnerRepository.GetAllForDropdownAsync();
            Logger.LogWarning($"Partners loaded: {partners?.Count() ?? 0} items");

            // Load articole from database
            articole = await ArticoleRepository.GetAllAsync();
            Logger.LogWarning($"Articole loaded: {articole?.Count() ?? 0} items");
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
            Logger.LogWarning("LoadDataAsync started");
            ClearMessages();
            invoices = await AchizitiiService.GetAllInvoicesAsync();
            invoices = invoices.Select((i, index) => { i.RowIndex = index; return i; }).ToList();
            totalRecords = invoices.Count();
            Logger.LogWarning($"LoadDataAsync completed: {totalRecords} invoices loaded");
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
            Logger.LogWarning($"OpenCreateDialog called. isLoading: {isLoading}, documentStates count: {documentStates?.Count() ?? 0}");
            
            if (isLoading)
            {
                Logger.LogWarning("OpenCreateDialog called while loading");
                return;
            }
            
            Logger.LogWarning("OpenCreateDialog proceeding with dialog creation");
            
            isEditMode = false;
            
            // Get current user location
            var userLocationId = await GetCurrentUserLocationIdAsync();
            
            purchaseInvoiceDto = new PurchaseInvoiceCreateDto
            {
                DocumentDate = DateTime.Now,
                DocumentStateCode = "C", // Draft
                OwnerLocationId = userLocationId,
                LineItems = new List<InvoiceLineItemDto>
                {
                    new InvoiceLineItemDto() // Add one empty line
                }
            };

            selectedDocumentState = documentStates.FirstOrDefault(ds => ds.CodStare == "C");
            selectedPartner = null;

            isDialogVisible = true;
            Logger.LogWarning("Dialog visibility set to true");
            StateHasChanged();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error opening create dialog");
            errorMessage = "Eroare la deschiderea dialogului de creare";
        }
    }

    /// <summary>
    /// Loads partner addresses for the create dialog and groups them by TipAdresa.
    /// </summary>
    private async Task LoadPartnerAddressesAsync(Guid partnerId)
    {
        try
        {
            partnerAddresses = await PartnerRepository.GetAddressesByPartnerIdAsync(partnerId);
            partnerAddressesByType = partnerAddresses
                .GroupBy(a => a.TipAdresa)
                .ToDictionary(g => g.Key, g => g.OrderBy(a => a.SortOrder).ThenByDescending(a => a.EstePrincipala).ToList());

            selectedAddressByType.Clear();
            foreach (var kv in partnerAddressesByType)
            {
                // default to principal address if available, otherwise first
                var principal = kv.Value.FirstOrDefault(a => a.EstePrincipala) ?? kv.Value.FirstOrDefault();
                selectedAddressByType[kv.Key] = principal?.Id;
            }

            StateHasChanged();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading partner addresses for partner {PartnerId}", partnerId);
            partnerAddresses = Array.Empty<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerAddress>();
            partnerAddressesByType.Clear();
            selectedAddressByType.Clear();
        }
    }

    private async Task LoadPartnerContactsAsync(Guid partnerId)
    {
        try
        {
            partnerContacts = await PartnerRepository.GetContactsByPartnerIdAsync(partnerId);
            // default select principal contact if any
            var principal = partnerContacts.FirstOrDefault(c => c.EstePrincipal) ?? partnerContacts.FirstOrDefault();
            selectedPartnerContactId = principal?.Id;
            StateHasChanged();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading partner contacts for partner {PartnerId}", partnerId);
            partnerContacts = Array.Empty<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerContact>();
            selectedPartnerContactId = null;
        }
    }

    private async Task LoadPartnerBankAccountsAsync(Guid partnerId)
    {
        try
        {
            partnerBankAccounts = await PartnerRepository.GetBankAccountsByPartnerIdAsync(partnerId);
            var principal = partnerBankAccounts.FirstOrDefault(b => b.EstePrincipal) ?? partnerBankAccounts.FirstOrDefault();
            selectedPartnerBankAccountId = principal?.Id;
            StateHasChanged();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading partner bank accounts for partner {PartnerId}", partnerId);
            partnerBankAccounts = Array.Empty<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerBankAccount>();
            selectedPartnerBankAccountId = null;
        }
    }

    private async Task LoadEditPartnerAddressesAsync(Guid partnerId)
    {
        try
        {
            editPartnerAddresses = await PartnerRepository.GetAddressesByPartnerIdAsync(partnerId);
            editPartnerAddressesByType = editPartnerAddresses
                .GroupBy(a => a.TipAdresa)
                .ToDictionary(g => g.Key, g => g.OrderBy(a => a.SortOrder).ThenByDescending(a => a.EstePrincipala).ToList());

            editSelectedAddressByType.Clear();
            foreach (var kv in editPartnerAddressesByType)
            {
                var principal = kv.Value.FirstOrDefault(a => a.EstePrincipala) ?? kv.Value.FirstOrDefault();
                editSelectedAddressByType[kv.Key] = principal?.Id;
            }

            StateHasChanged();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading edit partner addresses for partner {PartnerId}", partnerId);
            editPartnerAddresses = Array.Empty<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerAddress>();
            editPartnerAddressesByType.Clear();
            editSelectedAddressByType.Clear();
        }
    }

    private async Task LoadEditPartnerContactsAsync(Guid partnerId)
    {
        try
        {
            editPartnerContacts = await PartnerRepository.GetContactsByPartnerIdAsync(partnerId);
            var principal = editPartnerContacts.FirstOrDefault(c => c.EstePrincipal) ?? editPartnerContacts.FirstOrDefault();
            editSelectedPartnerContactId = principal?.Id;
            StateHasChanged();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading edit partner contacts for partner {PartnerId}", partnerId);
            editPartnerContacts = Array.Empty<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerContact>();
            editSelectedPartnerContactId = null;
        }
    }

    private async Task LoadEditPartnerBankAccountsAsync(Guid partnerId)
    {
        try
        {
            editPartnerBankAccounts = await PartnerRepository.GetBankAccountsByPartnerIdAsync(partnerId);
            var principal = editPartnerBankAccounts.FirstOrDefault(b => b.EstePrincipal) ?? editPartnerBankAccounts.FirstOrDefault();
            editSelectedPartnerBankAccountId = principal?.Id;
            StateHasChanged();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading edit partner bank accounts for partner {PartnerId}", partnerId);
            editPartnerBankAccounts = Array.Empty<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerBankAccount>();
            editSelectedPartnerBankAccountId = null;
        }
    }

    private async Task OnPartnerChanged(Syncfusion.Blazor.DropDowns.ChangeEventArgs<Partner, Partner> args)
    {
        try
        {
            selectedPartner = args?.Value;
            if (selectedPartner != null)
            {
                if (purchaseInvoiceDto != null)
                    purchaseInvoiceDto.PartnerId = selectedPartner.Id;

                await LoadPartnerAddressesAsync(selectedPartner.Id);
                await LoadPartnerContactsAsync(selectedPartner.Id);
                await LoadPartnerBankAccountsAsync(selectedPartner.Id);
            }
            else
            {
                partnerAddresses = Array.Empty<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerAddress>();
                partnerAddressesByType.Clear();
                selectedAddressByType.Clear();
                partnerContacts = Array.Empty<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerContact>();
                partnerBankAccounts = Array.Empty<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerBankAccount>();
                selectedPartnerContactId = null;
                selectedPartnerBankAccountId = null;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error handling partner change");
        }
    }

    // Overload to handle ValueChange event which passes the Partner directly
    private async Task OnPartnerChanged(Partner partner)
    {
        try
        {
            selectedPartner = partner;
            if (selectedPartner != null)
            {
                if (purchaseInvoiceDto != null)
                    purchaseInvoiceDto.PartnerId = selectedPartner.Id;

                await LoadPartnerAddressesAsync(selectedPartner.Id);
                await LoadPartnerContactsAsync(selectedPartner.Id);
                await LoadPartnerBankAccountsAsync(selectedPartner.Id);
            }
            else
            {
                partnerAddresses = Array.Empty<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerAddress>();
                partnerAddressesByType.Clear();
                selectedAddressByType.Clear();
                partnerContacts = Array.Empty<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerContact>();
                partnerBankAccounts = Array.Empty<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerBankAccount>();
                selectedPartnerContactId = null;
                selectedPartnerBankAccountId = null;
                StateHasChanged();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error handling partner change (overload)");
        }
    }

    private async Task OnEditPartnerChanged(Syncfusion.Blazor.DropDowns.ChangeEventArgs<Partner, Partner> args)
    {
        try
        {
            editSelectedPartner = args?.Value;
            if (editSelectedPartner != null)
            {
                if (editInvoiceDto != null)
                    editInvoiceDto.PartnerId = editSelectedPartner.Id;

                await LoadEditPartnerAddressesAsync(editSelectedPartner.Id);
                await LoadEditPartnerContactsAsync(editSelectedPartner.Id);
                await LoadEditPartnerBankAccountsAsync(editSelectedPartner.Id);
            }
            else
            {
                editPartnerAddresses = Array.Empty<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerAddress>();
                editPartnerAddressesByType.Clear();
                editSelectedAddressByType.Clear();
                editPartnerContacts = Array.Empty<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerContact>();
                editPartnerBankAccounts = Array.Empty<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerBankAccount>();
                editSelectedPartnerContactId = null;
                editSelectedPartnerBankAccountId = null;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error handling edit partner change");
        }
    }

    // Overload to handle ValueChange event which passes the Partner directly (edit dialog)
    private async Task OnEditPartnerChanged(Partner partner)
    {
        try
        {
            editSelectedPartner = partner;
            if (editSelectedPartner != null)
            {
                if (editInvoiceDto != null)
                    editInvoiceDto.PartnerId = editSelectedPartner.Id;

                await LoadEditPartnerAddressesAsync(editSelectedPartner.Id);
                await LoadEditPartnerContactsAsync(editSelectedPartner.Id);
                await LoadEditPartnerBankAccountsAsync(editSelectedPartner.Id);
            }
            else
            {
                editPartnerAddresses = Array.Empty<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerAddress>();
                editPartnerAddressesByType.Clear();
                editSelectedAddressByType.Clear();
                editPartnerContacts = Array.Empty<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerContact>();
                editPartnerBankAccounts = Array.Empty<ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerBankAccount>();
                editSelectedPartnerContactId = null;
                editSelectedPartnerBankAccountId = null;
                StateHasChanged();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error handling edit partner change (overload)");
        }
    }

    private void OnAddressSelected(ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums.TipAdresa tip, Syncfusion.Blazor.DropDowns.ChangeEventArgs<Guid?, ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerAddress> args)
    {
        selectedAddressByType[tip] = args.Value;
        StateHasChanged();
    }

    private void OnContactSelected(Syncfusion.Blazor.DropDowns.ChangeEventArgs<Guid?, ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerContact> args)
    {
        selectedPartnerContactId = args.Value;
        StateHasChanged();
    }

    private void OnBankAccountSelected(Syncfusion.Blazor.DropDowns.ChangeEventArgs<Guid?, ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerBankAccount> args)
    {
        selectedPartnerBankAccountId = args.Value;
        StateHasChanged();
    }

    private void OnEditAddressSelected(ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums.TipAdresa tip, Syncfusion.Blazor.DropDowns.ChangeEventArgs<Guid?, ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerAddress> args)
    {
        editSelectedAddressByType[tip] = args.Value;
        StateHasChanged();
    }

    private void OnEditContactSelected(Syncfusion.Blazor.DropDowns.ChangeEventArgs<Guid?, ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerContact> args)
    {
        editSelectedPartnerContactId = args.Value;
        StateHasChanged();
    }

    private void OnEditBankAccountSelected(Syncfusion.Blazor.DropDowns.ChangeEventArgs<Guid?, ValyanERP.Web.Features.Administrare.Parteneri.Models.PartnerBankAccount> args)
    {
        editSelectedPartnerBankAccountId = args.Value;
        StateHasChanged();
    }

    private async Task ViewInvoice(Invoice invoice)
    {
        try
        {
            Logger.LogInformation("Opening view dialog for invoice {DocumentNumber}", invoice.Document?.DocumentNumber);

            // Load full invoice details including line items
            var fullInvoice = await AchizitiiRepository.GetInvoiceByIdAsync(invoice.Id);
            var invoiceDetails = await AchizitiiRepository.GetInvoiceDetailsByInvoiceIdAsync(invoice.Id);

            if (fullInvoice == null)
            {
                errorMessage = "Factura nu a fost găsită";
                return;
            }

            // Create a view model with details
            viewInvoice = new Invoice
            {
                Id = fullInvoice.Id,
                DocumentId = fullInvoice.DocumentId,
                Document = fullInvoice.Document,
                PartnerId = fullInvoice.PartnerId,
                PartnerName = fullInvoice.PartnerName,
                DocumentNumber = fullInvoice.DocumentNumber,
                DocumentDate = fullInvoice.DocumentDate,
                DueDate = fullInvoice.DueDate,
                DocumentStateName = fullInvoice.DocumentStateName,
                TotalAmount = fullInvoice.TotalAmount,
                VATAmount = fullInvoice.VATAmount,
                TotalPayment = fullInvoice.TotalPayment,
                Observations = fullInvoice.Observations,
                UserId = fullInvoice.UserId,
                IsActive = fullInvoice.IsActive,
                CreatedAt = fullInvoice.CreatedAt,
                CreatedBy = fullInvoice.CreatedBy,
                UpdatedAt = fullInvoice.UpdatedAt,
                UpdatedBy = fullInvoice.UpdatedBy
            };

            // Store details separately for display
            viewInvoiceDetails = invoiceDetails?.ToList() ?? new List<InvoiceDetail>();

            showViewDialog = true;
            StateHasChanged();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error opening view dialog for invoice {InvoiceId}", invoice.Id);
            errorMessage = "Eroare la deschiderea dialogului de vizualizare";
        }
    }

    private async Task EditInvoice(Invoice invoice)
    {
        try
        {
            Logger.LogInformation("Opening edit dialog for invoice {DocumentNumber}", invoice.Document?.DocumentNumber);

            // Check if invoice is validated - cannot edit validated invoices
            if (invoice.DocumentStateName == "Valid")
            {
                errorMessage = "Factura validată nu poate fi modificată";
                return;
            }

            // Load full invoice details for editing
            var fullInvoice = await AchizitiiRepository.GetInvoiceByIdAsync(invoice.Id);
            var invoiceDetails = await AchizitiiRepository.GetInvoiceDetailsByInvoiceIdAsync(invoice.Id);

            if (fullInvoice == null)
            {
                errorMessage = "Factura nu a fost găsită";
                return;
            }

            // Convert to edit DTO
            editInvoiceDto = new PurchaseInvoiceEditDto
            {
                DocumentId = fullInvoice.DocumentId,
                DocumentNumber = fullInvoice.Document?.DocumentNumber ?? "",
                DocumentDate = fullInvoice.Document?.DocumentDate ?? DateTime.Now,
                DueDate = fullInvoice.Document?.DueDate,
                DocumentObservations = fullInvoice.Document?.Observations ?? "",
                InvoiceObservations = fullInvoice.Observations ?? "",
                PartnerId = fullInvoice.PartnerId,
                LineItems = invoiceDetails?.Select(detail => new InvoiceLineItemDto
                {
                    ItemId = detail.ItemId,
                    Quantity = detail.Quantity,
                    UnitMeasure = detail.UnitMeasure ?? "",
                    UnitPrice = detail.UnitPrice,
                    VATRate = detail.VATPercent
                }).ToList() ?? new List<InvoiceLineItemDto>()
            };

            // Set selected values for dropdowns
            editSelectedDocumentState = documentStates.FirstOrDefault(ds => ds.Id == fullInvoice.Document?.DocumentStateId);
            editSelectedPartner = partners.FirstOrDefault(p => p.Id == fullInvoice.PartnerId);

            showEditDialog = true;
            StateHasChanged();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error opening edit dialog for invoice {InvoiceId}", invoice.Id);
            errorMessage = "Eroare la deschiderea dialogului de editare";
        }
    }

    private async Task ValidateInvoice(Invoice invoice)
    {
        try
        {
            // Get current user
            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
            {
                errorMessage = "Utilizatorul nu este autentificat corespunzător";
                return;
            }

            // Validate the document
            var success = await AchizitiiService.ValidateDocumentAsync(invoice.DocumentId, userId);

            if (success)
            {
                successMessage = $"Factura {invoice.Document?.DocumentNumber} a fost validată cu succes și stocul a fost actualizat";
                await LoadDataAsync();
            }
            else
            {
                errorMessage = "Eroare la validarea facturii";
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error validating invoice {InvoiceId}", invoice.Id);
            errorMessage = "Eroare la validarea facturii";
        }
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

            // Map selected partner addresses into DTO
            if (selectedAddressByType != null && selectedAddressByType.Any())
            {
                purchaseInvoiceDto.PartnerAddressSediuId = selectedAddressByType.ContainsKey(ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums.TipAdresa.Sediu) ? selectedAddressByType[ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums.TipAdresa.Sediu] : null;
                purchaseInvoiceDto.PartnerAddressCorespondentaId = selectedAddressByType.ContainsKey(ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums.TipAdresa.Corespondenta) ? selectedAddressByType[ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums.TipAdresa.Corespondenta] : null;
                purchaseInvoiceDto.PartnerAddressLivrareId = selectedAddressByType.ContainsKey(ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums.TipAdresa.Livrare) ? selectedAddressByType[ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums.TipAdresa.Livrare] : null;
                purchaseInvoiceDto.PartnerAddressFacturareId = selectedAddressByType.ContainsKey(ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums.TipAdresa.Facturare) ? selectedAddressByType[ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums.TipAdresa.Facturare] : null;
            }

            // Map selected partner contact and bank account
            purchaseInvoiceDto.PartnerContactId = selectedPartnerContactId;
            purchaseInvoiceDto.PartnerBankAccountId = selectedPartnerBankAccountId;

            // Get current user
            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier);
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

    private async Task ValidateInvoiceByIndex(int index)
    {
        var invoice = invoices.ElementAtOrDefault(index);
        if (invoice != null)
        {
            await ValidateInvoice(invoice);
        }
    }

    private async Task RefreshData()
    {
        await LoadDataAsync();
    }

    #endregion

    #region User Context Methods

    private async Task<Guid?> GetCurrentUserLocationIdAsync()
    {
        try
        {
            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier);
            
            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
            {
                Logger.LogWarning("Cannot get user ID for location lookup");
                return null;
            }

            return await AchizitiiService.GetUserCurrentLocationAsync(userId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error getting current user location");
            return null;
        }
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

    #region Dialog Management

    private void CloseViewDialog()
    {
        showViewDialog = false;
        viewInvoice = null;
        viewInvoiceDetails.Clear();
        StateHasChanged();
    }

    private void CloseEditDialog()
    {
        showEditDialog = false;
        editInvoiceDto = null;
        editSelectedDocumentState = null;
        editSelectedPartner = null;
        StateHasChanged();
    }

    private async Task HandleEditValidSubmit()
    {
        await SaveEditInvoice();
    }

    private async Task SaveEditInvoice()
    {
        if (editInvoiceDto == null)
            return;

        try
        {
            // Get current user
            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
            {
                errorMessage = "Utilizatorul nu este autentificat corespunzător";
                return;
            }

            // Update the invoice
            // Map selected edit addresses into DTO
            if (editSelectedAddressByType != null && editSelectedAddressByType.Any())
            {
                editInvoiceDto.PartnerAddressSediuId = editSelectedAddressByType.ContainsKey(ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums.TipAdresa.Sediu) ? editSelectedAddressByType[ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums.TipAdresa.Sediu] : null;
                editInvoiceDto.PartnerAddressCorespondentaId = editSelectedAddressByType.ContainsKey(ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums.TipAdresa.Corespondenta) ? editSelectedAddressByType[ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums.TipAdresa.Corespondenta] : null;
                editInvoiceDto.PartnerAddressLivrareId = editSelectedAddressByType.ContainsKey(ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums.TipAdresa.Livrare) ? editSelectedAddressByType[ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums.TipAdresa.Livrare] : null;
                editInvoiceDto.PartnerAddressFacturareId = editSelectedAddressByType.ContainsKey(ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums.TipAdresa.Facturare) ? editSelectedAddressByType[ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums.TipAdresa.Facturare] : null;
            }

            // Map selected partner contact and bank account for edit
            editInvoiceDto.PartnerContactId = editSelectedPartnerContactId;
            editInvoiceDto.PartnerBankAccountId = editSelectedPartnerBankAccountId;

            var success = await AchizitiiService.UpdateInvoiceAsync(editInvoiceDto, userId);

            if (success)
            {
                successMessage = $"Factura {editInvoiceDto.DocumentNumber} a fost actualizată cu succes";
                CloseEditDialog();
                await LoadDataAsync();
            }
            else
            {
                errorMessage = "Eroare la actualizarea facturii";
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating invoice {DocumentId}", editInvoiceDto.DocumentId);
            errorMessage = "Eroare la actualizarea facturii";
        }
    }

    #endregion

    #region Edit Line Items Management

    private void AddEditLineItem()
    {
        if (editInvoiceDto != null)
        {
            editInvoiceDto.LineItems.Add(new InvoiceLineItemDto());
            StateHasChanged();
        }
    }

    private void RemoveEditLineItem(int index)
    {
        if (editInvoiceDto != null && index >= 0 && index < editInvoiceDto.LineItems.Count)
        {
            editInvoiceDto.LineItems.RemoveAt(index);
            StateHasChanged();
        }
    }

    private decimal CalculateEditLineTotal(InvoiceLineItemDto item)
    {
        var subtotal = item.Quantity * item.UnitPrice;
        var vatAmount = subtotal * (item.VATRate / 100);
        return subtotal + vatAmount;
    }

    #endregion
}