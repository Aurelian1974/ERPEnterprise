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
            _logger.LogDebug("Applying lazy load grouping for {GroupCount} columns: {GroupColumns}, LazyLoad={LazyLoad}, LazyExpandAllGroup={LazyExpandAllGroup}", 
                dm.Group.Count, 
                string.Join(", ", dm.Group),
                dm.LazyLoad,
                dm.LazyExpandAllGroup);

            // Convert to list for grouping operations
            IEnumerable result = dataSource.ToList();

            // Apply grouping using Syncfusion DataUtil
            // For lazy load grouping, DataUtil.Group handles the lazy loading logic internally
            // It returns Group objects that contain caption info and can be expanded on demand
            for (int i = 0; i < dm.Group.Count; i++)
            {
                result = DataUtil.Group<T>(
                    result, 
                    dm.Group[i], 
                    dm.Aggregates, 
                    i,  // Level parameter for nested grouping
                    dm.GroupByFormatter,
                    dm.LazyLoad,           // Pass lazy load flag
                    dm.LazyExpandAllGroup); // Pass expand all flag
            }

            // For lazy load grouping, count should be the number of group captions, not total records
            var groupCount = result.Cast<object>().Count();
            
            _logger.LogDebug("Lazy load grouping applied successfully, group count: {GroupCount}", groupCount);

            return new DataResult
            {
                Result = result,
                Count = groupCount
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
    public bool IsLazyLoadGrouping(DataManagerRequest dm)
    {
        return RequiresGrouping(dm) && dm.LazyLoad;
    }

    /// <inheritdoc />
    public object ApplyLazyLoadGrouping<T>(IEnumerable<T> dataSource, DataManagerRequest dm, int totalCount)
    {
        if (!RequiresGrouping(dm))
        {
            return dm.RequiresCounts 
                ? new DataResult { Result = dataSource, Count = totalCount } 
                : dataSource;
        }

        try
        {
            _logger.LogDebug("Applying lazy load grouping for {GroupCount} columns: {GroupColumns}, LazyLoad={LazyLoad}, LazyExpandAllGroup={LazyExpandAllGroup}, RequiresCounts={RequiresCounts}", 
                dm.Group.Count, 
                string.Join(", ", dm.Group),
                dm.LazyLoad,
                dm.LazyExpandAllGroup,
                dm.RequiresCounts);

            // Apply grouping using Syncfusion DataUtil
            // For lazy load, DataUtil.Group creates Group objects with Items populated only when expanded
            IEnumerable result = dataSource.ToList();
            
            for (int i = 0; i < dm.Group.Count; i++)
            {
                result = DataUtil.Group<T>(
                    result, 
                    dm.Group[i], 
                    dm.Aggregates, 
                    i,
                    dm.GroupByFormatter,
                    dm.LazyLoad,
                    dm.LazyExpandAllGroup);
            }

            var groupCount = result.Cast<object>().Count();
            
            _logger.LogDebug("Lazy load grouping applied, group count: {GroupCount}", groupCount);

            // Return based on RequiresCounts flag (as per Syncfusion documentation)
            if (dm.RequiresCounts)
            {
                return new DataResult
                {
                    Result = result,
                    Count = groupCount
                };
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying lazy load grouping");
            
            // Fallback
            return dm.RequiresCounts 
                ? new DataResult { Result = dataSource, Count = totalCount } 
                : dataSource;
        }
    }

    /// <inheritdoc />
    public IEnumerable<T> ApplyFiltering<T>(IEnumerable<T> dataSource, DataManagerRequest dm)
    {
        IEnumerable<T> result = dataSource;

        // Apply Where filters (used by lazy load expand for group filtering)
        if (dm.Where != null && dm.Where.Count > 0)
        {
            _logger.LogDebug("Applying {FilterCount} filter conditions", dm.Where.Count);
            
            foreach (var filter in dm.Where)
            {
                _logger.LogDebug("Filter: Field={Field}, Operator={Operator}, Value={Value}", 
                    filter.Field, filter.Operator, filter.value);
            }
            
            result = DataOperations.PerformFiltering(result, dm.Where, dm.Where[0].Operator);
        }

        // Apply sorting if specified
        if (dm.Sorted != null && dm.Sorted.Count > 0)
        {
            result = DataOperations.PerformSorting(result, dm.Sorted);
        }

        return result;
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
