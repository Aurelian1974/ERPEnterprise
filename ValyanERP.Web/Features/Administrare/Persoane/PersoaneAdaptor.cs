using Syncfusion.Blazor;
using Syncfusion.Blazor.Data;
using ValyanERP.Web.Features.Administrare.Persoane.Repositories;
using ValyanERP.Web.Features.Administrare.Persoane.Services;
using ValyanERP.Web.Features.Administrare.Persoane.Models;
using ValyanERP.Web.Features.Infrastructure.DataGrid;

namespace ValyanERP.Web.Features.Administrare.Persoane;

public class PersoaneAdaptor : DataAdaptor
{
    private readonly IPersoaneRepository _repository;
    private readonly IPersoaneService _service;
    private readonly IDataGridOperationsService _gridOperations;
    private readonly ILogger<PersoaneAdaptor> _logger;
    
    // Static property to expose the last total count
    public static int LastTotalCount { get; private set; }

    public PersoaneAdaptor(IPersoaneRepository repository, IPersoaneService service, IDataGridOperationsService gridOperations, ILogger<PersoaneAdaptor> logger)
    {
        _repository = repository;
        _service = service;
        _gridOperations = gridOperations;
        _logger = logger;
    }

    public override async Task<object> ReadAsync(DataManagerRequest dm, string? key = null)
    {
        try
        {
            // Detect lazy-load expand requests (user expands a group):
            // - LazyLoad enabled, no Group (expand request), has filters (group value sent as filter), Take==0
            var hasFilters = (dm.Where != null && dm.Where.Any(w => !string.IsNullOrEmpty(w.Field) || w.value != null || !string.IsNullOrEmpty(w.Operator)))
                             || (dm.Search != null && dm.Search.Count > 0);
            var isLazyLoadExpand = dm.LazyLoad && !_gridOperations.RequiresGrouping(dm) && hasFilters && dm.Take == 0;

            _logger.LogDebug("PersoaneAdaptor.ReadAsync: Skip={Skip}, Take={Take}, Sorted={SortCount}, Filtered={FilterCount}, Grouped={GroupCount}, LazyLoad={LazyLoad}, IsExpand={IsExpand}",
                dm.Skip, dm.Take,
                dm.Sorted?.Count ?? 0,
                dm.Where?.Count ?? 0,
                dm.Group?.Count ?? 0,
                dm.LazyLoad,
                isLazyLoadExpand);

            // Get paged data from repository
            var result = await _repository.GetPagedAsync(dm);

            // Export / FilterChoice requests (Take==0 and not grouping)
            if (!dm.LazyLoad && dm.Take == 0 && !_gridOperations.RequiresGrouping(dm))
            {
                var exportData = result.Result as IEnumerable<Persoana> ?? Enumerable.Empty<Persoana>();
                _logger.LogDebug("Export/FilterChoice request - returning {Count} items as DataResult", exportData.Count());
                var count = exportData.Count();
                LastTotalCount = count;
                _logger.LogDebug("Export DataResult -> ResultCount={ResultCount}, TotalCount={TotalCount}", exportData.Count(), count);
                return new DataResult { Result = exportData.ToList(), Count = count };
            }

            // Lazy-load expand (user clicked to expand a group): fetch full matching set from DB and apply filters so counts are DB-accurate
            if (isLazyLoadExpand)
            {
                // Build a request for all matching records (Take=0 interpreted as 'all' by repository)
                var fullRequest = new DataManagerRequest
                {
                    Search = dm.Search,
                    Where = dm.Where,
                    Sorted = dm.Sorted,
                    Group = dm.Group,
                    Aggregates = dm.Aggregates,
                    Take = 0,
                    Skip = 0,
                    LazyLoad = dm.LazyLoad,
                    GroupByFormatter = dm.GroupByFormatter,
                    RequiresCounts = dm.RequiresCounts
                };

                var fullResult = await _repository.GetPagedAsync(fullRequest);
                var all = fullResult.Result as IEnumerable<Persoana> ?? Enumerable.Empty<Persoana>();
                var filtered = _gridOperations.ApplyFiltering(all, dm);
                var filteredCount = filtered.Count();
                _logger.LogDebug("Lazy load expand - filtered from {TotalCount} to {FilteredCount}", all.Count(), filteredCount);
                LastTotalCount = filteredCount;
                _logger.LogDebug("Lazy-expand DataResult -> ResultCount={ResultCount}, TotalCount={TotalCount}", filtered.Count(), filteredCount);
                return filtered;
            }

            // Normal filter requests (no grouping) - request full matching set from DB so counts/pager reflect DB results
            if (hasFilters && !_gridOperations.RequiresGrouping(dm))
            {
                var fullRequest = new DataManagerRequest
                {
                    Search = dm.Search,
                    Where = dm.Where,
                    Sorted = dm.Sorted,
                    Group = dm.Group,
                    Aggregates = dm.Aggregates,
                    Take = 0, // get all matching rows from DB
                    Skip = 0
                };

                var fullResult = await _repository.GetPagedAsync(fullRequest);
                var all = fullResult.Result as IEnumerable<Persoana> ?? Enumerable.Empty<Persoana>();

                // The repository returns an authoritative DB count in fullResult.Count.
                // Use that for pager/header so it matches the DB state. Apply paging to the returned dataset.
                var totalCount = fullResult.Count;
                var paged = all.Skip(dm.Skip).Take(dm.Take > 0 ? dm.Take : totalCount).ToList();

                _logger.LogDebug("Normal filter request - DB count {DbCount}, returning page of {PageCount} items", totalCount, paged.Count);
                LastTotalCount = totalCount;
                _logger.LogDebug("Normal-filter DataResult -> ResultCount={ResultCount}, TotalCount={TotalCount}", paged.Count, totalCount);
                return new DataResult { Result = paged, Count = totalCount };
            }

            // Apply lazy-load grouping if requested
            if (_gridOperations.RequiresGrouping(dm) && result.Result != null)
            {
                var persoane = result.Result as IEnumerable<Persoana>;
                if (persoane != null)
                {
                    _logger.LogDebug("Applying lazy load grouping for columns: {GroupColumns}", string.Join(", ", dm.Group ?? new List<string>()));
                    var groupingResult = _gridOperations.ApplyLazyLoadGrouping(persoane, dm, result.Count);
                    // If DataResult returned, update LastTotalCount accordingly, otherwise use group count or total
                    if (groupingResult is DataResult gr)
                    {
                        LastTotalCount = gr.Count;
                    }
                    else if (groupingResult is IEnumerable<object> ienum)
                    {
                        LastTotalCount = ienum.Cast<object>().Count();
                    }

                    _logger.LogDebug("Grouping result type: {Type}, Count (if DataResult)={Count}", groupingResult.GetType().Name, groupingResult is DataResult d ? d.Count : (object)0);
                    return groupingResult;
                }
            }

            LastTotalCount = result.Count;
            _logger.LogDebug("Default return -> ResultCount={ResultCount}, TotalCount={TotalCount}", (result.Result as IEnumerable<Persoana>)?.Count() ?? 0, result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in PersoaneAdaptor.ReadAsync");
            throw;
        }
    }
    
    public override async Task<object> InsertAsync(DataManager dm, object value, string key)
    {
        try
        {
            var persoana = value as Persoana;
            if (persoana == null)
                throw new ArgumentNullException(nameof(value));
                
            await _service.CreateAsync(persoana);
            return value;
        }
        catch (InvalidOperationException ex)
        {
            // Re-throw with clear message for UI
            _logger.LogWarning(ex, "Validation error in InsertAsync");
            throw new InvalidOperationException(ex.Message, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in InsertAsync");
            throw new InvalidOperationException("Eroare la crearea persoanei. Vă rugăm verificați datele introduse.", ex);
        }
    }
    
    public override async Task<object> UpdateAsync(DataManager dm, object value, string keyField, string key)
    {
        try
        {
            var persoana = value as Persoana;
            if (persoana == null)
                throw new ArgumentNullException(nameof(value));
                
            await _service.UpdateAsync(persoana);
            return value;
        }
        catch (InvalidOperationException ex)
        {
            // Re-throw with clear message for UI
            _logger.LogWarning(ex, "Validation error in UpdateAsync");
            throw new InvalidOperationException(ex.Message, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in UpdateAsync");
            throw new InvalidOperationException("Eroare la actualizarea persoanei. Vă rugăm verificați datele introduse.", ex);
        }
    }
    
    public override async Task<object> RemoveAsync(DataManager dm, object value, string keyField, string key)
    {
        var persoana = value as Persoana;
        if (persoana == null)
            throw new ArgumentNullException(nameof(value));
            
        await _service.DeleteAsync(persoana.Id);
        return value;
    }
}
