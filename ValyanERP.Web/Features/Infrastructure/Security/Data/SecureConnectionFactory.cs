using System.Data;
using Microsoft.Data.SqlClient;
using Dapper;
using ValyanERP.Web.Features.Infrastructure.Security.Services;

namespace ValyanERP.Web.Features.Infrastructure.Security.Data;

/// <summary>
/// Implementation of ISecureConnectionFactory.
/// Creates SQL Server connections with SESSION_CONTEXT for automatic security filtering.
/// </summary>
public class SecureConnectionFactory : ISecureConnectionFactory
{
    private readonly IConfiguration _configuration;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<SecureConnectionFactory> _logger;
    private readonly string _connectionString;
    
    public SecureConnectionFactory(
        IConfiguration configuration,
        ICurrentUserService currentUserService,
        ILogger<SecureConnectionFactory> logger)
    {
        _configuration = configuration;
        _currentUserService = currentUserService;
        _logger = logger;
        _connectionString = _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    }
    
    /// <inheritdoc />
    public async Task<IDbConnection> CreateSecureConnectionAsync()
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        
        try
        {
            var userId = _currentUserService.GetUserIdOrNull();
            if (!userId.HasValue)
            {
                _logger.LogWarning("Creating secure connection without authenticated user");
                return connection;
            }
            
            var isAdmin = await _currentUserService.IsInRoleAsync("Admin");
            
            // Set SESSION_CONTEXT for automatic filtering in views
            await connection.ExecuteAsync(@"
                EXEC sp_set_session_context @key = N'UserId', @value = @UserId;
                EXEC sp_set_session_context @key = N'IsAdmin', @value = @IsAdmin;",
                new { UserId = userId.Value, IsAdmin = isAdmin ? 1 : 0 });
            
            _logger.LogDebug(
                "Secure connection created for user {UserId}, IsAdmin: {IsAdmin}",
                userId.Value, isAdmin);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set SESSION_CONTEXT for secure connection");
            // Don't dispose - let the caller handle the connection
            // The connection is still valid, just without security context
        }
        
        return connection;
    }
    
    /// <inheritdoc />
    public IDbConnection CreateSystemConnection()
    {
        var connection = new SqlConnection(_connectionString);
        connection.Open();
        
        _logger.LogDebug("System connection created (no security context)");
        
        return connection;
    }
    
    /// <inheritdoc />
    public async Task<IDbConnection> CreateSecureConnectionForUserAsync(Guid userId, bool isAdmin)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        
        try
        {
            await connection.ExecuteAsync(@"
                EXEC sp_set_session_context @key = N'UserId', @value = @UserId;
                EXEC sp_set_session_context @key = N'IsAdmin', @value = @IsAdmin;",
                new { UserId = userId, IsAdmin = isAdmin ? 1 : 0 });
            
            _logger.LogDebug(
                "Secure connection created for specific user {UserId}, IsAdmin: {IsAdmin}",
                userId, isAdmin);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set SESSION_CONTEXT for user {UserId}", userId);
            // Don't dispose - let the caller handle
        }
        
        return connection;
    }
}
