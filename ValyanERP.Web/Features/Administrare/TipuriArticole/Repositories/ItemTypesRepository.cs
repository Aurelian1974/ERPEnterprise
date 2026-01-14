using Dapper;
using Microsoft.Extensions.Logging;
using Syncfusion.Blazor;
using Syncfusion.Blazor.Data;
using System.Data;
using System.Text;
using ValyanERP.Web.Features.Administrare.TipuriArticole.Models;
using ValyanERP.Web.Infrastructure.Data;

namespace ValyanERP.Web.Features.Administrare.TipuriArticole.Repositories;

/// <summary>
/// Repository for ItemTypes data access using stored procedures.
/// All operations use parameterized queries to prevent SQL injection.
/// </summary>
public class ItemTypesRepository : IItemTypesRepository
{
    private readonly DapperContext _context;
    private readonly ILogger<ItemTypesRepository> _logger;

    public ItemTypesRepository(DapperContext context, ILogger<ItemTypesRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<DataResult> GetPagedAsync(DataManagerRequest dm)
    {
        try
        {
            using var connection = _context.CreateConnection();
            var parameters = new DynamicParameters();

            // Extract search term
            string? searchTerm = null;
            if (dm.Search != null && dm.Search.Count > 0)
            {
                searchTerm = dm.Search[0].Key;
            }

            // Extract sort
            string sortField = "CreatedAt";
            string sortDirection = "DESC";
            if (dm.Sorted != null && dm.Sorted.Count > 0)
            {
                var sort = dm.Sorted[0];
                sortField = sort.Name;
                sortDirection = sort.Direction;
            }

            // Set parameters for stored procedure
            // When Take=0, return all data (used for export operations)
            parameters.Add("@SearchTerm", searchTerm);
            parameters.Add("@SortField", sortField);
            parameters.Add("@SortDirection", sortDirection);
            parameters.Add("@Skip", dm.Skip);
            parameters.Add("@Take", dm.Take == 0 ? 10000 : dm.Take); // 10000 = effectively all for export

            // Execute stored procedure - returns multiple result sets
            using var multi = await connection.QueryMultipleAsync(
                "sp_ItemTypes_GetPaged",
                parameters,
                commandType: CommandType.StoredProcedure);

            var items = await multi.ReadAsync<ItemType>();
            var countResult = await multi.ReadFirstOrDefaultAsync<int>();

            return new DataResult
            {
                Result = items,
                Count = countResult
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ItemTypes GetPagedAsync: {Message}", ex.Message);
            throw new InvalidOperationException($"Database error in GetPagedAsync: {ex.Message}", ex);
        }
    }

    public async Task<IEnumerable<ItemType>> GetAllAsync()
    {
        try
        {
            using var connection = _context.CreateConnection();
            return await connection.QueryAsync<ItemType>(
                "sp_ItemTypes_GetAll",
                commandType: CommandType.StoredProcedure);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ItemTypes GetAllAsync: {Message}", ex.Message);
            throw new InvalidOperationException($"Database error in GetAllAsync: {ex.Message}", ex);
        }
    }

    public async Task<ItemType?> GetByIdAsync(Guid id)
    {
        try
        {
            using var connection = _context.CreateConnection();
            return await connection.QueryFirstOrDefaultAsync<ItemType>(
                "sp_ItemTypes_GetById",
                new { Id = id },
                commandType: CommandType.StoredProcedure);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ItemTypes GetByIdAsync: {Message}", ex.Message);
            throw new InvalidOperationException($"Database error in GetByIdAsync: {ex.Message}", ex);
        }
    }

    public async Task CreateAsync(ItemTypeCreateDto itemType)
    {
        try
        {
            using var connection = _context.CreateConnection();
            var parameters = new DynamicParameters();
            parameters.Add("@ItemTypeCode", itemType.ItemTypeCode);
            parameters.Add("@ItemTypeName", itemType.ItemTypeName);
            parameters.Add("@Description", itemType.Description);
            // TODO: Add CreatedBy when user context is available
            // parameters.Add("@CreatedBy", userId);

            await connection.ExecuteAsync(
                "sp_ItemTypes_Create",
                parameters,
                commandType: CommandType.StoredProcedure);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ItemTypes CreateAsync: {Message}", ex.Message);
            throw new InvalidOperationException($"Database error in CreateAsync: {ex.Message}", ex);
        }
    }

    public async Task UpdateAsync(ItemTypeUpdateDto itemType)
    {
        try
        {
            using var connection = _context.CreateConnection();
            var parameters = new DynamicParameters();
            parameters.Add("@Id", itemType.Id);
            parameters.Add("@ItemTypeCode", itemType.ItemTypeCode);
            parameters.Add("@ItemTypeName", itemType.ItemTypeName);
            parameters.Add("@Description", itemType.Description);

            await connection.ExecuteAsync(
                "sp_ItemTypes_Update",
                parameters,
                commandType: CommandType.StoredProcedure);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ItemTypes UpdateAsync: {Message}", ex.Message);
            throw new InvalidOperationException($"Database error in UpdateAsync: {ex.Message}", ex);
        }
    }

    public async Task DeleteAsync(Guid id)
    {
        try
        {
            using var connection = _context.CreateConnection();
            await connection.ExecuteAsync(
                "sp_ItemTypes_Delete",
                new { Id = id },
                commandType: CommandType.StoredProcedure);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ItemTypes DeleteAsync: {Message}", ex.Message);
            throw new InvalidOperationException($"Database error in DeleteAsync: {ex.Message}", ex);
        }
    }
}