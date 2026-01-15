using Syncfusion.Blazor;
using Syncfusion.Blazor.Data;
using ValyanERP.Web.Features.Administrare.Articole.Models;

namespace ValyanERP.Web.Features.Administrare.Articole.Repositories;

public interface IArticoleRepository
{
    Task<DataResult> GetPagedAsync(DataManagerRequest dm);
    Task<IEnumerable<Articol>> GetAllAsync();
    Task<Articol?> GetByIdAsync(Guid id);
    Task<Articol?> GetByCodeAsync(string code);
    Task<Guid> CreateAsync(ArticolCreateDto articol);
    Task<bool> UpdateAsync(ArticolUpdateDto articol);
    Task<bool> DeleteAsync(Guid id);
    Task<bool> ExistsAsync(string code, Guid? excludeId = null);
    Task<int> GetTotalCountAsync();
}