using Syncfusion.Blazor;
using Syncfusion.Blazor.Data;
using ValyanERP.Web.Features.Administrare.TipuriArticole.Models;

namespace ValyanERP.Web.Features.Administrare.TipuriArticole.Repositories;

public interface IItemTypesRepository
{
    Task<DataResult> GetPagedAsync(DataManagerRequest dm);
    Task<IEnumerable<ItemType>> GetAllAsync();
    Task<ItemType?> GetByIdAsync(Guid id);
    Task CreateAsync(ItemTypeCreateDto itemType);
    Task UpdateAsync(ItemTypeUpdateDto itemType);
    Task DeleteAsync(Guid id);
}