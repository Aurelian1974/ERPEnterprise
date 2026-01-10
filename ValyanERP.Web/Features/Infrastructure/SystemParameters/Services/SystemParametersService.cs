// ================================================================
// FILE: SystemParametersService.cs
// PURPOSE: Service implementation with caching for parameters
// ARCHITECTURE: Service Layer with IMemoryCache optimization
// ================================================================

using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ValyanERP.Web.Features.Infrastructure.SystemParameters.Models;
using ValyanERP.Web.Features.Infrastructure.SystemParameters.Repositories;

namespace ValyanERP.Web.Features.Infrastructure.SystemParameters.Services;

/// <summary>
/// Service implementation for system parameters with intelligent caching.
/// </summary>
/// <remarks>
/// Cache Strategy:
/// - Parameters cached indefinitely (rarely change)
/// - Manual invalidation on CUD operations
/// - Individual parameter caching (not bulk cache)
/// - Default fallback values for missing/invalid parameters
/// </remarks>
public class SystemParametersService : ISystemParametersService
{
    private readonly ISystemParametersRepository _repository;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SystemParametersService> _logger;
    
    // Cache key prefix for individual parameters
    private const string CACHE_KEY_PREFIX = "SystemParameter_";
    
    // Cache options: never expire (manual invalidation only)
    private static readonly MemoryCacheEntryOptions CacheOptions = new MemoryCacheEntryOptions
    {
        Priority = CacheItemPriority.High,
        Size = 1 // Each entry counts as 1 unit toward SizeLimit
    };

    public SystemParametersService(
        ISystemParametersRepository repository,
        IMemoryCache cache,
        ILogger<SystemParametersService> logger)
    {
        _repository = repository;
        _cache = cache;
        _logger = logger;
    }

    // ================================================================
    // Type-Safe Parameter Accessors
    // ================================================================

    /// <inheritdoc />
    public async Task<int> GetIntAsync(string parameterKey, int defaultValue = 0)
    {
        try
        {
            var parameter = await GetParameterWithCacheAsync(parameterKey);
            
            if (parameter == null)
            {
                _logger.LogWarning("Parameter not found, using default: {ParameterKey} = {DefaultValue}",
                    parameterKey, defaultValue);
                return defaultValue;
            }

            if (int.TryParse(parameter.ParameterValue, out var result))
            {
                return result;
            }

            _logger.LogWarning(
                "Invalid int value for {ParameterKey}: '{Value}', using default: {DefaultValue}",
                parameterKey, parameter.ParameterValue, defaultValue);
            
            return defaultValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Error getting int parameter {ParameterKey}, using default: {DefaultValue}",
                parameterKey, defaultValue);
            return defaultValue;
        }
    }

    /// <inheritdoc />
    public async Task<string> GetStringAsync(string parameterKey, string defaultValue = "")
    {
        try
        {
            var parameter = await GetParameterWithCacheAsync(parameterKey);
            
            if (parameter == null || string.IsNullOrWhiteSpace(parameter.ParameterValue))
            {
                _logger.LogWarning("Parameter not found or empty, using default: {ParameterKey} = '{DefaultValue}'",
                    parameterKey, defaultValue);
                return defaultValue;
            }

            return parameter.ParameterValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Error getting string parameter {ParameterKey}, using default: '{DefaultValue}'",
                parameterKey, defaultValue);
            return defaultValue;
        }
    }

    /// <inheritdoc />
    public async Task<bool> GetBoolAsync(string parameterKey, bool defaultValue = false)
    {
        try
        {
            var parameter = await GetParameterWithCacheAsync(parameterKey);
            
            if (parameter == null)
            {
                _logger.LogWarning("Parameter not found, using default: {ParameterKey} = {DefaultValue}",
                    parameterKey, defaultValue);
                return defaultValue;
            }

            // Accept: true/false, 1/0, yes/no
            var normalizedValue = parameter.ParameterValue.Trim().ToLowerInvariant();
            
            if (normalizedValue == "true" || normalizedValue == "1" || normalizedValue == "yes")
                return true;
            
            if (normalizedValue == "false" || normalizedValue == "0" || normalizedValue == "no")
                return false;

            _logger.LogWarning(
                "Invalid bool value for {ParameterKey}: '{Value}', using default: {DefaultValue}",
                parameterKey, parameter.ParameterValue, defaultValue);
            
            return defaultValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Error getting bool parameter {ParameterKey}, using default: {DefaultValue}",
                parameterKey, defaultValue);
            return defaultValue;
        }
    }

    /// <inheritdoc />
    public async Task<decimal> GetDecimalAsync(string parameterKey, decimal defaultValue = 0m)
    {
        try
        {
            var parameter = await GetParameterWithCacheAsync(parameterKey);
            
            if (parameter == null)
            {
                _logger.LogWarning("Parameter not found, using default: {ParameterKey} = {DefaultValue}",
                    parameterKey, defaultValue);
                return defaultValue;
            }

            if (decimal.TryParse(parameter.ParameterValue, out var result))
            {
                return result;
            }

            _logger.LogWarning(
                "Invalid decimal value for {ParameterKey}: '{Value}', using default: {DefaultValue}",
                parameterKey, parameter.ParameterValue, defaultValue);
            
            return defaultValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Error getting decimal parameter {ParameterKey}, using default: {DefaultValue}",
                parameterKey, defaultValue);
            return defaultValue;
        }
    }

    /// <inheritdoc />
    public async Task<T?> GetJsonAsync<T>(string parameterKey, T? defaultValue = default)
    {
        try
        {
            var parameter = await GetParameterWithCacheAsync(parameterKey);
            
            if (parameter == null || string.IsNullOrWhiteSpace(parameter.ParameterValue))
            {
                _logger.LogWarning("Parameter not found or empty, using default: {ParameterKey}",
                    parameterKey);
                return defaultValue;
            }

            var result = JsonSerializer.Deserialize<T>(parameter.ParameterValue);
            return result;
        }
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx, 
                "Invalid JSON for parameter {ParameterKey}, using default",
                parameterKey);
            return defaultValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Error getting JSON parameter {ParameterKey}, using default",
                parameterKey);
            return defaultValue;
        }
    }

    // ================================================================
    // CRUD Operations (Admin UI)
    // ================================================================

    /// <inheritdoc />
    public async Task<IEnumerable<SystemParameter>> GetAllAsync(
        string? category = null,
        string? subCategory = null,
        bool includeReadOnly = true)
    {
        return await _repository.GetAllAsync(category, subCategory, includeReadOnly);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<SystemParameter>> GetByCategoryAsync(string category)
    {
        return await _repository.GetByCategoryAsync(category);
    }

    /// <inheritdoc />
    public async Task<Guid> CreateAsync(SystemParameter parameter, Guid? createdBy = null)
    {
        ValidateParameter(parameter);
        
        var id = await _repository.CreateAsync(parameter, createdBy);
        
        // Clear cache for this parameter
        ClearParameterCache(parameter.ParameterKey);
        
        _logger.LogInformation(
            "Created and invalidated cache for parameter: {ParameterKey}",
            parameter.ParameterKey);
        
        return id;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(SystemParameter parameter, Guid? updatedBy = null)
    {
        ValidateParameter(parameter);
        
        var success = await _repository.UpdateAsync(parameter, updatedBy);
        
        if (success)
        {
            // Clear cache for this parameter
            ClearParameterCache(parameter.ParameterKey);
            
            _logger.LogInformation(
                "Updated and invalidated cache for parameter: {ParameterKey}",
                parameter.ParameterKey);
        }
        
        return success;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, Guid? updatedBy = null)
    {
        // Need to get parameter first to know its key for cache invalidation
        var allParameters = await _repository.GetAllAsync();
        var parameter = allParameters.FirstOrDefault(p => p.Id == id);
        
        var success = await _repository.DeleteAsync(id, updatedBy);
        
        if (success && parameter != null)
        {
            // Clear cache for this parameter
            ClearParameterCache(parameter.ParameterKey);
            
            _logger.LogInformation(
                "Deleted and invalidated cache for parameter: {ParameterKey}",
                parameter.ParameterKey);
        }
        
        return success;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<(string Category, int ParameterCount)>> GetCategoriesAsync()
    {
        return await _repository.GetCategoriesAsync();
    }

    // ================================================================
    // Cache Management
    // ================================================================

    /// <inheritdoc />
    public void ClearCache()
    {
        // Note: IMemoryCache doesn't provide a way to enumerate keys
        // We'd need to track keys separately if we want bulk clear
        // For now, cache is cleared per-parameter on updates
        
        _logger.LogInformation("Cache clear requested (per-parameter invalidation active)");
    }

    // ================================================================
    // Private Helpers
    // ================================================================

    /// <summary>
    /// Gets a parameter with caching (individual parameter cache).
    /// </summary>
    private async Task<SystemParameter?> GetParameterWithCacheAsync(string parameterKey)
    {
        var cacheKey = $"{CACHE_KEY_PREFIX}{parameterKey}";
        
        // Try get from cache
        if (_cache.TryGetValue(cacheKey, out SystemParameter? cached))
        {
            _logger.LogDebug("Cache HIT for parameter: {ParameterKey}", parameterKey);
            return cached;
        }
        
        // Cache miss - load from database
        _logger.LogDebug("Cache MISS for parameter: {ParameterKey}", parameterKey);
        
        var parameter = await _repository.GetByKeyAsync(parameterKey);
        
        if (parameter != null)
        {
            // Cache the parameter (never expires unless manually cleared)
            _cache.Set(cacheKey, parameter, CacheOptions);
            
            _logger.LogDebug("Cached parameter: {ParameterKey} = {Value}",
                parameterKey, parameter.ParameterValue);
        }
        
        return parameter;
    }

    /// <summary>
    /// Clears cache for a specific parameter.
    /// </summary>
    private void ClearParameterCache(string parameterKey)
    {
        var cacheKey = $"{CACHE_KEY_PREFIX}{parameterKey}";
        _cache.Remove(cacheKey);
        
        _logger.LogDebug("Cleared cache for parameter: {ParameterKey}", parameterKey);
    }

    /// <summary>
    /// Validates parameter business rules.
    /// </summary>
    private void ValidateParameter(SystemParameter parameter)
    {
        if (string.IsNullOrWhiteSpace(parameter.ParameterKey))
            throw new ArgumentException("ParameterKey is required", nameof(parameter));
        
        if (string.IsNullOrWhiteSpace(parameter.Category))
            throw new ArgumentException("Category is required", nameof(parameter));
        
        if (string.IsNullOrWhiteSpace(parameter.ParameterValue))
            throw new ArgumentException("ParameterValue is required", nameof(parameter));
        
        if (string.IsNullOrWhiteSpace(parameter.DataType))
            throw new ArgumentException("DataType is required", nameof(parameter));
        
        // Validate DataType enum
        var validDataTypes = new[] { "int", "string", "bool", "decimal", "json", "enum" };
        if (!validDataTypes.Contains(parameter.DataType.ToLowerInvariant()))
        {
            throw new ArgumentException(
                $"Invalid DataType: {parameter.DataType}. Must be one of: {string.Join(", ", validDataTypes)}",
                nameof(parameter));
        }
        
        // Validate numeric constraints
        if (parameter.MinValue.HasValue && parameter.MaxValue.HasValue)
        {
            if (parameter.MinValue.Value > parameter.MaxValue.Value)
            {
                throw new ArgumentException(
                    $"MinValue ({parameter.MinValue}) cannot be greater than MaxValue ({parameter.MaxValue})",
                    nameof(parameter));
            }
        }
    }
}
