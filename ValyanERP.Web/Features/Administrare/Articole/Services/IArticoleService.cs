using ValyanERP.Web.Features.Administrare.Articole.Models;

namespace ValyanERP.Web.Features.Administrare.Articole.Services;

public interface IArticoleService
{
    Task<IEnumerable<Articol>> GetAllArticoleAsync();
    Task<Articol?> GetArticolByIdAsync(Guid id);
    Task<Articol?> GetArticolByCodeAsync(string code);
    Task<bool> CreateArticolAsync(ArticolCreateDto articolDto);
    Task<bool> UpdateArticolAsync(ArticolUpdateDto articolDto);
    Task<bool> DeleteArticolAsync(Guid id);
    Task<bool> ArticolExistsAsync(string code, Guid? excludeId = null);
}