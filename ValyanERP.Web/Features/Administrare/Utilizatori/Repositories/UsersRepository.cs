using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Syncfusion.Blazor;
using Syncfusion.Blazor.Data;
using System.Data;
using System.Text;
using ValyanERP.Web.Features.Administrare.Utilizatori.Models;
using ValyanERP.Web.Infrastructure.Data;

namespace ValyanERP.Web.Features.Administrare.Utilizatori.Repositories;

/// <summary>
/// Repository for Users data access using stored procedures.
/// All operations use parameterized queries to prevent SQL injection.
/// </summary>
public class UsersRepository : IUsersRepository
{
    private readonly DapperContext _context;
    private readonly ILogger<UsersRepository> _logger;

    public UsersRepository(DapperContext context, ILogger<UsersRepository> logger)
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
            parameters.Add("@SearchTerm", searchTerm);
            parameters.Add("@SortField", sortField);
            parameters.Add("@SortDirection", sortDirection);
            parameters.Add("@Skip", dm.Skip);
            parameters.Add("@Take", dm.Take == 0 ? 20 : dm.Take);

            // Execute stored procedure - returns multiple result sets
            using var multi = await connection.QueryMultipleAsync(
                "sp_Users_GetPaged",
                parameters,
                commandType: CommandType.StoredProcedure);

            var items = await multi.ReadAsync<User>();
            var countResult = await multi.ReadFirstOrDefaultAsync<int>();

            return new DataResult
            {
                Result = items,
                Count = countResult
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Users GetPagedAsync: {Message}", ex.Message);
            throw new InvalidOperationException($"Database error in GetPagedAsync: {ex.Message}", ex);
        }
    }

    public async Task CreateAsync(UserCreateDto user)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            // NOTE: Password should be hashed by Service layer BEFORE calling repository!
            // This repository should NOT hash passwords - that's business logic.
            // For now, accepting pre-hashed password from caller.
            
            await connection.QueryFirstOrDefaultAsync<User>(
                "sp_Users_Create",
                new
                {
                    Id = Guid.NewGuid(),
                    user.PersoanaId,
                    user.UserName,
                    NormalizedUserName = user.UserName.ToUpperInvariant(),
                    user.Email,
                    NormalizedEmail = user.Email.ToUpperInvariant(),
                    user.PasswordHash, // Pre-hashed by service
                    user.IsActive
                },
                commandType: CommandType.StoredProcedure);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating User {UserName}", user.UserName);
            throw new InvalidOperationException($"Database error in CreateAsync: {ex.Message}", ex);
        }
    }

    public async Task<User?> GetByIdAsync(Guid id)
    {
        try
        {
            using var connection = _context.CreateConnection();
            var result = await connection.QueryFirstOrDefaultAsync<User>(
                "sp_Users_GetById",
                new { Id = id },
                commandType: CommandType.StoredProcedure);
            
            if (result == null)
                _logger.LogWarning("User with Id={Id} not found", id);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetByIdAsync for UserId={Id}", id);
            throw new InvalidOperationException($"Database error in GetByIdAsync: {ex.Message}", ex);
        }
    }

    public async Task UpdateAsync(User user)
    {
        try
        {
            using var connection = _context.CreateConnection();
            await connection.ExecuteAsync(
                "sp_Users_Update",
                new
                {
                    user.Id,
                    user.PersoanaId,
                    user.UserName,
                    NormalizedUserName = user.UserName.ToUpperInvariant(),
                    user.Email,
                    NormalizedEmail = user.Email.ToUpperInvariant(),
                    user.IsActive
                },
                commandType: CommandType.StoredProcedure);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating User Id={Id}", user.Id);
            throw new InvalidOperationException($"Database error in UpdateAsync: {ex.Message}", ex);
        }
    }

    public async Task DeleteAsync(Guid id)
    {
        try
        {
            using var connection = _context.CreateConnection();
            // Soft delete via stored procedure - consistent with application standard
            await connection.ExecuteAsync(
                "sp_Users_Delete",
                new { Id = id },
                commandType: CommandType.StoredProcedure);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting User Id={Id}", id);
            throw new InvalidOperationException($"Database error in DeleteAsync: {ex.Message}", ex);
        }
    }
}
