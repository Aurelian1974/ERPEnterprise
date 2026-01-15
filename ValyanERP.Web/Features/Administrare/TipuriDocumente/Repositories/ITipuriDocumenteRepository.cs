using ValyanERP.Web.Features.Administrare.TipuriDocumente.Models;

namespace ValyanERP.Web.Features.Administrare.TipuriDocumente.Repositories;

public interface ITipuriDocumenteRepository
{
    Task<IEnumerable<TipDocument>> GetAllAsync();
    Task<TipDocument?> GetByIdAsync(Guid id);
    Task<int> CreateAsync(CreateTipDocumentDto dto);
    Task<bool> UpdateAsync(UpdateTipDocumentDto dto);
    Task<bool> DeleteAsync(Guid id);
    Task<bool> ExistsByCodeAsync(string code, Guid? excludeId = null);
    Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null);
}