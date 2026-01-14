using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ValyanERP.Web.Features.Administrare.TipuriArticole.Models;

namespace ValyanERP.Web.Features.Administrare.TipuriArticole.Services
{
    public interface IItemTypesService
    {
        Task<IEnumerable<ItemType>> GetAllAsync();
        Task<ItemType?> GetByIdAsync(Guid id);
        Task<Guid> CreateAsync(string code, string name);
        Task<int> UpdateAsync(Guid id, string code, string name);
        Task<int> DeleteAsync(Guid id);
    }
}