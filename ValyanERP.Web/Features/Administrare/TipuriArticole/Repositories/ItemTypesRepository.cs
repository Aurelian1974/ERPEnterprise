using Dapper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using ValyanERP.Web.Features.Administrare.TipuriArticole.Models;
using ValyanERP.Web.Infrastructure.Data;

namespace ValyanERP.Web.Features.Administrare.TipuriArticole.Repositories
{
    public class ItemTypesRepository : IItemTypesRepository
    {
        private readonly DapperContext _context;

        public ItemTypesRepository(DapperContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<ItemType>> GetAllAsync()
        {
            using var connection = _context.CreateConnection();
            return await connection.QueryAsync<ItemType>("sp_ItemTypes_GetAll", commandType: CommandType.StoredProcedure);
        }

        public async Task<ItemType?> GetByIdAsync(Guid id)
        {
            using var connection = _context.CreateConnection();
            return await connection.QueryFirstOrDefaultAsync<ItemType>("sp_ItemTypes_GetById", new { Id = id }, commandType: CommandType.StoredProcedure);
        }

        public async Task<Guid> CreateAsync(string itemTypeCode, string itemTypeName, Guid? createdBy)
        {
            using var connection = _context.CreateConnection();
            var result = await connection.QuerySingleAsync<Guid>("sp_ItemTypes_Create", new { ItemTypeCode = itemTypeCode, ItemTypeName = itemTypeName, CreatedBy = createdBy }, commandType: CommandType.StoredProcedure);
            return result;
        }

        public async Task<int> UpdateAsync(Guid id, string itemTypeCode, string itemTypeName, Guid? updatedBy)
        {
            using var connection = _context.CreateConnection();
            var result = await connection.QuerySingleAsync<int>("sp_ItemTypes_Update", new { Id = id, ItemTypeCode = itemTypeCode, ItemTypeName = itemTypeName, UpdatedBy = updatedBy }, commandType: CommandType.StoredProcedure);
            return result;
        }

        public async Task<int> DeleteAsync(Guid id, Guid? updatedBy)
        {
            using var connection = _context.CreateConnection();
            var result = await connection.QuerySingleAsync<int>("sp_ItemTypes_Delete", new { Id = id, UpdatedBy = updatedBy }, commandType: CommandType.StoredProcedure);
            return result;
        }
    }
}