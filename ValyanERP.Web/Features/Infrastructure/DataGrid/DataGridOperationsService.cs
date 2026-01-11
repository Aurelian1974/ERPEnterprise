using Microsoft.Extensions.Logging;
using Syncfusion.Blazor;
using Syncfusion.Blazor.Data;
using System.Collections;

namespace ValyanERP.Web.Features.Infrastructure.DataGrid;

/// <summary>
/// Service for performing server-side DataGrid operations.
/// Provides reusable methods for grouping, sorting, filtering, and paging.
/// </summary>
/// <remarks>
/// This service wraps Syncfusion DataOperations and DataUtil to provide
/// consistent server-side processing across all DataGrids in the application.
/// 
/// Key features:
/// - Server-side grouping with LazyLoad support
/// - Server-side filtering with multiple conditions
/// - Server-side sorting with multiple columns
/// - Server-side paging with count
/// </remarks>
public class DataGridOperationsService : IDataGridOperationsService
{
    private readonly ILogger<DataGridOperationsService> _logger;

    public DataGridOperationsService(ILogger<DataGridOperationsService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool RequiresGrouping(DataManagerRequest dm)
    {
        return dm.Group != null && dm.Group.Count > 0;
    }

    /// <inheritdoc />
    public DataResult ApplyGrouping<T>(IEnumerable<T> dataSource, DataManagerRequest dm, int totalCount)
    {
        if (!RequiresGrouping(dm))
        {
            return new DataResult
            {
                Result = dataSource,
                Count = totalCount
            };
        }

        try
        {
            _logger.LogDebug("Applying grouping for {GroupCount} columns: {GroupColumns}", 
                dm.Group.Count, 
                string.Join(", ", dm.Group));

            // Convert to list for grouping operations
            IEnumerable result = dataSource.ToList();

            // Apply grouping using Syncfusion DataUtil
            // DataUtil.Group returns IEnumerable (not IEnumerable<T>) with Group objects
            // For multiple group columns, we apply them sequentially
            for (int i = 0; i < dm.Group.Count; i++)
            {
                result = DataUtil.Group<T>(
                    result, 
                    dm.Group[i], 
                    dm.Aggregates, 
                    i,  // Level parameter for nested grouping
                    dm.GroupByFormatter,
                    dm.LazyLoad,
                    dm.LazyExpandAllGroup);
            }

            _logger.LogDebug("Grouping applied successfully");

            return new DataResult
            {
                Result = result,
                Count = totalCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying grouping to data source");
            
            // Fallback: return ungrouped data
            return new DataResult
            {
                Result = dataSource,
                Count = totalCount
            };
        }
    }

    /// <inheritdoc />
    public DataResult ApplyOperations<T>(IEnumerable<T> dataSource, DataManagerRequest dm)
    {
        try
        {
            IEnumerable<T> result = dataSource.ToList();
            int totalCount = result.Count();

            // Apply search/filtering
            if (dm.Search != null && dm.Search.Count > 0)
            {
                _logger.LogDebug("Applying search: {SearchCount} conditions", dm.Search.Count);
                result = DataOperations.PerformSearching(result, dm.Search);
            }

            if (dm.Where != null && dm.Where.Count > 0)
            {
                _logger.LogDebug("Applying filter: {FilterCount} conditions", dm.Where.Count);
                result = DataOperations.PerformFiltering(result, dm.Where, dm.Where[0].Operator);
            }

            // Get count after filtering
            totalCount = result.Cast<T>().Count();

            // Apply sorting
            if (dm.Sorted != null && dm.Sorted.Count > 0)
            {
                _logger.LogDebug("Applying sort: {SortCount} columns", dm.Sorted.Count);
                result = DataOperations.PerformSorting(result, dm.Sorted);
            }

            // Apply paging (skip/take) ONLY if NOT grouping
            // When grouping, we need all data for proper group formation
            if (!RequiresGrouping(dm))
            {
                if (dm.Skip > 0)
                {
                    result = DataOperations.PerformSkip(result, dm.Skip);
                }

                if (dm.Take > 0)
                {
                    result = DataOperations.PerformTake(result, dm.Take);
                }
            }

            // Apply grouping if requested
            if (RequiresGrouping(dm))
            {
                return ApplyGrouping(result, dm, totalCount);
            }

            return new DataResult
            {
                Result = result,
                Count = totalCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying operations to data source");
            throw;
        }
    }
}
