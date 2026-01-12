// ========================================================================
// SecuredRepositoryBase.cs - Abstract base class for security-aware repositories
// Features/Infrastructure/Security/Data
// Provides automatic security filtering based on user's organizational perimeter
// ========================================================================

using System.Data;
using Dapper;
using ValyanERP.Web.Features.Infrastructure.Security.Services;

namespace ValyanERP.Web.Features.Infrastructure.Security.Data;

/// <summary>
/// Abstract base class for repositories that require organizational security filtering.
/// Provides automatic filtering based on user's perimeter and access level verification.
/// </summary>
/// <typeparam name="T">The entity type this repository manages.</typeparam>
public abstract class SecuredRepositoryBase<T> where T : class
{
    protected readonly ISecureConnectionFactory _connectionFactory;
    protected readonly IUserPerimeterProvider _perimeterProvider;
    protected readonly ICurrentUserService _currentUserService;
    protected readonly ILogger _logger;

    protected SecuredRepositoryBase(
        ISecureConnectionFactory connectionFactory,
        IUserPerimeterProvider perimeterProvider,
        ICurrentUserService currentUserService,
        ILogger logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _perimeterProvider = perimeterProvider ?? throw new ArgumentNullException(nameof(perimeterProvider));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region Abstract Properties (to be implemented by derived classes)

    /// <summary>
    /// The database table name for this entity.
    /// </summary>
    protected abstract string TableName { get; }

    #endregion

    #region Virtual Properties (can be overridden)

    /// <summary>
    /// The filtered view name that applies security automatically via SESSION_CONTEXT.
    /// If null, manual filtering will be applied using SecurityFilter.
    /// </summary>
    protected virtual string? FilteredViewName => null;

    /// <summary>
    /// The column name that holds the owning company ID.
    /// Default: "OwnerCompanyId"
    /// </summary>
    protected virtual string OwnershipColumn => "OwnerCompanyId";

    /// <summary>
    /// SQL WHERE clause for manual security filtering.
    /// Used when FilteredViewName is not set.
    /// </summary>
    protected virtual string SecurityFilter => 
        $"({OwnershipColumn} IN @VisibleCompanyIds OR {OwnershipColumn} IS NULL OR CreatedBy = @CurrentUserId)";

    #endregion

    #region Protected Helper Methods

    /// <summary>
    /// Gets all entities visible to the current user.
    /// Uses filtered view if available, otherwise applies manual security filter.
    /// </summary>
    protected virtual async Task<IEnumerable<T>> GetAllSecuredAsync()
    {
        var perimeter = await _perimeterProvider.GetPerimeterAsync();

        // Admin users can see everything
        if (perimeter.HasFullAccess)
        {
            using var db = _connectionFactory.CreateSystemConnection();
            var source = !string.IsNullOrEmpty(FilteredViewName) ? FilteredViewName : TableName;
            return await db.QueryAsync<T>($"SELECT * FROM {source} WHERE IsActive = 1");
        }

        // Use filtered view with SESSION_CONTEXT if available
        if (!string.IsNullOrEmpty(FilteredViewName))
        {
            using var db = await _connectionFactory.CreateSecureConnectionAsync();
            return await db.QueryAsync<T>($"SELECT * FROM {FilteredViewName}");
        }

        // Manual filtering using visible company IDs
        using (var db = _connectionFactory.CreateSystemConnection())
        {
            var sql = $@"
                SELECT * FROM {TableName}
                WHERE IsActive = 1 AND {SecurityFilter}";

            return await db.QueryAsync<T>(sql, new
            {
                VisibleCompanyIds = perimeter.VisibleCompanyIds.ToList(),
                CurrentUserId = _currentUserService.UserId
            });
        }
    }

    /// <summary>
    /// Gets a single entity by ID with security check.
    /// Returns null if entity doesn't exist or user doesn't have access.
    /// </summary>
    protected virtual async Task<T?> GetByIdSecuredAsync(Guid id)
    {
        var perimeter = await _perimeterProvider.GetPerimeterAsync();

        // Admin users can access everything
        if (perimeter.HasFullAccess)
        {
            using var db = _connectionFactory.CreateSystemConnection();
            return await db.QueryFirstOrDefaultAsync<T>(
                $"SELECT * FROM {TableName} WHERE Id = @Id AND IsActive = 1",
                new { Id = id });
        }

        // Use filtered view with SESSION_CONTEXT if available
        if (!string.IsNullOrEmpty(FilteredViewName))
        {
            using var db = await _connectionFactory.CreateSecureConnectionAsync();
            return await db.QueryFirstOrDefaultAsync<T>(
                $"SELECT * FROM {FilteredViewName} WHERE Id = @Id",
                new { Id = id });
        }

        // Manual filtering
        using (var db = _connectionFactory.CreateSystemConnection())
        {
            var sql = $@"
                SELECT * FROM {TableName}
                WHERE Id = @Id AND IsActive = 1 AND {SecurityFilter}";

            return await db.QueryFirstOrDefaultAsync<T>(sql, new
            {
                Id = id,
                VisibleCompanyIds = perimeter.VisibleCompanyIds.ToList(),
                CurrentUserId = _currentUserService.UserId
            });
        }
    }

    /// <summary>
    /// Ensures the current user has write access to the specified company.
    /// Throws UnauthorizedAccessException if access is denied.
    /// </summary>
    /// <param name="companyId">The company ID to check write access for.</param>
    protected async Task EnsureWriteAccessAsync(Guid companyId)
    {
        var canWrite = await _perimeterProvider.CanWriteToCompanyAsync(companyId);

        if (!canWrite)
        {
            _logger.LogWarning(
                "Access denied: User {UserId} attempted write on company {CompanyId}",
                _currentUserService.UserId, companyId);

            throw new UnauthorizedAccessException(
                "Nu aveți permisiune de scriere pentru această entitate.");
        }
    }

    /// <summary>
    /// Ensures the current user has write access to the specified location.
    /// Throws UnauthorizedAccessException if access is denied.
    /// </summary>
    /// <param name="locationId">The location ID to check write access for.</param>
    protected async Task EnsureWriteAccessToLocationAsync(Guid locationId)
    {
        var canWrite = await _perimeterProvider.CanWriteToLocationAsync(locationId);

        if (!canWrite)
        {
            _logger.LogWarning(
                "Access denied: User {UserId} attempted write on location {LocationId}",
                _currentUserService.UserId, locationId);

            throw new UnauthorizedAccessException(
                "Nu aveți permisiune de scriere pentru această locație.");
        }
    }

    /// <summary>
    /// Ensures the current user has read access to the specified entity.
    /// Throws UnauthorizedAccessException if access is denied.
    /// </summary>
    /// <param name="entityType">Entity type: GROUP, COMPANY, WORKPLACE, LOCATION</param>
    /// <param name="entityId">The entity ID to check.</param>
    protected async Task EnsureReadAccessAsync(string entityType, Guid entityId)
    {
        var canRead = await _perimeterProvider.CanAccessAsync(entityType, entityId, 1);

        if (!canRead)
        {
            _logger.LogWarning(
                "Access denied: User {UserId} attempted read on {EntityType} {EntityId}",
                _currentUserService.UserId, entityType, entityId);

            throw new UnauthorizedAccessException(
                "Nu aveți permisiune de citire pentru această entitate.");
        }
    }

    /// <summary>
    /// Gets the current user's ID.
    /// </summary>
    protected Guid CurrentUserId => _currentUserService.UserId;

    /// <summary>
    /// Checks if the current user has admin privileges.
    /// </summary>
    protected async Task<bool> IsCurrentUserAdminAsync()
    {
        var perimeter = await _perimeterProvider.GetPerimeterAsync();
        return perimeter.HasFullAccess;
    }

    /// <summary>
    /// Logs an access denied event and optionally records it to database.
    /// </summary>
    protected async Task LogAccessDeniedAsync(
        string entityType, 
        Guid entityId, 
        string requestedAction,
        string? additionalInfo = null)
    {
        _logger.LogWarning(
            "ACCESS DENIED: User {UserId} attempted {Action} on {EntityType} {EntityId}. Info: {Info}",
            _currentUserService.UserId, requestedAction, entityType, entityId, additionalInfo);

        // Optionally log to database
        try
        {
            using var db = _connectionFactory.CreateSystemConnection();
            await db.ExecuteAsync(@"
                INSERT INTO AccessDeniedLog 
                    (UserId, AttemptedEntityType, AttemptedEntityId, RequestedAction, AdditionalInfo)
                VALUES 
                    (@UserId, @EntityType, @EntityId, @Action, @Info)",
                new
                {
                    UserId = _currentUserService.UserId,
                    EntityType = entityType,
                    EntityId = entityId,
                    Action = requestedAction,
                    Info = additionalInfo
                });
        }
        catch (Exception ex)
        {
            // Don't fail the operation if logging fails
            _logger.LogError(ex, "Failed to log access denied event to database");
        }
    }

    #endregion

    #region Helper Methods for Filtering Parameters

    /// <summary>
    /// Creates a dynamic parameters object with security context.
    /// Includes VisibleCompanyIds and CurrentUserId for manual filtering.
    /// </summary>
    protected async Task<DynamicParameters> CreateSecurityParametersAsync(object? additionalParams = null)
    {
        var perimeter = await _perimeterProvider.GetPerimeterAsync();
        var parameters = new DynamicParameters(additionalParams);

        parameters.Add("@CurrentUserId", _currentUserService.UserId);
        parameters.Add("@IsAdmin", perimeter.HasFullAccess);
        
        // For manual filtering, add visible IDs
        if (!perimeter.HasFullAccess)
        {
            parameters.Add("@VisibleCompanyIds", perimeter.VisibleCompanyIds.ToList());
            parameters.Add("@VisibleLocationIds", perimeter.VisibleLocationIds.ToList());
        }

        return parameters;
    }

    #endregion
}
