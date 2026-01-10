using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ValyanERP.Web.Features.Infrastructure.Audit.Models;
using ValyanERP.Web.Features.Infrastructure.Audit.Repositories;

namespace ValyanERP.Web.Features.Infrastructure.Audit.Services;

/// <summary>
/// Service implementation for audit logging with automatic field-level diff and sensitive data masking.
/// </summary>
public class AuditService : IAuditService
{
    private readonly IAuditRepository _repository;
    private readonly SensitiveDataMasker _masker;
    private readonly ILogger<AuditService> _logger;

    public AuditService(
        IAuditRepository repository,
        SensitiveDataMasker masker,
        ILogger<AuditService> logger)
    {
        _repository = repository;
        _masker = masker;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Guid> LogCreateAsync<T>(
        string entityType,
        string entityId,
        T newValue,
        Guid userId,
        Guid? sessionId = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? notes = null) where T : class
    {
        try
        {
            var entry = new AuditLogEntry
            {
                EntityType = entityType,
                EntityId = entityId,
                TableName = entityType, // Can be customized if needed
                OperationType = "Create",
                UserId = userId,
                UserEmail = string.Empty, // Will be populated if available
                SessionId = sessionId,
                IPAddress = ipAddress,
                UserAgent = userAgent,
                Notes = notes,
                ChangedFields = ExtractFieldsForCreate(newValue)
            };

            return await _repository.CreateAsync(entry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging Create operation for {EntityType} {EntityId}", entityType, entityId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Guid> LogUpdateAsync<T>(
        string entityType,
        string entityId,
        T oldValue,
        T newValue,
        Guid userId,
        Guid? sessionId = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? notes = null) where T : class
    {
        try
        {
            var entry = new AuditLogEntry
            {
                EntityType = entityType,
                EntityId = entityId,
                TableName = entityType,
                OperationType = "Update",
                UserId = userId,
                UserEmail = string.Empty,
                SessionId = sessionId,
                IPAddress = ipAddress,
                UserAgent = userAgent,
                Notes = notes,
                ChangedFields = CompareAndExtractChanges(oldValue, newValue)
            };

            // Only log if there are actual changes
            if (entry.ChangedFields.Count == 0)
            {
                return Guid.Empty;
            }

            return await _repository.CreateAsync(entry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging Update operation for {EntityType} {EntityId}", entityType, entityId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Guid> LogDeleteAsync<T>(
        string entityType,
        string entityId,
        T deletedValue,
        Guid userId,
        Guid? sessionId = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? notes = null) where T : class
    {
        try
        {
            var entry = new AuditLogEntry
            {
                EntityType = entityType,
                EntityId = entityId,
                TableName = entityType,
                OperationType = "Delete",
                UserId = userId,
                UserEmail = string.Empty,
                SessionId = sessionId,
                IPAddress = ipAddress,
                UserAgent = userAgent,
                Notes = notes,
                ChangedFields = ExtractFieldsForDelete(deletedValue)
            };

            return await _repository.CreateAsync(entry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging Delete operation for {EntityType} {EntityId}", entityType, entityId);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<PagedAuditResult<AuditLog>> GetByEntityAsync(string entityType, string entityId, int pageNumber = 1, int pageSize = 50)
        => _repository.GetByEntityAsync(entityType, entityId, pageNumber, pageSize);

    /// <inheritdoc />
    public Task<PagedAuditResult<AuditLog>> GetByUserAsync(Guid userId, DateTime? startDate = null, DateTime? endDate = null, int pageNumber = 1, int pageSize = 50)
        => _repository.GetByUserAsync(userId, startDate, endDate, pageNumber, pageSize);

    /// <inheritdoc />
    public Task<IEnumerable<AuditLog>> GetBySessionAsync(Guid sessionId)
        => _repository.GetBySessionAsync(sessionId);

    /// <inheritdoc />
    public Task<AuditLog?> GetByIdAsync(Guid id)
        => _repository.GetByIdAsync(id);

    /// <inheritdoc />
    public Task<AuditStatistics> GetStatisticsAsync(DateTime? startDate = null, DateTime? endDate = null)
        => _repository.GetStatisticsAsync(startDate, endDate);

    /// <inheritdoc />
    public Task<PagedAuditResult<AuditLog>> SearchAsync(
        string? entityType = null,
        Guid? userId = null,
        string? operationType = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? searchTerm = null,
        int pageNumber = 1,
        int pageSize = 50)
        => _repository.SearchAsync(entityType, userId, operationType, startDate, endDate, searchTerm, pageNumber, pageSize);

    /// <inheritdoc />
    public Task<PagedAuditResult<AuditLog>> GetUserOwnActivityAsync(Guid userId, DateTime? startDate = null, DateTime? endDate = null, int pageNumber = 1, int pageSize = 50)
        => _repository.GetUserOwnActivityAsync(userId, startDate, endDate, pageNumber, pageSize);

    /// <inheritdoc />
    public Task<int> DeleteOldLogsAsync(int retentionDays)
        => _repository.DeleteOldLogsAsync(retentionDays);

    // Private helper methods

    /// <summary>
    /// Extracts all properties for a Create operation.
    /// </summary>
    private List<AuditFieldChange> ExtractFieldsForCreate<T>(T newValue) where T : class
    {
        var changes = new List<AuditFieldChange>();
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            if (!prop.CanRead)
                continue;

            var propValue = prop.GetValue(newValue);
            var valueString = ConvertToString(propValue);
            var isSensitive = _masker.IsSensitiveField(prop.Name);

            changes.Add(new AuditFieldChange
            {
                FieldName = prop.Name,
                OldValue = null, // No old value for Create
                NewValue = isSensitive ? _masker.MaskValue(prop.Name, valueString) : valueString,
                DataType = GetDataTypeName(prop.PropertyType),
                IsSensitive = isSensitive
            });
        }

        return changes;
    }

    /// <summary>
    /// Extracts all properties for a Delete operation.
    /// </summary>
    private List<AuditFieldChange> ExtractFieldsForDelete<T>(T deletedValue) where T : class
    {
        var changes = new List<AuditFieldChange>();
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            if (!prop.CanRead)
                continue;

            var propValue = prop.GetValue(deletedValue);
            var valueString = ConvertToString(propValue);
            var isSensitive = _masker.IsSensitiveField(prop.Name);

            changes.Add(new AuditFieldChange
            {
                FieldName = prop.Name,
                OldValue = isSensitive ? _masker.MaskValue(prop.Name, valueString) : valueString,
                NewValue = null, // No new value for Delete
                DataType = GetDataTypeName(prop.PropertyType),
                IsSensitive = isSensitive
            });
        }

        return changes;
    }

    /// <summary>
    /// Compares old and new values and extracts only changed fields.
    /// </summary>
    private List<AuditFieldChange> CompareAndExtractChanges<T>(T oldValue, T newValue) where T : class
    {
        var changes = new List<AuditFieldChange>();
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            if (!prop.CanRead)
                continue;

            var oldPropValue = prop.GetValue(oldValue);
            var newPropValue = prop.GetValue(newValue);

            // Check if values are different
            if (AreValuesEqual(oldPropValue, newPropValue))
                continue;

            var oldString = ConvertToString(oldPropValue);
            var newString = ConvertToString(newPropValue);
            var isSensitive = _masker.IsSensitiveField(prop.Name);

            changes.Add(new AuditFieldChange
            {
                FieldName = prop.Name,
                OldValue = isSensitive ? _masker.MaskValue(prop.Name, oldString) : oldString,
                NewValue = isSensitive ? _masker.MaskValue(prop.Name, newString) : newString,
                DataType = GetDataTypeName(prop.PropertyType),
                IsSensitive = isSensitive
            });
        }

        return changes;
    }

    /// <summary>
    /// Compares two property values for equality.
    /// </summary>
    private bool AreValuesEqual(object? oldValue, object? newValue)
    {
        if (oldValue == null && newValue == null)
            return true;

        if (oldValue == null || newValue == null)
            return false;

        return oldValue.Equals(newValue);
    }

    /// <summary>
    /// Converts a property value to string for storage.
    /// </summary>
    private string? ConvertToString(object? value)
    {
        if (value == null)
            return null;

        if (value is DateTime dateTime)
            return dateTime.ToString("yyyy-MM-dd HH:mm:ss");

        if (value is bool boolValue)
            return boolValue.ToString().ToLowerInvariant();

        return value.ToString();
    }

    /// <summary>
    /// Gets a friendly data type name for storage.
    /// </summary>
    private string GetDataTypeName(Type type)
    {
        // Handle nullable types
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        if (underlyingType == typeof(string))
            return "string";
        if (underlyingType == typeof(int) || underlyingType == typeof(long) || underlyingType == typeof(short))
            return "int";
        if (underlyingType == typeof(decimal) || underlyingType == typeof(double) || underlyingType == typeof(float))
            return "decimal";
        if (underlyingType == typeof(bool))
            return "bool";
        if (underlyingType == typeof(DateTime))
            return "datetime";
        if (underlyingType == typeof(Guid))
            return "guid";

        return "string"; // Default
    }
}
