using Microsoft.Extensions.Logging;
using Syncfusion.Blazor;
using Syncfusion.Blazor.Data;
using ValyanERP.Web.Features.Administrare.Utilizatori.Models;
using ValyanERP.Web.Features.Administrare.Utilizatori.Repositories;
using ValyanERP.Web.Features.Administrare.Utilizatori.Services;
using ValyanERP.Web.Features.Infrastructure.DataGrid;

namespace ValyanERP.Web.Features.Administrare.Utilizatori;

/// <summary>
/// Custom Syncfusion DataAdaptor for Users grid.
/// Handles server-side paging, sorting, filtering, grouping, and CRUD operations.
/// </summary>
public class UsersAdaptor : DataAdaptor
{
    private readonly IUsersRepository _repository;
    private readonly IUsersService _service;
    private readonly IDataGridOperationsService _gridOperations;
    private readonly ILogger<UsersAdaptor> _logger;

    public UsersAdaptor(
        IUsersRepository repository, 
        IUsersService service,
        IDataGridOperationsService gridOperations,
        ILogger<UsersAdaptor> logger)
    {
        _repository = repository;
        _service = service;
        _gridOperations = gridOperations;
        _logger = logger;
    }

    /// <summary>
    /// Reads data with server-side paging, sorting, filtering, and grouping.
    /// </summary>
    public override async Task<object> ReadAsync(DataManagerRequest dm, string? key = null)
    {
        try
        {
            _logger.LogDebug("UsersAdaptor.ReadAsync: Skip={Skip}, Take={Take}, Sorted={SortCount}, Filtered={FilterCount}, Grouped={GroupCount}", 
                dm.Skip, dm.Take, 
                dm.Sorted?.Count ?? 0, 
                dm.Where?.Count ?? 0,
                dm.Group?.Count ?? 0);
            
            // Get paged data from repository
            var result = await _repository.GetPagedAsync(dm);
            
            // Apply grouping if requested
            if (_gridOperations.RequiresGrouping(dm) && result.Result != null)
            {
                var users = result.Result as IEnumerable<User>;
                if (users != null)
                {
                    _logger.LogDebug("Applying server-side grouping for columns: {GroupColumns}", 
                        string.Join(", ", dm.Group ?? []));
                    
                    return _gridOperations.ApplyGrouping(users, dm, result.Count);
                }
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in UsersAdaptor.ReadAsync");
            throw;
        }
    }

    /// <summary>
    /// Inserts a new user record.
    /// </summary>
    public override async Task<object> InsertAsync(DataManager dataManager, object data, string key)
    {
        try
        {
            if (data is UserCreateDto userDto)
            {
                _logger.LogInformation("UsersAdaptor.InsertAsync: Creating user {UserName}", userDto.UserName);
                await _service.CreateUserAsync(userDto);
                return data;
            }
            else if (data is User user)
            {
                // Convert User to UserCreateDto for creation
                var createDto = new UserCreateDto
                {
                    PersoanaId = user.PersoanaId,
                    UserName = user.UserName,
                    Email = user.Email,
                    IsActive = user.IsActive,
                    Password = "Parola123!" // Default password - should be changed by user
                };
                
                _logger.LogInformation("UsersAdaptor.InsertAsync: Creating user {UserName} (converted from User)", createDto.UserName);
                await _service.CreateUserAsync(createDto);
                return data;
            }
            
            _logger.LogWarning("UsersAdaptor.InsertAsync: Unknown data type {DataType}", data?.GetType().Name);
            return data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in UsersAdaptor.InsertAsync");
            throw;
        }
    }

    /// <summary>
    /// Updates an existing user record.
    /// </summary>
    public override async Task<object> UpdateAsync(DataManager dataManager, object data, string keyField, string key)
    {
        try
        {
            if (data is User user)
            {
                _logger.LogInformation("UsersAdaptor.UpdateAsync: Updating user Id={UserId}", user.Id);
                await _service.UpdateUserAsync(user);
                return data;
            }
            
            _logger.LogWarning("UsersAdaptor.UpdateAsync: Unknown data type {DataType}", data?.GetType().Name);
            return data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in UsersAdaptor.UpdateAsync");
            throw;
        }
    }

    /// <summary>
    /// Deletes (soft delete) a user record.
    /// </summary>
    public override async Task<object> RemoveAsync(DataManager dataManager, object primaryKeyValue, string keyField, string key)
    {
        try
        {
            if (primaryKeyValue is Guid userId)
            {
                _logger.LogInformation("UsersAdaptor.RemoveAsync: Deleting user Id={UserId}", userId);
                await _service.DeleteUserAsync(userId);
                return primaryKeyValue;
            }
            else if (Guid.TryParse(primaryKeyValue?.ToString(), out var parsedId))
            {
                _logger.LogInformation("UsersAdaptor.RemoveAsync: Deleting user Id={UserId} (parsed)", parsedId);
                await _service.DeleteUserAsync(parsedId);
                return primaryKeyValue;
            }
            
            _logger.LogWarning("UsersAdaptor.RemoveAsync: Invalid primary key value {PrimaryKeyValue}", primaryKeyValue);
            return primaryKeyValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in UsersAdaptor.RemoveAsync");
            throw;
        }
    }

    /// <summary>
    /// Handles batch operations (insert, update, delete in one call).
    /// </summary>
    public override async Task<object> BatchUpdateAsync(DataManager dataManager, object changedRecords, object addedRecords, object deletedRecords, string keyField, string key, int? dropIndex)
    {
        try
        {
            // Process deleted records
            if (deletedRecords is IEnumerable<User> deleted)
            {
                foreach (var user in deleted)
                {
                    _logger.LogInformation("UsersAdaptor.BatchUpdate: Deleting user Id={UserId}", user.Id);
                    await _service.DeleteUserAsync(user.Id);
                }
            }

            // Process added records
            if (addedRecords is IEnumerable<User> added)
            {
                foreach (var user in added)
                {
                    var createDto = new UserCreateDto
                    {
                        PersoanaId = user.PersoanaId,
                        UserName = user.UserName,
                        Email = user.Email,
                        IsActive = user.IsActive,
                        Password = "Parola123!" // Default password
                    };
                    
                    _logger.LogInformation("UsersAdaptor.BatchUpdate: Creating user {UserName}", createDto.UserName);
                    await _service.CreateUserAsync(createDto);
                }
            }

            // Process changed records
            if (changedRecords is IEnumerable<User> changed)
            {
                foreach (var user in changed)
                {
                    _logger.LogInformation("UsersAdaptor.BatchUpdate: Updating user Id={UserId}", user.Id);
                    await _service.UpdateUserAsync(user);
                }
            }

            return changedRecords ?? addedRecords ?? deletedRecords ?? new object();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in UsersAdaptor.BatchUpdateAsync");
            throw;
        }
    }
}
