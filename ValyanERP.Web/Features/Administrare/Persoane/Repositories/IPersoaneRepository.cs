using ValyanERP.Web.Features.Administrare.Persoane.Models;

namespace ValyanERP.Web.Features.Administrare.Persoane.Repositories;

/// <summary>
/// Interface for Persoane repository operations.
/// </summary>
public interface IPersoaneRepository
{
    Task<IEnumerable<Persoana>> GetAllAsync(bool includeInactive = false);
    Task<Persoana?> GetByIdAsync(int id);
    Task<int> CreateAsync(CreatePersoanaDto persoana, Guid? createdBy = null);
    Task<bool> UpdateAsync(UpdatePersoanaDto persoana, Guid? updatedBy = null);
    Task<bool> DeleteAsync(int id, Guid? updatedBy = null);
    Task<bool> HardDeleteAsync(int id);
    Task<IEnumerable<Persoana>> SearchAsync(string? searchTerm, bool includeInactive = false);
}
