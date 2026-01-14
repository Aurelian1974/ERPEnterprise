using Microsoft.Extensions.Logging;
using Syncfusion.Blazor;
using Syncfusion.Blazor.Data;
using ValyanERP.Web.Features.Administrare.TipuriArticole.Models;
using ValyanERP.Web.Features.Administrare.TipuriArticole.Repositories;
using ValyanERP.Web.Features.Administrare.TipuriArticole.Services;
using ValyanERP.Web.Features.Infrastructure.DataGrid;

namespace ValyanERP.Web.Features.Administrare.TipuriArticole;

/// <summary>
/// Custom Syncfusion DataAdaptor for ItemTypes grid.
/// Handles server-side paging, sorting, filtering, grouping, and CRUD operations.
/// </summary>
public class ItemTypesAdaptor : DataAdaptor
{
    private readonly IItemTypesRepository _repository;
    private readonly IItemTypesService _service;
    private readonly IDataGridOperationsService _gridOperations;
    private readonly ILogger<ItemTypesAdaptor> _logger;

    public ItemTypesAdaptor(
        IItemTypesRepository repository,
        IItemTypesService service,
        IDataGridOperationsService gridOperations,
        ILogger<ItemTypesAdaptor> logger)
    {
        _repository = repository;
        _service = service;
        _gridOperations = gridOperations;
        _logger = logger;
    }

    /// <summary>
    /// Reads data with server-side paging, sorting, filtering, and lazy load grouping.
    /// </summary>
    public override async Task<object> ReadAsync(DataManagerRequest dm, string? key = null)
    {
        try
        {
            // Lazy load expand detection:
            // - LazyLoad is enabled
            // - No grouping columns in request (user clicked to expand a group)
            // - Has filters (Syncfusion sends the group value as filter)
            // - Take=0 (expand requests don't use paging, normal filter requests have Take>0)
            var hasFilters = dm.Where != null && dm.Where.Count > 0;
            var isLazyLoadExpand = dm.LazyLoad && !_gridOperations.RequiresGrouping(dm) && hasFilters && dm.Take == 0;

            _logger.LogDebug("ItemTypesAdaptor.ReadAsync: Skip={Skip}, Take={Take}, Sorted={SortCount}, Filtered={FilterCount}, Grouped={GroupCount}, LazyLoad={LazyLoad}, IsExpand={IsExpand}",
                dm.Skip, dm.Take,
                dm.Sorted?.Count ?? 0,
                dm.Where?.Count ?? 0,
                dm.Group?.Count ?? 0,
                dm.LazyLoad,
                isLazyLoadExpand);

            // Get paged data from repository
            var result = await _repository.GetPagedAsync(dm);

            // For filter choice requests and export operations (LazyLoad=false, Take=0):
            // - FilterChoiceRequest: Syncfusion needs DataResult to populate Excel filter popup
            // - Export: handled manually in code-behind, so returning DataResult is fine too
            if (!dm.LazyLoad && dm.Take == 0 && !_gridOperations.RequiresGrouping(dm))
            {
                var exportData = result.Result as IEnumerable<ItemType> ?? Enumerable.Empty<ItemType>();
                _logger.LogDebug("Export/FilterChoice request - returning {Count} items as DataResult", exportData.Count());

                // Return DataResult for Syncfusion compatibility (Excel filter popup needs this)
                return new DataResult
                {
                    Result = exportData.ToList(),
                    Count = exportData.Count()
                };
            }

            // For lazy load expand requests (user clicked to expand a group),
            // Syncfusion sends filters in dm.Where with the group value.
            // We need to filter the data to return only items for that group.
            if (isLazyLoadExpand)
            {
                var itemTypes = result.Result as IEnumerable<ItemType> ?? Enumerable.Empty<ItemType>();

                // Apply filtering to get only items for the expanded group
                var filteredItemTypes = _gridOperations.ApplyFiltering(itemTypes, dm);

                _logger.LogDebug("Lazy load expand request - filtered from {TotalCount} to {FilteredCount} items",
                    itemTypes.Count(), filteredItemTypes.Count());

                return filteredItemTypes;
            }

            // For normal filtering requests (from UI filter), apply filtering client-side
            // since repository doesn't support advanced filters yet
            if (hasFilters && !_gridOperations.RequiresGrouping(dm))
            {
                var itemTypes = result.Result as IEnumerable<ItemType> ?? Enumerable.Empty<ItemType>();
                var filteredItemTypes = _gridOperations.ApplyFiltering(itemTypes, dm);
                var filteredCount = filteredItemTypes.Count();

                _logger.LogDebug("Normal filter request - filtered from {TotalCount} to {FilteredCount} items",
                    itemTypes.Count(), filteredCount);

                return new DataResult
                {
                    Result = filteredItemTypes,
                    Count = filteredCount
                };
            }

            // Apply lazy load grouping if requested
            if (_gridOperations.RequiresGrouping(dm) && result.Result != null)
            {
                var itemTypes = result.Result as IEnumerable<ItemType>;
                if (itemTypes != null)
                {
                    _logger.LogDebug("Applying lazy load grouping for columns: {GroupColumns}",
                        string.Join(", ", dm.Group ?? []));

                    // Use ApplyLazyLoadGrouping which returns correct type based on RequiresCounts
                    return _gridOperations.ApplyLazyLoadGrouping(itemTypes, dm, result.Count);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ItemTypesAdaptor.ReadAsync");
            throw;
        }
    }

    /// <summary>
    /// Inserts a new item type record.
    /// </summary>
    public override async Task<object> InsertAsync(DataManager dataManager, object data, string key)
    {
        try
        {
            if (data is ItemTypeCreateDto itemTypeDto)
            {
                _logger.LogInformation("ItemTypesAdaptor.InsertAsync: Creating item type {ItemTypeCode}", itemTypeDto.ItemTypeCode);
                await _service.CreateItemTypeAsync(itemTypeDto);
                return data;
            }
            else if (data is ItemType itemType)
            {
                // Convert ItemType to ItemTypeCreateDto for creation
                var createDto = new ItemTypeCreateDto
                {
                    ItemTypeCode = itemType.ItemTypeCode,
                    ItemTypeName = itemType.ItemTypeName,
                    Description = itemType.Description
                };

                _logger.LogInformation("ItemTypesAdaptor.InsertAsync: Creating item type {ItemTypeCode} via fallback", createDto.ItemTypeCode);
                await _service.CreateItemTypeAsync(createDto);
                return data ?? new object();
            }

            _logger.LogWarning("ItemTypesAdaptor.InsertAsync: Unknown data type {DataType}", data?.GetType().Name);
            return data ?? new object();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ItemTypesAdaptor.InsertAsync");
            throw;
        }
    }

    /// <summary>
    /// Updates an existing item type record.
    /// </summary>
    public override async Task<object> UpdateAsync(DataManager dataManager, object data, string keyField, string key)
    {
        try
        {
            if (data is ItemType itemType)
            {
                // Convert ItemType to ItemTypeUpdateDto for update
                var updateDto = new ItemTypeUpdateDto
                {
                    Id = itemType.Id,
                    ItemTypeCode = itemType.ItemTypeCode,
                    ItemTypeName = itemType.ItemTypeName,
                    Description = itemType.Description
                };

                _logger.LogInformation("ItemTypesAdaptor.UpdateAsync: Updating item type Id={ItemTypeId}", itemType.Id);
                await _service.UpdateItemTypeAsync(updateDto);
                return data ?? new object();
            }

            _logger.LogWarning("ItemTypesAdaptor.UpdateAsync: Unknown data type {DataType}", data?.GetType().Name);
            return data ?? new object();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ItemTypesAdaptor.UpdateAsync");
            throw;
        }
    }

    /// <summary>
    /// Deletes (soft delete) an item type record.
    /// </summary>
    public override async Task<object> RemoveAsync(DataManager dataManager, object primaryKeyValue, string keyField, string key)
    {
        try
        {
            if (primaryKeyValue is Guid itemTypeId)
            {
                _logger.LogInformation("ItemTypesAdaptor.RemoveAsync: Deleting item type Id={ItemTypeId}", itemTypeId);
                await _service.DeleteItemTypeAsync(itemTypeId);
                return primaryKeyValue ?? new object();
            }
            else if (Guid.TryParse(primaryKeyValue?.ToString(), out var parsedId))
            {
                _logger.LogInformation("ItemTypesAdaptor.RemoveAsync: Deleting item type Id={ItemTypeId} (parsed)", parsedId);
                await _service.DeleteItemTypeAsync(parsedId);
                return primaryKeyValue ?? new object();
            }

            _logger.LogWarning("ItemTypesAdaptor.RemoveAsync: Invalid primary key value {PrimaryKeyValue}", primaryKeyValue);
            return primaryKeyValue ?? new object();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ItemTypesAdaptor.RemoveAsync");
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
            if (deletedRecords is IEnumerable<ItemType> deleted)
            {
                foreach (var itemType in deleted)
                {
                    _logger.LogInformation("ItemTypesAdaptor.BatchUpdate: Deleting item type Id={ItemTypeId}", itemType.Id);
                    await _service.DeleteItemTypeAsync(itemType.Id);
                }
            }

            // Process added records
            if (addedRecords is IEnumerable<ItemType> added)
            {
                foreach (var itemType in added)
                {
                    var createDto = new ItemTypeCreateDto
                    {
                        ItemTypeCode = itemType.ItemTypeCode,
                        ItemTypeName = itemType.ItemTypeName,
                        Description = itemType.Description
                    };

                    _logger.LogInformation("ItemTypesAdaptor.BatchUpdate: Creating item type {ItemTypeCode}", createDto.ItemTypeCode);
                    await _service.CreateItemTypeAsync(createDto);
                }
            }

            // Process changed records
            if (changedRecords is IEnumerable<ItemType> changed)
            {
                foreach (var itemType in changed)
                {
                    // Convert ItemType to ItemTypeUpdateDto for update
                    var updateDto = new ItemTypeUpdateDto
                    {
                        Id = itemType.Id,
                        ItemTypeCode = itemType.ItemTypeCode,
                        ItemTypeName = itemType.ItemTypeName,
                        Description = itemType.Description
                    };

                    _logger.LogInformation("ItemTypesAdaptor.BatchUpdate: Updating item type Id={ItemTypeId}", itemType.Id);
                    await _service.UpdateItemTypeAsync(updateDto);
                }
            }

            return changedRecords ?? addedRecords ?? deletedRecords ?? new object();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ItemTypesAdaptor.BatchUpdateAsync");
            throw;
        }
    }
}