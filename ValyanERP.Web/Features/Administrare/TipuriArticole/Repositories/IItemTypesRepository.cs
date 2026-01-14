using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ValyanERP.Web.Features.Administrare.TipuriArticole.Models;

namespace ValyanERP.Web.Features.Administrare.TipuriArticole.Repositories
{
    public interface IItemTypesRepository
    {
        Task<IEnumerable<ItemType>> GetAllAsync();
        Task<ItemType?> GetByIdAsync(Guid id);
        Task<Guid> CreateAsync(string itemTypeCode, string itemTypeName, Guid? createdBy);
        Task<int> UpdateAsync(Guid id, string itemTypeCode, string itemTypeName, Guid? updatedBy);
        Task<int> DeleteAsync(Guid id, Guid? updatedBy);
    }
}