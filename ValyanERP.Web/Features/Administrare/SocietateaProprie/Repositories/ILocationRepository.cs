using ValyanERP.Web.Features.Administrare.SocietateaProprie.Models;

namespace ValyanERP.Web.Features.Administrare.SocietateaProprie.Repositories;

/// <summary>
/// Repository pentru operații CRUD pe locații.
/// </summary>
public interface ILocationRepository
{
    /// <summary>
    /// Obține toate locațiile.
    /// </summary>
    /// <param name="includeInactive">Dacă se includ și locațiile inactive.</param>
    /// <param name="companyId">Filtrare opțională pe companie.</param>
    /// <param name="workPlaceId">Filtrare opțională pe punct de lucru.</param>
    /// <returns>Lista de locații.</returns>
    Task<IEnumerable<Location>> GetAllAsync(bool includeInactive = false, Guid? companyId = null, Guid? workPlaceId = null);

    /// <summary>
    /// Obține o locație după ID.
    /// </summary>
    /// <param name="id">ID-ul locației.</param>
    /// <returns>Locația găsită sau null.</returns>
    Task<Location?> GetByIdAsync(Guid id);

    /// <summary>
    /// Creează o locație nouă.
    /// </summary>
    /// <param name="location">Datele locației.</param>
    /// <param name="createdBy">ID-ul utilizatorului care creează.</param>
    /// <returns>ID-ul locației create.</returns>
    Task<Guid> CreateAsync(Location location, Guid? createdBy = null);

    /// <summary>
    /// Actualizează o locație existentă.
    /// </summary>
    /// <param name="location">Datele actualizate.</param>
    /// <param name="updatedBy">ID-ul utilizatorului care modifică.</param>
    /// <returns>True dacă actualizarea a reușit.</returns>
    Task<bool> UpdateAsync(Location location, Guid? updatedBy = null);

    /// <summary>
    /// Șterge (soft delete) o locație.
    /// </summary>
    /// <param name="id">ID-ul locației.</param>
    /// <param name="updatedBy">ID-ul utilizatorului care șterge.</param>
    /// <returns>True dacă ștergerea a reușit.</returns>
    Task<bool> DeleteAsync(Guid id, Guid? updatedBy = null);

    /// <summary>
    /// Obține locațiile cu stoc pentru o companie.
    /// </summary>
    /// <param name="companyId">ID-ul companiei.</param>
    /// <returns>Lista de locații cu stoc.</returns>
    Task<IEnumerable<Location>> GetWithStockAsync(Guid companyId);

    /// <summary>
    /// Obține locațiile pentru vânzare pentru o companie.
    /// </summary>
    /// <param name="companyId">ID-ul companiei.</param>
    /// <returns>Lista de locații pentru vânzare.</returns>
    Task<IEnumerable<Location>> GetForSaleAsync(Guid companyId);

    /// <summary>
    /// Verifică dacă există o locație cu codul intern specificat pentru companie.
    /// </summary>
    /// <param name="companyId">ID-ul companiei.</param>
    /// <param name="codIntern">Codul intern de verificat.</param>
    /// <param name="excludeId">ID-ul locației de exclus (pentru update).</param>
    /// <returns>True dacă există.</returns>
    Task<bool> ExistsByCodInternAsync(Guid companyId, string codIntern, Guid? excludeId = null);
}
