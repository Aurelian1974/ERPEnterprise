using ValyanERP.Web.Features.Administrare.TipuriDocumente.Models;

namespace ValyanERP.Web.Features.Administrare.TipuriDocumente.Services;

public interface ITipuriDocumenteService
{
    Task<IEnumerable<TipDocument>> GetAllAsync();
    Task<TipDocument?> GetByIdAsync(Guid id);
    Task<int> CreateAsync(CreateTipDocumentDto dto);
    Task<bool> UpdateAsync(UpdateTipDocumentDto dto);
    Task<bool> DeleteAsync(Guid id);
    Task<bool> CanDeleteAsync(Guid id);
}