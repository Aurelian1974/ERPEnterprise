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
using ValyanERP.Web.Features.Infrastructure.Security.Data;
using ValyanERP.Web.Features.Infrastructure.Security.Services;
using ValyanERP.Web.Infrastructure.Data;

namespace ValyanERP.Web.Features.Administrare.Persoane.Repositories;

/// <summary>
/// Repository for Persoane data access using stored procedures.
/// All operations use parameterized queries to prevent SQL injection.
/// Includes automatic audit logging for Create/Update/Delete operations.
/// Implements organizational security filtering via IUserPerimeterProvider.
/// </summary>
public class PersoaneRepository : IPersoaneRepository
{
    private readonly DapperContext _context;
    private readonly ISecureConnectionFactory _secureConnectionFactory;
    private readonly IUserPerimeterProvider _perimeterProvider;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<PersoaneRepository> _logger;
    private readonly IAuditService _auditService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AuthenticationStateProvider _authStateProvider;

    public PersoaneRepository(
        DapperContext context, 
        ISecureConnectionFactory secureConnectionFactory,
        IUserPerimeterProvider perimeterProvider,
        ICurrentUserService currentUserService,
        ILogger<PersoaneRepository> logger,
        IAuditService auditService,
        IHttpContextAccessor httpContextAccessor,
        AuthenticationStateProvider authStateProvider)
    {
        _context = context;
        _secureConnectionFactory = secureConnectionFactory;
        _perimeterProvider = perimeterProvider;
        _currentUserService = currentUserService;
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
            using var connection = _context.CreateConnection();
            var parameters = new DynamicParameters();

            // Extract search term
            string? searchTerm = null;
            if (dm.Search != null && dm.Search.Count > 0)
            {
                searchTerm = dm.Search[0].Key;
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
            // Treat Take == 0 as a request for all (use a large limit) so export/grouping/counts work correctly
            parameters.Add("@Take", dm.Take == 0 ? 10000 : dm.Take);

            // Execute stored procedure - returns multiple result sets
            using var multi = await connection.QueryMultipleAsync(
                "sp_Persoane_GetPaged",
                parameters,
                commandType: CommandType.StoredProcedure);

            var items = await multi.ReadAsync<Persoana>();
            var countResult = await multi.ReadFirstOrDefaultAsync<int>();

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
            // ═══════════════════════════════════════════════════════════════
            // SECURITY CHECK: Verify user has write access to target company
            // ═══════════════════════════════════════════════════════════════
            if (persoana.OwnerCompanyId.HasValue)
            {
                var canWrite = await _perimeterProvider.CanWriteToCompanyAsync(persoana.OwnerCompanyId.Value);
                if (!canWrite)
                {
                    _logger.LogWarning(
                        "ACCESS DENIED: User {UserId} attempted to create Persoana in company {CompanyId}",
                        _currentUserService.UserId, persoana.OwnerCompanyId.Value);
                    throw new UnauthorizedAccessException(
                        "Nu aveți permisiune de scriere pentru compania selectată.");
                }
            }
            
            using var connection = _context.CreateConnection();
            
            if (persoana.Id == Guid.Empty) 
                persoana.Id = Guid.NewGuid();
            
            // Get current user ID for CreatedBy (use security service)
            persoana.CreatedBy = _currentUserService.UserId != Guid.Empty 
                ? _currentUserService.UserId 
                : await GetCurrentUserIdAsync();
            
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
                    persoana.IsActive,
                    // Ownership columns
                    persoana.OwnerCompanyId,
                    persoana.OwnerWorkPlaceId,
                    persoana.OwnerLocationId,
                    persoana.CreatedBy
                },
                commandType: CommandType.StoredProcedure);
            
            // Auto-audit: Log Create operation
            await LogAuditAsync("Create", persoana.Id.ToString(), persoana);
        }
        catch (UnauthorizedAccessException)
        {
            // Re-throw security exceptions as-is
            throw;
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
            // Get old value for audit diff AND security check
            var oldValue = await GetByIdAsync(persoana.Id);
            
            if (oldValue == null)
            {
                throw new KeyNotFoundException($"Persoana cu Id={persoana.Id} nu a fost găsită.");
            }
            
            // ═══════════════════════════════════════════════════════════════
            // SECURITY CHECK: Verify user has write access to existing record's company
            // ═══════════════════════════════════════════════════════════════
            if (oldValue.OwnerCompanyId.HasValue)
            {
                var canWrite = await _perimeterProvider.CanWriteToCompanyAsync(oldValue.OwnerCompanyId.Value);
                if (!canWrite)
                {
                    _logger.LogWarning(
                        "ACCESS DENIED: User {UserId} attempted to update Persoana {PersoanaId} owned by company {CompanyId}",
                        _currentUserService.UserId, persoana.Id, oldValue.OwnerCompanyId.Value);
                    throw new UnauthorizedAccessException(
                        "Nu aveți permisiune de scriere pentru această persoană.");
                }
            }
            
            // If changing ownership to a different company, verify access to new company too
            if (persoana.OwnerCompanyId.HasValue && persoana.OwnerCompanyId != oldValue.OwnerCompanyId)
            {
                var canWriteNew = await _perimeterProvider.CanWriteToCompanyAsync(persoana.OwnerCompanyId.Value);
                if (!canWriteNew)
                {
                    _logger.LogWarning(
                        "ACCESS DENIED: User {UserId} attempted to transfer Persoana {PersoanaId} to company {CompanyId}",
                        _currentUserService.UserId, persoana.Id, persoana.OwnerCompanyId.Value);
                    throw new UnauthorizedAccessException(
                        "Nu aveți permisiune de scriere pentru compania destinație.");
                }
            }
            
            // Get current user ID for UpdatedBy (use security service)
            persoana.UpdatedBy = _currentUserService.UserId != Guid.Empty 
                ? _currentUserService.UserId 
                : await GetCurrentUserIdAsync();
            
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
                    persoana.IsActive,
                    // Ownership columns
                    persoana.OwnerCompanyId,
                    persoana.OwnerWorkPlaceId,
                    persoana.OwnerLocationId,
                    persoana.UpdatedBy
                },
                commandType: CommandType.StoredProcedure);
            
            // Auto-audit: Log Update operation with diff
            await LogAuditAsync("Update", persoana.Id.ToString(), persoana, oldValue);
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (KeyNotFoundException)
        {
            throw;
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
            // Get entity before delete for audit AND security check
            var deletedValue = await GetByIdAsync(id);
            
            if (deletedValue == null)
            {
                throw new KeyNotFoundException($"Persoana cu Id={id} nu a fost găsită.");
            }
            
            // ═══════════════════════════════════════════════════════════════
            // SECURITY CHECK: Verify user has write access to record's company
            // ═══════════════════════════════════════════════════════════════
            if (deletedValue.OwnerCompanyId.HasValue)
            {
                var canWrite = await _perimeterProvider.CanWriteToCompanyAsync(deletedValue.OwnerCompanyId.Value);
                if (!canWrite)
                {
                    _logger.LogWarning(
                        "ACCESS DENIED: User {UserId} attempted to delete Persoana {PersoanaId} owned by company {CompanyId}",
                        _currentUserService.UserId, id, deletedValue.OwnerCompanyId.Value);
                    throw new UnauthorizedAccessException(
                        "Nu aveți permisiune de ștergere pentru această persoană.");
                }
            }
            
            using var connection = _context.CreateConnection();
            // Soft delete via stored procedure
            await connection.ExecuteAsync(
                "sp_Persoane_Delete",
                new { Id = id },
                commandType: CommandType.StoredProcedure);
            
            // Auto-audit: Log Delete operation
            await LogAuditAsync("Delete", id.ToString(), deletedValue);
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (KeyNotFoundException)
        {
            throw;
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

    /// <summary>
    /// Returns a total count of active Persoane (applies basic IsActive filter).
    /// </summary>
    public async Task<int> GetTotalCountAsync()
    {
        try
        {
            using var connection = _context.CreateConnection();
            var sql = "SELECT COUNT(*) FROM Persoane WHERE IsActive = 1";
            var count = await connection.ExecuteScalarAsync<int>(sql);
            return count;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL error in GetTotalCountAsync");
            throw new InvalidOperationException($"Database error in GetTotalCountAsync: {ex.Message}", ex);
        }
    }

    public async Task<IEnumerable<Persoana>> GetAllSimpleAsync()
    {
        try
        {
            using var connection = _context.CreateConnection();
            // Now returns NumeComplet calculated field and limits to 1000 records
            var result = await connection.QueryAsync<Persoana>(
                "sp_Persoane_GetAllSimple",
                commandType: CommandType.StoredProcedure);
            
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
