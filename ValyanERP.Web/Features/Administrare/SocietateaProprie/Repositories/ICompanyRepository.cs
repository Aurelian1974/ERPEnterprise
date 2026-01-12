using ValyanERP.Web.Features.Administrare.SocietateaProprie.Models;

namespace ValyanERP.Web.Features.Administrare.SocietateaProprie.Repositories;

/// <summary>
/// Repository pentru operații CRUD pe companii.
/// </summary>
public interface ICompanyRepository
{
    /// <summary>
    /// Obține toate companiile (cu date din View).
    /// </summary>
    /// <param name="includeInactive">Dacă se includ și companiile inactive.</param>
    /// <param name="groupId">Filtrare opțională pe grup.</param>
    /// <returns>Lista de companii.</returns>
    Task<IEnumerable<Company>> GetAllAsync(bool includeInactive = false, Guid? groupId = null);

    /// <summary>
    /// Obține o companie după ID.
    /// </summary>
    /// <param name="id">ID-ul companiei.</param>
    /// <returns>Compania găsită sau null.</returns>
    Task<Company?> GetByIdAsync(Guid id);

    /// <summary>
    /// Obține o companie după CUI.
    /// </summary>
    /// <param name="cui">Codul unic de identificare.</param>
    /// <returns>Compania găsită sau null.</returns>
    Task<Company?> GetByCUIAsync(string cui);

    /// <summary>
    /// Creează o companie nouă.
    /// </summary>
    /// <param name="company">Datele companiei.</param>
    /// <param name="createdBy">ID-ul utilizatorului care creează.</param>
    /// <returns>ID-ul companiei create.</returns>
    Task<Guid> CreateAsync(Company company, Guid? createdBy = null);

    /// <summary>
    /// Actualizează o companie existentă.
    /// </summary>
    /// <param name="company">Datele actualizate.</param>
    /// <param name="updatedBy">ID-ul utilizatorului care modifică.</param>
    /// <returns>True dacă actualizarea a reușit.</returns>
    Task<bool> UpdateAsync(Company company, Guid? updatedBy = null);

    /// <summary>
    /// Șterge (soft delete) o companie.
    /// </summary>
    /// <param name="id">ID-ul companiei.</param>
    /// <param name="updatedBy">ID-ul utilizatorului care șterge.</param>
    /// <returns>True dacă ștergerea a reușit.</returns>
    Task<bool> DeleteAsync(Guid id, Guid? updatedBy = null);

    /// <summary>
    /// Verifică dacă există o companie cu CUI-ul specificat.
    /// </summary>
    /// <param name="cui">CUI-ul de verificat.</param>
    /// <param name="excludeId">ID-ul companiei de exclus (pentru update).</param>
    /// <returns>True dacă există.</returns>
    Task<bool> ExistsByCUIAsync(string cui, Guid? excludeId = null);
}
