using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using ValyanERP.Web.Features.Administrare.Parteneri.Models;
using ValyanERP.Web.Features.Administrare.Parteneri.Models.DTOs;
using ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums;
using ValyanERP.Web.Features.Administrare.Parteneri.Repositories;

namespace ValyanERP.Web.Components.Pages.Administrare;

/// <summary>
/// Code-behind pentru pagina Parteneri.
/// </summary>
public partial class Parteneri : ComponentBase
{
    // ==================== INJECTED SERVICES ====================
    
    [Inject]
    private IPartnerRepository PartnerRepository { get; set; } = default!;
    
    [Inject]
    private AuthenticationStateProvider AuthStateProvider { get; set; } = default!;
    
    [Inject]
    private ILogger<Parteneri> Logger { get; set; } = default!;

    // ==================== LIST STATE ====================
    
    private List<PartnerListDto> partnersList = new();
    private int totalCount;
    private int currentPage = 1;
    private int pageSize = 20;
    private string searchTerm = string.Empty;
    private string selectedCategoryFilter = "ALL";
    private bool isLoading = true;
    
    // ==================== SELECTION STATE ====================
    
    private PartnerListDto? selectedPartner;
    private Partner? selectedPartnerDetails;
    private bool isLoadingDetails;
    
    // ==================== MESSAGES ====================
    
    private string? errorMessage;
    private string? successMessage;
    
    // ==================== DIALOG STATE - PARTNER ====================
    
    private bool partnerDialogVisible;
    private CreatePartnerDto partnerDialogModel = new();
    private bool isNewPartner;
    
    // ==================== DIALOG STATE - ADDRESS ====================
    
    private bool addressDialogVisible;
    private PartnerAddress? editingAddress;
    
    // ==================== DIALOG STATE - CONTACT ====================
    
    private bool contactDialogVisible;
    private PartnerContact? editingContact;
    
    // ==================== DIALOG STATE - BANK ACCOUNT ====================
    
    private bool bankAccountDialogVisible;
    private PartnerBankAccount? editingBankAccount;
    
    // ==================== DIALOG STATE - REPRESENTATIVE ====================
    
    private bool representativeDialogVisible;
    private PartnerRepresentative? editingRepresentative;
    
    // ==================== CONFIRM DIALOG STATE ====================
    
    private bool confirmDialogVisible;
    private string confirmDialogTitle = string.Empty;
    private string confirmDialogMessage = string.Empty;
    private Func<Task>? pendingConfirmAction;
    
    // ==================== CURRENT USER ====================
    
    private Guid currentUserId;

    // ==================== LIFECYCLE ====================

    protected override async Task OnInitializedAsync()
    {
        await LoadCurrentUserAsync();
        await LoadPartnersAsync();
    }

    private async Task LoadCurrentUserAsync()
    {
        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        var userIdClaim = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(userIdClaim, out var userId))
        {
            currentUserId = userId;
        }
    }

    // ==================== DATA LOADING ====================

    private async Task LoadPartnersAsync()
    {
        isLoading = true;
        StateHasChanged();
        
        try
        {
            var skip = (currentPage - 1) * pageSize;
            
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                var result = await PartnerRepository.GetAllAsync(skip, pageSize);
                partnersList = result.Partners.ToList();
                totalCount = result.TotalCount;
            }
            else
            {
                var result = await PartnerRepository.SearchAsync(searchTerm, skip, pageSize);
                partnersList = result.Partners.ToList();
                totalCount = result.TotalCount;
            }
            
            // Apply local category filter
            ApplyCategoryFilter();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Eroare la încărcarea partenerilor");
            errorMessage = "Eroare la încărcarea listei de parteneri.";
        }
        finally
        {
            isLoading = false;
            StateHasChanged();
        }
    }

    private void ApplyCategoryFilter()
    {
        if (selectedCategoryFilter == "ALL") return;
        
        partnersList = selectedCategoryFilter switch
        {
            "FURNIZOR" => partnersList.Where(p => p.EsteFurnizor).ToList(),
            "CLIENT" => partnersList.Where(p => p.EsteClient).ToList(),
            "PJ" => partnersList.Where(p => p.Categoria != CategoriePartener.PF).ToList(),
            "PF" => partnersList.Where(p => p.Categoria == CategoriePartener.PF).ToList(),
            _ => partnersList
        };
    }

    private async Task LoadPartnerDetailsAsync(Guid partnerId)
    {
        isLoadingDetails = true;
        StateHasChanged();
        
        try
        {
            selectedPartnerDetails = await PartnerRepository.GetByIdAsync(partnerId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Eroare la încărcarea detaliilor partenerului {PartnerId}", partnerId);
            errorMessage = "Eroare la încărcarea detaliilor partenerului.";
        }
        finally
        {
            isLoadingDetails = false;
            StateHasChanged();
        }
    }

    private async Task RefreshList()
    {
        selectedPartner = null;
        selectedPartnerDetails = null;
        currentPage = 1;
        await LoadPartnersAsync();
    }

    // ==================== SEARCH & FILTER ====================

    private System.Timers.Timer? searchDebounceTimer;

    private void OnSearchChanged(ChangeEventArgs e)
    {
        searchDebounceTimer?.Stop();
        searchDebounceTimer?.Dispose();
        
        searchDebounceTimer = new System.Timers.Timer(300);
        searchDebounceTimer.Elapsed += async (s, args) =>
        {
            searchDebounceTimer?.Stop();
            await InvokeAsync(async () =>
            {
                currentPage = 1;
                await LoadPartnersAsync();
            });
        };
        searchDebounceTimer.Start();
    }

    private async Task OnSearchValueChanged(Syncfusion.Blazor.Inputs.ChangedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.Value))
        {
            currentPage = 1;
            await LoadPartnersAsync();
        }
    }

    private async Task SetCategoryFilter(string category)
    {
        selectedCategoryFilter = category;
        currentPage = 1;
        await LoadPartnersAsync();
    }

    // ==================== PAGINATION ====================

    private async Task PreviousPage()
    {
        if (currentPage > 1)
        {
            currentPage--;
            await LoadPartnersAsync();
        }
    }

    private async Task NextPage()
    {
        if (currentPage * pageSize < totalCount)
        {
            currentPage++;
            await LoadPartnersAsync();
        }
    }

    // ==================== SELECTION ====================

    private async Task SelectPartner(PartnerListDto partner)
    {
        selectedPartner = partner;
        await LoadPartnerDetailsAsync(partner.Id);
    }

    // ==================== PARTNER CRUD ====================

    private void ShowAddPartnerDialog()
    {
        partnerDialogModel = new CreatePartnerDto
        {
            Categoria = CategoriePartener.SC,
            TipEntitate = "SRL",
            RolPartener = RolPartener.Client
        };
        isNewPartner = true;
        partnerDialogVisible = true;
    }

    private void EditPartner()
    {
        if (selectedPartnerDetails == null) return;
        
        // Convert Partner to CreatePartnerDto for editing
        partnerDialogModel = new CreatePartnerDto
        {
            Categoria = selectedPartnerDetails.Categoria,
            TipEntitate = selectedPartnerDetails.TipEntitate,
            RolPartener = selectedPartnerDetails.RolPartener,
            Denumire = selectedPartnerDetails.Denumire,
            DenumireScurta = selectedPartnerDetails.DenumireScurta,
            Nume = selectedPartnerDetails.Nume,
            Prenume = selectedPartnerDetails.Prenume,
            CNP = selectedPartnerDetails.CNP,
            CUI = selectedPartnerDetails.CUI,
            CIF = selectedPartnerDetails.CIF,
            VATID = selectedPartnerDetails.VATID,
            RegCom = selectedPartnerDetails.RegCom,
            Email = selectedPartnerDetails.Email,
            Telefon = selectedPartnerDetails.Telefon,
            Website = selectedPartnerDetails.Website,
            EstePlatitorTVA = selectedPartnerDetails.EstePlatitorTVA,
            LimitaCredit = selectedPartnerDetails.LimitaCredit,
            TermenPlataDef = selectedPartnerDetails.TermenPlataDef
        };
        isNewPartner = false;
        partnerDialogVisible = true;
    }

    private void DeletePartner()
    {
        if (selectedPartnerDetails == null) return;
        
        confirmDialogTitle = "Ștergere Partener";
        confirmDialogMessage = $"Sigur doriți să ștergeți partenerul '{selectedPartnerDetails.DenumireAfisare}'?";
        pendingConfirmAction = async () =>
        {
            try
            {
                await PartnerRepository.DeleteAsync(selectedPartnerDetails.Id, currentUserId);
                successMessage = "Partenerul a fost șters cu succes.";
                selectedPartner = null;
                selectedPartnerDetails = null;
                await LoadPartnersAsync();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Eroare la ștergerea partenerului");
                errorMessage = "Eroare la ștergerea partenerului.";
            }
        };
        confirmDialogVisible = true;
    }

    private async Task OnPartnerSaved(CreatePartnerDto dto)
    {
        try
        {
            if (isNewPartner)
            {
                await PartnerRepository.CreateAsync(dto, currentUserId);
                successMessage = "Partenerul a fost creat cu succes.";
            }
            else if (selectedPartnerDetails != null)
            {
                var updateDto = new UpdatePartnerDto
                {
                    Id = selectedPartnerDetails.Id,
                    Categoria = dto.Categoria,
                    TipEntitate = dto.TipEntitate,
                    RolPartener = dto.RolPartener,
                    Denumire = dto.Denumire,
                    DenumireScurta = dto.DenumireScurta,
                    Nume = dto.Nume,
                    Prenume = dto.Prenume,
                    CNP = dto.CNP,
                    CUI = dto.CUI,
                    CIF = dto.CIF,
                    VATID = dto.VATID,
                    RegCom = dto.RegCom,
                    Email = dto.Email,
                    Telefon = dto.Telefon,
                    Website = dto.Website,
                    EstePlatitorTVA = dto.EstePlatitorTVA,
                    LimitaCredit = dto.LimitaCredit,
                    TermenPlataDef = dto.TermenPlataDef
                };
                await PartnerRepository.UpdateAsync(updateDto, currentUserId);
                successMessage = "Partenerul a fost actualizat cu succes.";
                await LoadPartnerDetailsAsync(selectedPartnerDetails.Id);
            }
            
            partnerDialogVisible = false;
            await LoadPartnersAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Eroare la salvarea partenerului");
            errorMessage = "Eroare la salvarea partenerului.";
        }
    }

    // ==================== ANAF VERIFICATION ====================

    private async Task VerifyAnaf()
    {
        if (selectedPartnerDetails == null || string.IsNullOrEmpty(selectedPartnerDetails.CUI))
        {
            errorMessage = "Partenerul nu are CUI pentru verificare ANAF.";
            return;
        }
        
        // TODO: Implementare serviciu ANAF în FAZA 4
        successMessage = "Verificarea ANAF va fi implementată în faza următoare.";
    }

    // ==================== ADDRESS CRUD ====================

    private void ShowAddAddressDialog()
    {
        editingAddress = new PartnerAddress
        {
            PartnerId = selectedPartnerDetails?.Id ?? Guid.Empty,
            TipAdresa = TipAdresa.Sediu,
            Tara = "România",
            CodTaraISO = "RO"
        };
        addressDialogVisible = true;
    }

    private void ShowEditAddressDialog(PartnerAddress address)
    {
        editingAddress = address;
        addressDialogVisible = true;
    }

    private void DeleteAddress(PartnerAddress address)
    {
        confirmDialogTitle = "Ștergere Adresă";
        confirmDialogMessage = $"Sigur doriți să ștergeți adresa '{address.AdresaCompleta}'?";
        pendingConfirmAction = async () =>
        {
            try
            {
                await PartnerRepository.DeleteAddressAsync(address.Id, currentUserId);
                successMessage = "Adresa a fost ștearsă cu succes.";
                await LoadPartnerDetailsAsync(selectedPartnerDetails!.Id);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Eroare la ștergerea adresei");
                errorMessage = "Eroare la ștergerea adresei.";
            }
        };
        confirmDialogVisible = true;
    }

    private async Task OnAddressSaved(PartnerAddress address)
    {
        try
        {
            if (address.Id == Guid.Empty)
            {
                address.PartnerId = selectedPartnerDetails!.Id;
                await PartnerRepository.CreateAddressAsync(address.PartnerId, address, currentUserId);
            }
            else
            {
                await PartnerRepository.UpdateAddressAsync(address, currentUserId);
            }
            addressDialogVisible = false;
            successMessage = "Adresa a fost salvată cu succes.";
            await LoadPartnerDetailsAsync(selectedPartnerDetails!.Id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Eroare la salvarea adresei");
            errorMessage = "Eroare la salvarea adresei.";
        }
    }

    // ==================== CONTACT CRUD ====================

    private void ShowAddContactDialog()
    {
        editingContact = new PartnerContact
        {
            PartnerId = selectedPartnerDetails?.Id ?? Guid.Empty
        };
        contactDialogVisible = true;
    }

    private void ShowEditContactDialog(PartnerContact contact)
    {
        editingContact = contact;
        contactDialogVisible = true;
    }

    private void DeleteContact(PartnerContact contact)
    {
        confirmDialogTitle = "Ștergere Contact";
        confirmDialogMessage = $"Sigur doriți să ștergeți contactul '{contact.NumeComplet}'?";
        pendingConfirmAction = async () =>
        {
            try
            {
                await PartnerRepository.DeleteContactAsync(contact.Id, currentUserId);
                successMessage = "Contactul a fost șters cu succes.";
                await LoadPartnerDetailsAsync(selectedPartnerDetails!.Id);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Eroare la ștergerea contactului");
                errorMessage = "Eroare la ștergerea contactului.";
            }
        };
        confirmDialogVisible = true;
    }

    private async Task OnContactSaved(PartnerContact contact)
    {
        try
        {
            if (contact.Id == Guid.Empty)
            {
                contact.PartnerId = selectedPartnerDetails!.Id;
                await PartnerRepository.CreateContactAsync(contact.PartnerId, contact, currentUserId);
            }
            else
            {
                await PartnerRepository.UpdateContactAsync(contact, currentUserId);
            }
            contactDialogVisible = false;
            successMessage = "Contactul a fost salvat cu succes.";
            await LoadPartnerDetailsAsync(selectedPartnerDetails!.Id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Eroare la salvarea contactului");
            errorMessage = "Eroare la salvarea contactului.";
        }
    }

    // ==================== BANK ACCOUNT CRUD ====================

    private void ShowAddBankAccountDialog()
    {
        editingBankAccount = new PartnerBankAccount
        {
            PartnerId = selectedPartnerDetails?.Id ?? Guid.Empty,
            Moneda = "RON"
        };
        bankAccountDialogVisible = true;
    }

    private void ShowEditBankAccountDialog(PartnerBankAccount bankAccount)
    {
        editingBankAccount = bankAccount;
        bankAccountDialogVisible = true;
    }

    private void DeleteBankAccount(PartnerBankAccount bankAccount)
    {
        confirmDialogTitle = "Ștergere Cont Bancar";
        confirmDialogMessage = $"Sigur doriți să ștergeți contul '{bankAccount.IBANFormatat}'?";
        pendingConfirmAction = async () =>
        {
            try
            {
                await PartnerRepository.DeleteBankAccountAsync(bankAccount.Id, currentUserId);
                successMessage = "Contul bancar a fost șters cu succes.";
                await LoadPartnerDetailsAsync(selectedPartnerDetails!.Id);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Eroare la ștergerea contului bancar");
                errorMessage = "Eroare la ștergerea contului bancar.";
            }
        };
        confirmDialogVisible = true;
    }

    private async Task OnBankAccountSaved(PartnerBankAccount bankAccount)
    {
        try
        {
            if (bankAccount.Id == Guid.Empty)
            {
                bankAccount.PartnerId = selectedPartnerDetails!.Id;
                await PartnerRepository.CreateBankAccountAsync(bankAccount.PartnerId, bankAccount, currentUserId);
            }
            else
            {
                await PartnerRepository.UpdateBankAccountAsync(bankAccount, currentUserId);
            }
            bankAccountDialogVisible = false;
            successMessage = "Contul bancar a fost salvat cu succes.";
            await LoadPartnerDetailsAsync(selectedPartnerDetails!.Id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Eroare la salvarea contului bancar");
            errorMessage = "Eroare la salvarea contului bancar.";
        }
    }

    // ==================== REPRESENTATIVE CRUD ====================

    private void ShowAddRepresentativeDialog()
    {
        editingRepresentative = new PartnerRepresentative
        {
            PartnerId = selectedPartnerDetails?.Id ?? Guid.Empty,
            TipReprezentant = TipReprezentant.Administrator
        };
        representativeDialogVisible = true;
    }

    private void ShowEditRepresentativeDialog(PartnerRepresentative representative)
    {
        editingRepresentative = representative;
        representativeDialogVisible = true;
    }

    private void DeleteRepresentative(PartnerRepresentative representative)
    {
        confirmDialogTitle = "Ștergere Reprezentant";
        confirmDialogMessage = $"Sigur doriți să ștergeți reprezentantul '{representative.NumeComplet}'?";
        pendingConfirmAction = async () =>
        {
            try
            {
                await PartnerRepository.DeleteRepresentativeAsync(representative.Id, currentUserId);
                successMessage = "Reprezentantul a fost șters cu succes.";
                await LoadPartnerDetailsAsync(selectedPartnerDetails!.Id);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Eroare la ștergerea reprezentantului");
                errorMessage = "Eroare la ștergerea reprezentantului.";
            }
        };
        confirmDialogVisible = true;
    }

    private async Task OnRepresentativeSaved(PartnerRepresentative representative)
    {
        try
        {
            if (representative.Id == Guid.Empty)
            {
                representative.PartnerId = selectedPartnerDetails!.Id;
                await PartnerRepository.CreateRepresentativeAsync(representative.PartnerId, representative, currentUserId);
            }
            else
            {
                await PartnerRepository.UpdateRepresentativeAsync(representative, currentUserId);
            }
            representativeDialogVisible = false;
            successMessage = "Reprezentantul a fost salvat cu succes.";
            await LoadPartnerDetailsAsync(selectedPartnerDetails!.Id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Eroare la salvarea reprezentantului");
            errorMessage = "Eroare la salvarea reprezentantului.";
        }
    }

    // ==================== CONFIRM DIALOG ====================

    private async Task OnConfirmDialogConfirm()
    {
        confirmDialogVisible = false;
        if (pendingConfirmAction != null)
        {
            await pendingConfirmAction();
            pendingConfirmAction = null;
        }
    }

    // ==================== HELPERS ====================

    private void ClearError() => errorMessage = null;
    private void ClearSuccess() => successMessage = null;

    private static string GetPartnerIcon(PartnerListDto partner)
    {
        return partner.Categoria switch
        {
            CategoriePartener.PF => "bi-person",
            CategoriePartener.PFA => "bi-person-badge",
            CategoriePartener.SC => "bi-building",
            CategoriePartener.NP => "bi-heart",
            CategoriePartener.IP => "bi-bank",
            CategoriePartener.DIP => "bi-globe",
            CategoriePartener.OI => "bi-globe2",
            CategoriePartener.STR => "bi-airplane",
            CategoriePartener.SP => "bi-star",
            _ => "bi-person-lines-fill"
        };
    }
}
