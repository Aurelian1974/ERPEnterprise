using ValyanERP.Web.Features.Administrare.SocietateaProprie.Models;

namespace ValyanERP.Web.Features.Administrare.SocietateaProprie.Repositories;

/// <summary>
/// Repository pentru operații CRUD pe puncte de lucru.
/// </summary>
public interface IWorkPlaceRepository
{
    /// <summary>
    /// Obține toate punctele de lucru.
    /// </summary>
    /// <param name="includeInactive">Dacă se includ și punctele inactive.</param>
    /// <param name="companyId">Filtrare opțională pe companie.</param>
    /// <returns>Lista de puncte de lucru.</returns>
    Task<IEnumerable<WorkPlace>> GetAllAsync(bool includeInactive = false, Guid? companyId = null);

    /// <summary>
    /// Obține un punct de lucru după ID.
    /// </summary>
    /// <param name="id">ID-ul punctului de lucru.</param>
    /// <returns>Punctul de lucru găsit sau null.</returns>
    Task<WorkPlace?> GetByIdAsync(Guid id);

    /// <summary>
    /// Creează un punct de lucru nou.
    /// </summary>
    /// <param name="workPlace">Datele punctului de lucru.</param>
    /// <param name="createdBy">ID-ul utilizatorului care creează.</param>
    /// <returns>ID-ul punctului de lucru creat.</returns>
    Task<Guid> CreateAsync(WorkPlace workPlace, Guid? createdBy = null);

    /// <summary>
    /// Actualizează un punct de lucru existent.
    /// </summary>
    /// <param name="workPlace">Datele actualizate.</param>
    /// <param name="updatedBy">ID-ul utilizatorului care modifică.</param>
    /// <returns>True dacă actualizarea a reușit.</returns>
    Task<bool> UpdateAsync(WorkPlace workPlace, Guid? updatedBy = null);

    /// <summary>
    /// Șterge (soft delete) un punct de lucru.
    /// </summary>
    /// <param name="id">ID-ul punctului de lucru.</param>
    /// <param name="updatedBy">ID-ul utilizatorului care șterge.</param>
    /// <returns>True dacă ștergerea a reușit.</returns>
    Task<bool> DeleteAsync(Guid id, Guid? updatedBy = null);

    /// <summary>
    /// Obține sediul social al unei companii.
    /// </summary>
    /// <param name="companyId">ID-ul companiei.</param>
    /// <returns>Sediul social sau null.</returns>
    Task<WorkPlace?> GetSediuSocialAsync(Guid companyId);
}
