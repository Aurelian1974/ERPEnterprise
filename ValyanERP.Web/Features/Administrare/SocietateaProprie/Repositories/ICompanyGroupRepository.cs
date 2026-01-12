using ValyanERP.Web.Features.Administrare.SocietateaProprie.Models;

namespace ValyanERP.Web.Features.Administrare.SocietateaProprie.Repositories;

/// <summary>
/// Repository pentru operații CRUD pe grupuri de companii.
/// </summary>
public interface ICompanyGroupRepository
{
    /// <summary>
    /// Obține toate grupurile de companii.
    /// </summary>
    /// <param name="includeInactive">Dacă se includ și grupurile inactive.</param>
    /// <returns>Lista de grupuri.</returns>
    Task<IEnumerable<CompanyGroup>> GetAllAsync(bool includeInactive = false);

    /// <summary>
    /// Obține un grup după ID.
    /// </summary>
    /// <param name="id">ID-ul grupului.</param>
    /// <returns>Grupul găsit sau null.</returns>
    Task<CompanyGroup?> GetByIdAsync(Guid id);

    /// <summary>
    /// Creează un grup nou.
    /// </summary>
    /// <param name="group">Datele grupului.</param>
    /// <param name="createdBy">ID-ul utilizatorului care creează.</param>
    /// <returns>ID-ul grupului creat.</returns>
    Task<Guid> CreateAsync(CompanyGroup group, Guid? createdBy = null);

    /// <summary>
    /// Actualizează un grup existent.
    /// </summary>
    /// <param name="group">Datele actualizate.</param>
    /// <param name="updatedBy">ID-ul utilizatorului care modifică.</param>
    /// <returns>True dacă actualizarea a reușit.</returns>
    Task<bool> UpdateAsync(CompanyGroup group, Guid? updatedBy = null);

    /// <summary>
    /// Șterge (soft delete) un grup.
    /// </summary>
    /// <param name="id">ID-ul grupului.</param>
    /// <param name="updatedBy">ID-ul utilizatorului care șterge.</param>
    /// <returns>True dacă ștergerea a reușit.</returns>
    Task<bool> DeleteAsync(Guid id, Guid? updatedBy = null);
}
