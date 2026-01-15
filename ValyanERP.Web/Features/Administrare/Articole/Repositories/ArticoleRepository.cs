using Dapper;
using System.Data;
using Syncfusion.Blazor;
using Syncfusion.Blazor.Data;
using ValyanERP.Web.Features.Administrare.Articole.Models;
using ValyanERP.Web.Infrastructure.Data;

namespace ValyanERP.Web.Features.Administrare.Articole.Repositories;

public class ArticoleRepository : IArticoleRepository
{
    private readonly DapperContext _context;
    private readonly ILogger<ArticoleRepository> _logger;

    public ArticoleRepository(DapperContext context, ILogger<ArticoleRepository> logger)
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
                "sp_Articole_GetPaged",
                parameters,
                commandType: CommandType.StoredProcedure);

            var items = await multi.ReadAsync<Articol>();
            var countResult = await multi.ReadFirstOrDefaultAsync<int>();

            return new DataResult
            {
                Result = items,
                Count = countResult
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Articole GetPagedAsync: {Message}", ex.Message);
            throw new InvalidOperationException($"Database error in GetPagedAsync: {ex.Message}", ex);
        }
    }

    public async Task<IEnumerable<Articol>> GetAllAsync()
    {
        try
        {
            using var connection = _context.CreateConnection();
            var result = await connection.QueryAsync<Articol>(
                "sp_Articole_GetAll",
                commandType: CommandType.StoredProcedure);

            _logger.LogDebug("Retrieved {Count} articole", result.Count());
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all articole");
            throw;
        }
    }

    public async Task<Articol?> GetByIdAsync(Guid id)
    {
        try
        {
            using var connection = _context.CreateConnection();
            return await connection.QueryFirstOrDefaultAsync<Articol>(
                "sp_Articole_GetById",
                new { Id = id },
                commandType: CommandType.StoredProcedure);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving articol by ID {Id}", id);
            throw;
        }
    }

    public async Task<Articol?> GetByCodeAsync(string code)
    {
        try
        {
            using var connection = _context.CreateConnection();
            return await connection.QueryFirstOrDefaultAsync<Articol>(
                "sp_Articole_GetByCode",
                new { ArticolCode = code },
                commandType: CommandType.StoredProcedure);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving articol by code {Code}", code);
            throw;
        }
    }

    public async Task<Guid> CreateAsync(ArticolCreateDto articol)
    {
        try
        {
            using var connection = _context.CreateConnection();
            var parameters = new DynamicParameters();
            parameters.Add("@ArticolCode", articol.ArticolCode);
            parameters.Add("@ArticolName", articol.ArticolName);
            parameters.Add("@Description", articol.Description);
            parameters.Add("@TipArticolId", articol.TipArticolId);
            parameters.Add("@IsStockable", articol.IsStockable);
            parameters.Add("@OwnerCompanyId", articol.OwnerCompanyId);
            parameters.Add("@OwnerWorkPlaceId", articol.OwnerWorkPlaceId);
            parameters.Add("@OwnerLocationId", articol.OwnerLocationId);

            var result = await connection.ExecuteScalarAsync<Guid>(
                "sp_Articole_Create",
                parameters,
                commandType: CommandType.StoredProcedure);

            _logger.LogInformation("Created articol: {Code}", articol.ArticolCode);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating articol {Code}", articol.ArticolCode);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(ArticolUpdateDto articol)
    {
        try
        {
            using var connection = _context.CreateConnection();
            var parameters = new DynamicParameters();
            parameters.Add("@Id", articol.Id);
            parameters.Add("@ArticolCode", articol.ArticolCode);
            parameters.Add("@ArticolName", articol.ArticolName);
            parameters.Add("@Description", articol.Description);
            parameters.Add("@TipArticolId", articol.TipArticolId);
            parameters.Add("@IsStockable", articol.IsStockable);

            var rowsAffected = await connection.ExecuteAsync(
                "sp_Articole_Update",
                parameters,
                commandType: CommandType.StoredProcedure);

            var success = rowsAffected > 0;
            if (success)
            {
                _logger.LogInformation("Updated articol: {Code}", articol.ArticolCode);
            }
            else
            {
                _logger.LogWarning("No rows affected when updating articol {Id}", articol.Id);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating articol {Id}", articol.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        try
        {
            using var connection = _context.CreateConnection();
            var rowsAffected = await connection.ExecuteAsync(
                "sp_Articole_Delete",
                new { Id = id },
                commandType: CommandType.StoredProcedure);

            var success = rowsAffected > 0;
            if (success)
            {
                _logger.LogInformation("Deleted articol {Id}", id);
            }
            else
            {
                _logger.LogWarning("No rows affected when deleting articol {Id}", id);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting articol {Id}", id);
            throw;
        }
    }

    public async Task<bool> ExistsAsync(string code, Guid? excludeId = null)
    {
        try
        {
            using var connection = _context.CreateConnection();
            var parameters = new DynamicParameters();
            parameters.Add("@ArticolCode", code);
            parameters.Add("@ExcludeId", excludeId);

            var count = await connection.ExecuteScalarAsync<int>(
                "sp_Articole_Exists",
                parameters,
                commandType: CommandType.StoredProcedure);

            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if articol exists {Code}", code);
            throw;
        }
    }
}