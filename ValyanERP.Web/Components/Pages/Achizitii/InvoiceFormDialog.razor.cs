using Microsoft.AspNetCore.Components;
using Syncfusion.Blazor.Popups;
using Syncfusion.Blazor.DropDowns;
using ValyanERP.Web.Features.Achizitii.Models;
using ValyanERP.Web.Features.Administrare.Parteneri.Models;
using ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums;
using ValyanERP.Web.Features.Administrare.Parteneri.Repositories;
using ValyanERP.Web.Features.Administrare.Articole.Models;
using ValyanERP.Web.Features.Administrare.Articole.Repositories;
using ValyanERP.Web.Features.Achizitii.Services;

namespace ValyanERP.Web.Components.Pages.Achizitii;

public partial class InvoiceFormDialog : ComponentBase
{
    #region Services

    [Inject] private IPartnerRepository ParteneriRepository { get; set; } = default!;
    [Inject] private IArticoleRepository ArticoleRepository { get; set; } = default!;
    [Inject] private IAchizitiiService AchizitiiService { get; set; } = default!;
    [Inject] private ILogger<InvoiceFormDialog> Logger { get; set; } = default!;

    #endregion

    #region Parameters

    [Parameter] public bool IsVisible { get; set; }
    [Parameter] public EventCallback<bool> IsVisibleChanged { get; set; }

    [Parameter] public bool IsEditMode { get; set; }

    [Parameter] public List<Partner> Partners { get; set; } = new();
    [Parameter] public List<Articol> Articole { get; set; } = new();
    [Parameter] public List<DocumentState> DocumentStates { get; set; } = new();

    [Parameter] public Guid? DocumentIdToEdit { get; set; }
    [Parameter] public Guid UserId { get; set; }
    [Parameter] public Guid? CurrentLocationId { get; set; }

    [Parameter] public EventCallback<bool> OnSaveCompleted { get; set; }

    #endregion

    #region Private Fields

    private SfDialog? dialog;

    // Invoice DTO - works for both Create and Edit
    private object? invoiceDto;

    // Partner related
    private Partner? selectedPartner;
    private Dictionary<TipAdresa, List<PartnerAddress>> partnerAddressesByType = new();
    private Dictionary<TipAdresa, Guid?> selectedAddressByType = new();
    private List<PartnerContact> partnerContacts = new();
    private Guid? selectedPartnerContactId;
    private List<PartnerBankAccount> partnerBankAccounts = new();
    private Guid? selectedPartnerBankAccountId;

    // Document state
    private DocumentState? selectedDocumentState;

    #endregion

    #region Lifecycle Methods

    protected override async Task OnParametersSetAsync()
    {
        if (IsVisible && invoiceDto == null)
        {
            await InitializeDialog();
        }
    }

    #endregion

    #region Initialization

    private async Task InitializeDialog()
    {
        try
        {
            if (IsEditMode && DocumentIdToEdit.HasValue)
            {
                await LoadInvoiceForEdit(DocumentIdToEdit.Value);
            }
            else
            {
                CreateNewInvoice();
            }

            // Set default document state
            selectedDocumentState = DocumentStates.FirstOrDefault(ds => ds.CodStare == "C");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error initializing invoice dialog");
        }
    }

    private void CreateNewInvoice()
    {
        var createDto = new PurchaseInvoiceCreateDto
        {
            DocumentDate = DateTime.Now,
            DocumentNumber = string.Empty,
            DocumentStateCode = "C",
            LineItems = new List<InvoiceLineItemDto>(),
            OwnerCompanyId = null,
            OwnerWorkPlaceId = null,
            OwnerLocationId = CurrentLocationId
        };

        invoiceDto = createDto;
    }

    private async Task LoadInvoiceForEdit(Guid documentId)
    {
        // Get invoice by document ID
        var invoice = await AchizitiiService.GetAllInvoicesAsync();
        var invoiceToEdit = invoice.FirstOrDefault(i => i.DocumentId == documentId);

        if (invoiceToEdit == null)
        {
            Logger.LogWarning("Invoice not found for document {DocumentId}", documentId);
            CreateNewInvoice();
            return;
        }

        // Get invoice details
        var invoiceDetails = await AchizitiiService.GetInvoiceWithDetailsAsync(invoiceToEdit.Id);

        var editDto = new PurchaseInvoiceEditDto
        {
            DocumentId = documentId,
            DocumentDate = invoiceToEdit.DocumentDate,
            DueDate = invoiceToEdit.DueDate,
            DocumentNumber = invoiceToEdit.DocumentNumber,
            DocumentObservations = invoiceToEdit.Observations,
            PartnerId = invoiceToEdit.PartnerId,
            InvoiceObservations = invoiceToEdit.InvoiceObservations,
            PartnerAddressSediuId = invoiceToEdit.PartnerAddressSediuId,
            PartnerAddressCorespondentaId = invoiceToEdit.PartnerAddressCorespondentaId,
            PartnerAddressLivrareId = invoiceToEdit.PartnerAddressLivrareId,
            PartnerAddressFacturareId = invoiceToEdit.PartnerAddressFacturareId,
            PartnerContactId = invoiceToEdit.PartnerContactId,
            PartnerBankAccountId = invoiceToEdit.PartnerBankAccountId,
            OwnerCompanyId = invoiceToEdit.OwnerCompanyId,
            OwnerWorkPlaceId = invoiceToEdit.OwnerWorkPlaceId,
            OwnerLocationId = invoiceToEdit.OwnerLocationId,
            LineItems = new List<InvoiceLineItemDto>()
        };

        // Load line items from invoice details
        if (invoiceDetails?.Details != null)
        {
            foreach (var detail in invoiceDetails.Details)
            {
                editDto.LineItems.Add(new InvoiceLineItemDto
                {
                    ItemId = detail.ItemId,
                    Quantity = detail.Quantity,
                    UnitMeasure = detail.UnitMeasure,
                    UnitPrice = detail.UnitPrice,
                    VATRate = detail.VATRate
                });
            }
        }

        invoiceDto = editDto;

        // Load partner data
        selectedPartner = Partners.FirstOrDefault(p => p.Id == invoiceToEdit.PartnerId);
        if (selectedPartner != null)
        {
            await LoadPartnerData(selectedPartner.Id);
        }
    }

    #endregion

    #region Partner Management

    private async Task OnPartnerChanged(ChangeEventArgs<Partner, Partner> args)
    {
        if (args.Value == null) return;

        selectedPartner = args.Value;

        // Update DTO
        if (invoiceDto is PurchaseInvoiceCreateDto createDto)
        {
            createDto.PartnerId = args.Value.Id;
        }
        else if (invoiceDto is PurchaseInvoiceEditDto editDto)
        {
            editDto.PartnerId = args.Value.Id;
        }

        await LoadPartnerData(args.Value.Id);
        StateHasChanged();
    }

    private async Task LoadPartnerData(Guid partnerId)
    {
        try
        {
            // Load addresses
            var addresses = await ParteneriRepository.GetPartnerAddressesAsync(partnerId);
            partnerAddressesByType = addresses
                .Where(a => a.IsActive)
                .GroupBy(a => a.TipAdresa)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Auto-select single addresses
            selectedAddressByType.Clear();
            foreach (var kv in partnerAddressesByType)
            {
                if (kv.Value.Count == 1)
                {
                    selectedAddressByType[kv.Key] = kv.Value.First().Id;
                    SetAddressInDto(kv.Key, kv.Value.First().Id);
                }
            }

            // Load contacts
            partnerContacts = (await ParteneriRepository.GetPartnerContactsAsync(partnerId))
                .Where(c => c.IsActive)
                .ToList();

            if (partnerContacts.Count == 1)
            {
                selectedPartnerContactId = partnerContacts.First().Id;
                SetContactInDto(selectedPartnerContactId);
            }

            // Load bank accounts
            partnerBankAccounts = (await ParteneriRepository.GetPartnerBankAccountsAsync(partnerId))
                .Where(b => b.IsActive)
                .ToList();

            if (partnerBankAccounts.Count == 1)
            {
                selectedPartnerBankAccountId = partnerBankAccounts.First().Id;
                SetBankAccountInDto(selectedPartnerBankAccountId);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading partner data for {PartnerId}", partnerId);
        }
    }

    private void OnAddressSelected(TipAdresa tipAdresa, ChangeEventArgs<Guid?, PartnerAddress> args)
    {
        if (args.Value.HasValue)
        {
            selectedAddressByType[tipAdresa] = args.Value.Value;
            SetAddressInDto(tipAdresa, args.Value.Value);
        }
    }

    private void SetAddressInDto(TipAdresa tipAdresa, Guid addressId)
    {
        if (invoiceDto is PurchaseInvoiceCreateDto createDto)
        {
            switch (tipAdresa)
            {
                case TipAdresa.Sediu:
                    createDto.PartnerAddressSediuId = addressId;
                    break;
                case TipAdresa.Corespondenta:
                    createDto.PartnerAddressCorespondentaId = addressId;
                    break;
                case TipAdresa.Livrare:
                    createDto.PartnerAddressLivrareId = addressId;
                    break;
                case TipAdresa.Facturare:
                    createDto.PartnerAddressFacturareId = addressId;
                    break;
            }
        }
        else if (invoiceDto is PurchaseInvoiceEditDto editDto)
        {
            switch (tipAdresa)
            {
                case TipAdresa.Sediu:
                    editDto.PartnerAddressSediuId = addressId;
                    break;
                case TipAdresa.Corespondenta:
                    editDto.PartnerAddressCorespondentaId = addressId;
                    break;
                case TipAdresa.Livrare:
                    editDto.PartnerAddressLivrareId = addressId;
                    break;
                case TipAdresa.Facturare:
                    editDto.PartnerAddressFacturareId = addressId;
                    break;
            }
        }
    }

    private void OnContactSelected(ChangeEventArgs<Guid?, PartnerContact> args)
    {
        if (args.Value.HasValue)
        {
            selectedPartnerContactId = args.Value.Value;
            SetContactInDto(args.Value);
        }
    }

    private void SetContactInDto(Guid? contactId)
    {
        if (invoiceDto is PurchaseInvoiceCreateDto createDto)
        {
            createDto.PartnerContactId = contactId;
        }
        else if (invoiceDto is PurchaseInvoiceEditDto editDto)
        {
            editDto.PartnerContactId = contactId;
        }
    }

    private void OnBankAccountSelected(ChangeEventArgs<Guid?, PartnerBankAccount> args)
    {
        if (args.Value.HasValue)
        {
            selectedPartnerBankAccountId = args.Value.Value;
            SetBankAccountInDto(args.Value);
        }
    }

    private void SetBankAccountInDto(Guid? bankAccountId)
    {
        if (invoiceDto is PurchaseInvoiceCreateDto createDto)
        {
            createDto.PartnerBankAccountId = bankAccountId;
        }
        else if (invoiceDto is PurchaseInvoiceEditDto editDto)
        {
            editDto.PartnerBankAccountId = bankAccountId;
        }
    }

    #endregion

    #region Line Items Management

    private void AddLineItem()
    {
        var newItem = new InvoiceLineItemDto
        {
            ItemId = Guid.Empty,
            Quantity = 1,
            UnitMeasure = "buc",
            UnitPrice = 0,
            VATRate = 19
        };

        if (invoiceDto is PurchaseInvoiceCreateDto createDto)
        {
            createDto.LineItems.Add(newItem);
        }
        else if (invoiceDto is PurchaseInvoiceEditDto editDto)
        {
            editDto.LineItems.Add(newItem);
        }

        StateHasChanged();
    }

    private void RemoveLineItem(int index)
    {
        if (invoiceDto is PurchaseInvoiceCreateDto createDto)
        {
            if (index >= 0 && index < createDto.LineItems.Count)
            {
                createDto.LineItems.RemoveAt(index);
            }
        }
        else if (invoiceDto is PurchaseInvoiceEditDto editDto)
        {
            if (index >= 0 && index < editDto.LineItems.Count)
            {
                editDto.LineItems.RemoveAt(index);
            }
        }

        StateHasChanged();
    }

    private decimal CalculateLineTotal(InvoiceLineItemDto item)
    {
        var subtotal = item.Quantity * item.UnitPrice;
        var vatAmount = subtotal * (item.VATRate / 100);
        return subtotal + vatAmount;
    }

    #endregion

    #region Save/Cancel

    private async Task HandleValidSubmit()
    {
        try
        {
            // Update document state code before saving
            if (selectedDocumentState != null)
            {
                if (invoiceDto is PurchaseInvoiceCreateDto createDto)
                {
                    createDto.DocumentStateCode = selectedDocumentState.CodStare;
                }
            }

            bool success;

            if (IsEditMode && invoiceDto is PurchaseInvoiceEditDto editDto)
            {
                success = await AchizitiiService.UpdateInvoiceAsync(editDto, UserId);
            }
            else if (!IsEditMode && invoiceDto is PurchaseInvoiceCreateDto createDto)
            {
                var result = await AchizitiiService.CreatePurchaseInvoiceAsync(createDto, UserId);
                success = result.Success;
            }
            else
            {
                success = false;
            }

            await CloseDialog();
            await OnSaveCompleted.InvokeAsync(success);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error saving invoice");
            await OnSaveCompleted.InvokeAsync(false);
        }
    }

    private async Task HandleCancel()
    {
        await CloseDialog();
    }

    private async Task CloseDialog()
    {
        IsVisible = false;
        await IsVisibleChanged.InvokeAsync(false);

        // Reset state
        invoiceDto = null;
        selectedPartner = null;
        partnerAddressesByType.Clear();
        selectedAddressByType.Clear();
        partnerContacts.Clear();
        partnerBankAccounts.Clear();
        selectedPartnerContactId = null;
        selectedPartnerBankAccountId = null;
        selectedDocumentState = null;
    }

    #endregion
}
