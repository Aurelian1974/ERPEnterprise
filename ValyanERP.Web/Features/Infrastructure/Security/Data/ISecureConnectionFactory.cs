using System.Data;

namespace ValyanERP.Web.Features.Infrastructure.Security.Data;

/// <summary>
/// Factory for creating database connections with security context.
/// Supports both secure connections (with SESSION_CONTEXT) and system connections.
/// </summary>
public interface ISecureConnectionFactory
{
    /// <summary>
    /// Creates a database connection with SESSION_CONTEXT set for the current user.
    /// The connection will have UserId and IsAdmin set in SQL Server's SESSION_CONTEXT,
    /// allowing filtered views to automatically apply security filters.
    /// </summary>
    /// <returns>An open database connection with security context.</returns>
    Task<IDbConnection> CreateSecureConnectionAsync();
    
    /// <summary>
    /// Creates a database connection without security context.
    /// Use this for system operations that should bypass security filters.
    /// ⚠️ WARNING: Only use for administrative or system-level operations!
    /// </summary>
    /// <returns>An open database connection without security context.</returns>
    IDbConnection CreateSystemConnection();
    
    /// <summary>
    /// Creates a secure connection for a specific user (not the current user).
    /// Useful for background jobs or impersonation scenarios.
    /// </summary>
    /// <param name="userId">The user ID to set in SESSION_CONTEXT.</param>
    /// <param name="isAdmin">Whether the user has admin privileges.</param>
    /// <returns>An open database connection with the specified user's context.</returns>
    Task<IDbConnection> CreateSecureConnectionForUserAsync(Guid userId, bool isAdmin);
}
