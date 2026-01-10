using Dapper;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Syncfusion.Blazor;
using Syncfusion.Blazor.Data;
using System.Data;
using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using ValyanERP.Web.Features.Administrare.Persoane.Models;
using ValyanERP.Web.Features.Infrastructure.Audit.Services;
using ValyanERP.Web.Infrastructure.Data;

namespace ValyanERP.Web.Features.Administrare.Persoane.Repositories;

/// <summary>
/// Repository for Persoane data access using stored procedures.
/// All operations use parameterized queries to prevent SQL injection.
/// Includes automatic audit logging for Create/Update/Delete operations.
/// </summary>
public class PersoaneRepository : IPersoaneRepository
{
    private readonly DapperContext _context;
    private readonly ILogger<PersoaneRepository> _logger;
    private readonly IAuditService _auditService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AuthenticationStateProvider _authStateProvider;

    public PersoaneRepository(
        DapperContext context, 
        ILogger<PersoaneRepository> logger,
        IAuditService auditService,
        IHttpContextAccessor httpContextAccessor,
        AuthenticationStateProvider authStateProvider)
    {
        _context = context;
        _logger = logger;
        _auditService = auditService;
        _httpContextAccessor = httpContextAccessor;
        _authStateProvider = authStateProvider;
    }

    public async Task<DataResult> GetPagedAsync(DataManagerRequest dm)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            _logger.LogDebug("GetPagedAsync called with Skip={Skip}, Take={Take}", dm.Skip, dm.Take);
            
            using var connection = _context.CreateConnection();
            var parameters = new DynamicParameters();

            // Extract search term
            string? searchTerm = null;
            if (dm.Search != null && dm.Search.Count > 0)
            {
                searchTerm = dm.Search[0].Key;
                _logger.LogDebug("Search term: {SearchTerm}", searchTerm);
            }

            // Extract filter (simple implementation for first filter only)
            string? filterField = null;
            string? filterOperator = null;
            string? filterValue = null;
            if (dm.Where != null && dm.Where.Count > 0)
            {
                var where = dm.Where[0];
                filterField = where.Field;
                filterOperator = where.Operator?.ToLower();
                filterValue = where.value?.ToString();
                _logger.LogDebug("Filter: Field={Field}, Operator={Operator}, Value={Value}", 
                    filterField, filterOperator, filterValue);
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
            parameters.Add("@FilterField", filterField);
            parameters.Add("@FilterOperator", filterOperator);
            parameters.Add("@FilterValue", filterValue);
            parameters.Add("@SortField", sortField);
            parameters.Add("@SortDirection", sortDirection);
            parameters.Add("@Skip", dm.Skip);
            parameters.Add("@Take", dm.Take == 0 ? 20 : dm.Take);

            // Execute stored procedure - returns multiple result sets
            using var multi = await connection.QueryMultipleAsync(
                "sp_Persoane_GetPaged",
                parameters,
                commandType: CommandType.StoredProcedure);

            var items = await multi.ReadAsync<Persoana>();
            var countResult = await multi.ReadFirstOrDefaultAsync<int>();

            _logger.LogInformation("GetPagedAsync returned {Count} records out of {Total} in {ElapsedMs}ms", 
                items.Count(), countResult, stopwatch.ElapsedMilliseconds);

            return new DataResult
            {
                Result = items,
                Count = countResult
            };
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL error in GetPagedAsync after {ElapsedMs}ms: {Message}", 
                stopwatch.ElapsedMilliseconds, ex.Message);
            throw new InvalidOperationException($"Database error in GetPagedAsync: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in GetPagedAsync after {ElapsedMs}ms: {Message}", 
                stopwatch.ElapsedMilliseconds, ex.Message);
            throw;
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    public async Task<Persoana?> GetByIdAsync(Guid id)
    {
        try
        {
            _logger.LogDebug("GetByIdAsync called with Id={Id}", id);
            using var connection = _context.CreateConnection();
            var result = await connection.QueryFirstOrDefaultAsync<Persoana>(
                "sp_Persoane_GetById",
                new { Id = id },
                commandType: CommandType.StoredProcedure);
            
            if (result == null)
                _logger.LogWarning("Persoana with Id={Id} not found", id);
            
            return result;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL error in GetByIdAsync for Id={Id}", id);
            throw new InvalidOperationException($"Database error in GetByIdAsync: {ex.Message}", ex);
        }
    }

    public async Task CreateAsync(Persoana persoana)
    {
        try
        {
            _logger.LogInformation("Creating new Persoana: {Nume} {Prenume}", persoana.Nume, persoana.Prenume);
            using var connection = _context.CreateConnection();
            
            if (persoana.Id == Guid.Empty) 
                persoana.Id = Guid.NewGuid();
            
            await connection.QueryFirstOrDefaultAsync<Persoana>(
                "sp_Persoane_Create",
                new
                {
                    persoana.Id,
                    persoana.Nume,
                    persoana.Prenume,
                    persoana.CNP,
                    persoana.DataNasterii,
                    persoana.Email,
                    persoana.Telefon,
                    persoana.Adresa,
                    persoana.Oras,
                    persoana.Judet,
                    persoana.CodPostal,
                    persoana.Tara,
                    persoana.IsActive
                },
                commandType: CommandType.StoredProcedure);
            
            _logger.LogInformation("Persoana created successfully with Id={Id}", persoana.Id);
            
            // Auto-audit: Log Create operation
            await LogAuditAsync("Create", persoana.Id.ToString(), persoana);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL error creating Persoana: {Nume} {Prenume}", persoana.Nume, persoana.Prenume);
            throw new InvalidOperationException($"Database error in CreateAsync: {ex.Message}", ex);
        }
    }

    public async Task UpdateAsync(Persoana persoana)
    {
        try
        {
            _logger.LogInformation("Updating Persoana Id={Id}: {Nume} {Prenume}", persoana.Id, persoana.Nume, persoana.Prenume);
            
            // Get old value for audit diff
            var oldValue = await GetByIdAsync(persoana.Id);
            
            using var connection = _context.CreateConnection();
            await connection.ExecuteAsync(
                "sp_Persoane_Update",
                new
                {
                    persoana.Id,
                    persoana.Nume,
                    persoana.Prenume,
                    persoana.CNP,
                    persoana.DataNasterii,
                    persoana.Email,
                    persoana.Telefon,
                    persoana.Adresa,
                    persoana.Oras,
                    persoana.Judet,
                    persoana.CodPostal,
                    persoana.Tara,
                    persoana.IsActive
                },
                commandType: CommandType.StoredProcedure);
            
            _logger.LogInformation("Persoana Id={Id} updated successfully", persoana.Id);
            
            // Auto-audit: Log Update operation with diff
            if (oldValue != null)
            {
                await LogAuditAsync("Update", persoana.Id.ToString(), persoana, oldValue);
            }
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL error updating Persoana Id={Id}", persoana.Id);
            throw new InvalidOperationException($"Database error in UpdateAsync: {ex.Message}", ex);
        }
    }

    public async Task DeleteAsync(Guid id)
    {
        try
        {
            _logger.LogInformation("Soft deleting Persoana Id={Id}", id);
            
            // Get entity before delete for audit
            var deletedValue = await GetByIdAsync(id);
            
            using var connection = _context.CreateConnection();
            // Soft delete via stored procedure
            await connection.ExecuteAsync(
                "sp_Persoane_Delete",
                new { Id = id },
                commandType: CommandType.StoredProcedure);
            
            _logger.LogInformation("Persoana Id={Id} soft deleted successfully", id);
            
            // Auto-audit: Log Delete operation
            if (deletedValue != null)
            {
                await LogAuditAsync("Delete", id.ToString(), deletedValue);
            }
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL error deleting Persoana Id={Id}", id);
            throw new InvalidOperationException($"Database error in DeleteAsync: {ex.Message}", ex);
        }
    }

    public async Task<Persoana?> GetByEmailAsync(string email)
    {
        try
        {
            _logger.LogDebug("GetByEmailAsync called with Email={Email}", email);
            using var connection = _context.CreateConnection();
            var result = await connection.QueryFirstOrDefaultAsync<Persoana>(
                "sp_Persoane_GetByEmail",
                new { Email = email },
                commandType: CommandType.StoredProcedure);
            
            return result;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL error in GetByEmailAsync for Email={Email}", email);
            throw new InvalidOperationException($"Database error in GetByEmailAsync: {ex.Message}", ex);
        }
    }

    public async Task<IEnumerable<Persoana>> GetAllSimpleAsync()
    {
        try
        {
            _logger.LogDebug("GetAllSimpleAsync called");
            using var connection = _context.CreateConnection();
            // Now returns NumeComplet calculated field and limits to 1000 records
            var result = await connection.QueryAsync<Persoana>(
                "sp_Persoane_GetAllSimple",
                commandType: CommandType.StoredProcedure);
            
            _logger.LogInformation("GetAllSimpleAsync returned {Count} records", result.Count());
            return result;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL error in GetAllSimpleAsync");
            throw new InvalidOperationException($"Database error in GetAllSimpleAsync: {ex.Message}", ex);
        }
    }

    #region Private Audit Helper Methods

    /// <summary>
    /// Logs audit trail for Create/Update/Delete operations
    /// </summary>
    private async Task LogAuditAsync(string operationType, string entityId, Persoana entity, Persoana? oldValue = null)
    {
        try
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId == Guid.Empty)
            {
                _logger.LogWarning("Skipping audit log - no authenticated user found");
                return; // Skip if no authenticated user
            }

            var sessionId = GetCurrentSessionId();
            var ipAddress = GetClientIP();
            var userAgent = GetUserAgent();

            if (operationType == "Create")
            {
                await _auditService.LogCreateAsync(
                    "Persoane",
                    entityId,
                    entity,
                    userId,
                    sessionId,
                    ipAddress,
                    userAgent
                );
            }
            else if (operationType == "Update" && oldValue != null)
            {
                await _auditService.LogUpdateAsync(
                    "Persoane",
                    entityId,
                    oldValue,
                    entity,
                    userId,
                    sessionId,
                    ipAddress,
                    userAgent
                );
            }
            else if (operationType == "Delete")
            {
                await _auditService.LogDeleteAsync(
                    "Persoane",
                    entityId,
                    entity,
                    userId,
                    sessionId,
                    ipAddress,
                    userAgent
                );
            }

            _logger.LogDebug("Audit log created for {OperationType} on Persoane Id={EntityId}", operationType, entityId);
        }
        catch (Exception ex)
        {
            // Don't fail the operation if audit fails, just log the error
            _logger.LogError(ex, "Failed to create audit log for {OperationType} on Persoane Id={EntityId}", operationType, entityId);
        }
    }

    /// <summary>
    /// Extracts current user ID from AuthenticationState (Blazor Server compatible)
    /// </summary>
    private async Task<Guid> GetCurrentUserIdAsync()
    {
        try
        {
            var authState = await _authStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;

            if (user?.Identity?.IsAuthenticated == true)
            {
                var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
                {
                    return userId;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user ID from AuthenticationState");
        }

        return Guid.Empty;
    }

    /// <summary>
    /// Extracts current session ID from claims or session storage
    /// </summary>
    private Guid? GetCurrentSessionId()
    {
        // Try to get from custom claim first
        var sessionClaim = _httpContextAccessor.HttpContext?.User?.FindFirst("SessionId");
        if (sessionClaim != null && Guid.TryParse(sessionClaim.Value, out var sessionId))
        {
            return sessionId;
        }

        // Fallback: try to get from session storage
        var sessionIdString = _httpContextAccessor.HttpContext?.Session?.GetString("SessionId");
        if (!string.IsNullOrEmpty(sessionIdString) && Guid.TryParse(sessionIdString, out var sessionIdFromStorage))
        {
            return sessionIdFromStorage;
        }

        return null;
    }

    /// <summary>
    /// Extracts client IP address from HttpContext
    /// </summary>
    private string? GetClientIP()
    {
        return _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString();
    }

    /// <summary>
    /// Extracts User-Agent from request headers
    /// </summary>
    private string? GetUserAgent()
    {
        return _httpContextAccessor.HttpContext?.Request?.Headers["User-Agent"].ToString();
    }

    #endregion
}
