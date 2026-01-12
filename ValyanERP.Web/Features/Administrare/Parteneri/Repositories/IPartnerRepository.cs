// ========================================================================
// IPartnerRepository.cs - Interface pentru repository Partners
// Vertical Slices Architecture - Features/Administrare/Parteneri
// ========================================================================

using ValyanERP.Web.Features.Administrare.Parteneri.Models;
using ValyanERP.Web.Features.Administrare.Parteneri.Models.DTOs;

namespace ValyanERP.Web.Features.Administrare.Parteneri.Repositories;

/// <summary>
/// Repository interface pentru operații CRUD pe entitatea Partner.
/// Utilizează stored procedures pentru toate operațiile de acces la date.
/// </summary>
public interface IPartnerRepository
{
    #region Partner CRUD Operations

    /// <summary>
    /// Obține toți partenerii activi cu paginare server-side.
    /// </summary>
    /// <param name="skip">Numărul de înregistrări de sărit</param>
    /// <param name="take">Numărul de înregistrări de returnat</param>
    /// <returns>Lista de parteneri și total count</returns>
    Task<(IEnumerable<PartnerListDto> Partners, int TotalCount)> GetAllAsync(int skip = 0, int take = 50);

    /// <summary>
    /// Obține un partener după ID cu toate relațiile.
    /// </summary>
    /// <param name="id">ID-ul partenerului</param>
    /// <returns>Partenerul cu toate sub-entitățile sau null</returns>
    Task<Partner?> GetByIdAsync(Guid id);

    /// <summary>
    /// Obține un partener după CUI (pentru persoane juridice).
    /// </summary>
    /// <param name="cui">Codul Unic de Înregistrare</param>
    /// <returns>Partenerul sau null</returns>
    Task<Partner?> GetByCuiAsync(string cui);

    /// <summary>
    /// Obține un partener după CNP (pentru persoane fizice).
    /// </summary>
    /// <param name="cnp">Codul Numeric Personal</param>
    /// <returns>Partenerul sau null</returns>
    Task<Partner?> GetByCnpAsync(string cnp);

    /// <summary>
    /// Caută parteneri după termen (denumire, CUI, CNP, email).
    /// </summary>
    /// <param name="searchTerm">Termen de căutare</param>
    /// <param name="skip">Numărul de înregistrări de sărit</param>
    /// <param name="take">Numărul de înregistrări de returnat</param>
    /// <returns>Lista de parteneri și total count</returns>
    Task<(IEnumerable<PartnerListDto> Partners, int TotalCount)> SearchAsync(string searchTerm, int skip = 0, int take = 50);

    /// <summary>
    /// Creează un partener nou și returnează ID-ul generat.
    /// </summary>
    /// <param name="dto">Datele partenerului</param>
    /// <param name="createdBy">ID-ul utilizatorului care creează</param>
    /// <returns>ID-ul partenerului creat</returns>
    Task<Guid> CreateAsync(CreatePartnerDto dto, Guid createdBy);

    /// <summary>
    /// Actualizează un partener existent.
    /// </summary>
    /// <param name="dto">Datele actualizate</param>
    /// <param name="updatedBy">ID-ul utilizatorului care actualizează</param>
    /// <returns>True dacă actualizarea a reușit</returns>
    Task<bool> UpdateAsync(UpdatePartnerDto dto, Guid updatedBy);

    /// <summary>
    /// Șterge (soft delete) un partener.
    /// </summary>
    /// <param name="id">ID-ul partenerului</param>
    /// <param name="deletedBy">ID-ul utilizatorului care șterge</param>
    /// <returns>True dacă ștergerea a reușit</returns>
    Task<bool> DeleteAsync(Guid id, Guid deletedBy);

    /// <summary>
    /// Actualizează statusul verificării ANAF pentru un partener.
    /// </summary>
    /// <param name="id">ID-ul partenerului</param>
    /// <param name="esteInactiv">Status inactiv fiscal</param>
    /// <param name="esteRadiat">Status radiat din Registrul Comerțului</param>
    /// <param name="estePlatitorTva">Status plătitor TVA</param>
    /// <param name="esteInsolvent">Status insolvent</param>
    /// <param name="dataVerificareAnaf">Data verificării</param>
    /// <returns>True dacă actualizarea a reușit</returns>
    Task<bool> UpdateAnafStatusAsync(Guid id, bool? esteInactiv, bool? esteRadiat, 
        bool? estePlatitorTva, bool? esteInsolvent, DateTime dataVerificareAnaf);

    /// <summary>
    /// Setează adresa principală pentru un partener.
    /// </summary>
    /// <param name="partnerId">ID-ul partenerului</param>
    /// <param name="addressId">ID-ul adresei</param>
    /// <returns>True dacă operația a reușit</returns>
    Task<bool> SetPrincipalAddressAsync(Guid partnerId, Guid addressId);

    /// <summary>
    /// Generează un nou cod de partener.
    /// </summary>
    /// <returns>Codul generat (ex: P-000001)</returns>
    Task<string> GenerateCodeAsync();

    /// <summary>
    /// Verifică dacă un CUI există deja în baza de date.
    /// </summary>
    /// <param name="cui">CUI de verificat</param>
    /// <param name="excludeId">ID partener de exclus (pentru update)</param>
    /// <returns>True dacă există</returns>
    Task<bool> ExistsByCuiAsync(string cui, Guid? excludeId = null);

    /// <summary>
    /// Verifică dacă un CNP există deja în baza de date.
    /// </summary>
    /// <param name="cnp">CNP de verificat</param>
    /// <param name="excludeId">ID partener de exclus (pentru update)</param>
    /// <returns>True dacă există</returns>
    Task<bool> ExistsByCnpAsync(string cnp, Guid? excludeId = null);

    #endregion

    #region Partner Addresses

    /// <summary>
    /// Obține toate adresele unui partener.
    /// </summary>
    Task<IEnumerable<PartnerAddress>> GetAddressesByPartnerIdAsync(Guid partnerId);

    /// <summary>
    /// Creează o adresă nouă pentru un partener.
    /// </summary>
    Task<Guid> CreateAddressAsync(Guid partnerId, PartnerAddress address, Guid createdBy);

    /// <summary>
    /// Actualizează o adresă existentă.
    /// </summary>
    Task<bool> UpdateAddressAsync(PartnerAddress address, Guid updatedBy);

    /// <summary>
    /// Șterge o adresă.
    /// </summary>
    Task<bool> DeleteAddressAsync(Guid addressId, Guid deletedBy);

    #endregion

    #region Partner Contacts

    /// <summary>
    /// Obține toate contactele unui partener.
    /// </summary>
    Task<IEnumerable<PartnerContact>> GetContactsByPartnerIdAsync(Guid partnerId);

    /// <summary>
    /// Creează un contact nou pentru un partener.
    /// </summary>
    Task<Guid> CreateContactAsync(Guid partnerId, PartnerContact contact, Guid createdBy);

    /// <summary>
    /// Actualizează un contact existent.
    /// </summary>
    Task<bool> UpdateContactAsync(PartnerContact contact, Guid updatedBy);

    /// <summary>
    /// Șterge un contact.
    /// </summary>
    Task<bool> DeleteContactAsync(Guid contactId, Guid deletedBy);

    #endregion

    #region Partner Bank Accounts

    /// <summary>
    /// Obține toate conturile bancare ale unui partener.
    /// </summary>
    Task<IEnumerable<PartnerBankAccount>> GetBankAccountsByPartnerIdAsync(Guid partnerId);

    /// <summary>
    /// Creează un cont bancar nou pentru un partener.
    /// </summary>
    Task<Guid> CreateBankAccountAsync(Guid partnerId, PartnerBankAccount bankAccount, Guid createdBy);

    /// <summary>
    /// Actualizează un cont bancar existent.
    /// </summary>
    Task<bool> UpdateBankAccountAsync(PartnerBankAccount bankAccount, Guid updatedBy);

    /// <summary>
    /// Șterge un cont bancar.
    /// </summary>
    Task<bool> DeleteBankAccountAsync(Guid bankAccountId, Guid deletedBy);

    #endregion

    #region Partner Representatives

    /// <summary>
    /// Obține toți reprezentanții unui partener.
    /// </summary>
    Task<IEnumerable<PartnerRepresentative>> GetRepresentativesByPartnerIdAsync(Guid partnerId);

    /// <summary>
    /// Creează un reprezentant nou pentru un partener.
    /// </summary>
    Task<Guid> CreateRepresentativeAsync(Guid partnerId, PartnerRepresentative representative, Guid createdBy);

    /// <summary>
    /// Actualizează un reprezentant existent.
    /// </summary>
    Task<bool> UpdateRepresentativeAsync(PartnerRepresentative representative, Guid updatedBy);

    /// <summary>
    /// Șterge un reprezentant.
    /// </summary>
    Task<bool> DeleteRepresentativeAsync(Guid representativeId, Guid deletedBy);

    #endregion

    #region ANAF Verification Cache

    /// <summary>
    /// Obține verificarea ANAF din cache pentru un CUI.
    /// </summary>
    /// <param name="cui">CUI de verificat</param>
    /// <returns>Datele din cache sau null dacă expirate/inexistente</returns>
    Task<AnafVerificationCache?> GetAnafCacheAsync(string cui);

    /// <summary>
    /// Salvează rezultatul verificării ANAF în cache.
    /// </summary>
    Task<Guid> SaveAnafCacheAsync(AnafVerificationCache cache);

    /// <summary>
    /// Curăță înregistrările expirate din cache.
    /// </summary>
    /// <returns>Numărul de înregistrări șterse</returns>
    Task<int> CleanupExpiredAnafCacheAsync();

    #endregion
}
