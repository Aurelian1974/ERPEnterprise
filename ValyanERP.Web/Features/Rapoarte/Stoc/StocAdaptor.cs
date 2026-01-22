using Microsoft.Extensions.Logging;
using Syncfusion.Blazor;
using Syncfusion.Blazor.Data;
using System;
using System.Linq;
using System.Collections.Generic;
using ValyanERP.Web.Features.Rapoarte.Stoc.Models;
using ValyanERP.Web.Features.Rapoarte.Stoc.Repositories;
using ValyanERP.Web.Features.Rapoarte.Stoc.Services;
using ValyanERP.Web.Features.Infrastructure.DataGrid;
using ValyanERP.Web.Features.Administrare.SocietateaProprie.Repositories;
using ValyanERP.Web.Features.Infrastructure.Security.Services;

namespace ValyanERP.Web.Features.Rapoarte.Stoc;

/// <summary>
/// Custom Syncfusion DataAdaptor for Stoc grid.
/// Handles server-side paging, sorting, filtering, grouping.
/// </summary>
public class StocAdaptor : DataAdaptor
{
    private readonly IStocRepository _repository;
    private readonly IStocService _service;
    private readonly IDataGridOperationsService _gridOperations;
    private readonly IUserPerimeterProvider _perimeterProvider;
    private readonly ILocationRepository _locationRepository;
    private readonly ICompanyRepository _companyRepository;
    private readonly ILogger<StocAdaptor> _logger;

    public StocAdaptor(
        IStocRepository repository,
        IStocService service,
        IDataGridOperationsService gridOperations,
        IUserPerimeterProvider perimeterProvider,
        ILocationRepository locationRepository,
        ICompanyRepository companyRepository,
        ILogger<StocAdaptor> logger)
    {
        _repository = repository;
        _service = service;
        _gridOperations = gridOperations;
        _perimeterProvider = perimeterProvider;
            _locationRepository = locationRepository;
            _companyRepository = companyRepository;
        _logger = logger;
    }

    /// <summary>
    /// Reads data with server-side paging, sorting, filtering, and lazy load grouping.
    /// </summary>
    public override async Task<object> ReadAsync(DataManagerRequest dm, string? key = null)
    {
        try
        {
            var hasFilters = dm.Where != null && dm.Where.Count > 0;
            var isLazyLoadExpand = dm.LazyLoad && !_gridOperations.RequiresGrouping(dm) && hasFilters && dm.Take == 0;

            _logger.LogDebug("StocAdaptor.ReadAsync: Skip={Skip}, Take={Take}, Sorted={SortCount}, Filtered={FilterCount}, Grouped={GroupCount}, LazyLoad={LazyLoad}, IsExpand={IsExpand}",
                dm.Skip, dm.Take, dm.Sorted?.Count ?? 0, dm.Where?.Count ?? 0, dm.Group?.Count ?? 0, dm.LazyLoad, isLazyLoadExpand);

            // Log aggregates and group descriptors for debugging grouping/aggregates behavior
            try
            {
                if (dm.Group != null && dm.Group.Count > 0)
                {
                    _logger.LogDebug("StocAdaptor.ReadAsync - Group descriptors: {Groups}", string.Join(',', dm.Group.Select(g => g?.ToString() ?? "<null>")));
                }

                if (dm.Aggregates != null && dm.Aggregates.Count > 0)
                {
                    _logger.LogDebug("StocAdaptor.ReadAsync - Aggregates: {Aggregates}", string.Join(',', dm.Aggregates.Select(a => a?.ToString() ?? "<null>")));
                }
            }
            catch { /* non-fatal logging */ }

            // Extract possible filter parameters for repository/service
            Guid? punctLucruId = null;
            Guid? tipArticolId = null;
            decimal? minStoc = null;

            if (dm.Where != null)
            {
                foreach (var f in dm.Where)
                {
                    try
                    {
                        if (string.Equals(f.Field, "PunctLucruId", StringComparison.OrdinalIgnoreCase))
                        {
                            if (Guid.TryParse(Convert.ToString(f.value), out var g)) punctLucruId = g;
                        }
                        else if (string.Equals(f.Field, "TipArticolId", StringComparison.OrdinalIgnoreCase))
                        {
                            if (Guid.TryParse(Convert.ToString(f.value), out var g2)) tipArticolId = g2;
                        }
                        else if (string.Equals(f.Field, "MinStoc", StringComparison.OrdinalIgnoreCase) || string.Equals(f.Field, "minStoc", StringComparison.OrdinalIgnoreCase))
                        {
                            if (decimal.TryParse(Convert.ToString(f.value), out var d)) minStoc = d;
                        }
                    }
                    catch { /* ignore parse errors for individual filters */ }
                }
            }

            // If paging is requested and repository supports GetPagedAsync, prefer server-side paging
            // but only when the user has full access (so server SP can return correct scope).
            if (!_gridOperations.RequiresGrouping(dm) && dm.Take > 0)
            {
                try
                {
                    var perimeter = await _perimeterProvider.GetPerimeterAsync();
                    var sortField = dm.Sorted != null && dm.Sorted.Count > 0 ? (dm.Sorted[0].Name ?? "Cod") : "Cod";
                    var sortDirection = dm.Sorted != null && dm.Sorted.Count > 0 ? (dm.Sorted[0].Direction ?? "ASC") : "ASC";

                    if (perimeter.HasFullAccess)
                    {
                        var paged = await _repository.GetPagedAsync(dm.Skip, dm.Take, sortField, sortDirection, punctLucruId, tipArticolId, minStoc);
                        return paged;
                    }
                    // If the user does not have full access, fall back to fetching full data and filter in-memory
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Paged repository call failed, falling back to full fetch");
                }
            }

            // Fallback: Fetch full data from service (repository uses stored procedure)
            var data = await _service.GetStocAsync(punctLucruId, tipArticolId, minStoc);
            var list = data?.ToList() ?? new List<StocReportItemDto>();

            _logger.LogDebug("StocAdaptor fetched {Count} records from service", list.Count);

            // Apply organizational perimeter filtering: only return workplaces/points the user can see
            try
            {
                var visibleLocations = (await _perimeterProvider.GetVisibleLocationIdsAsync())?.ToList() ?? new List<Guid>();
                _logger.LogDebug("User visible locations count: {Count}", visibleLocations.Count);

                if (visibleLocations.Count > 0)
                {
                    var before = list.Count;
                    list = list.Where(i => i.PunctLucruId.HasValue && visibleLocations.Contains(i.PunctLucruId.Value)).ToList();
                    _logger.LogDebug("StocAdaptor filtered {Before} -> {After} by user's visible locations", before, list.Count);
                }
                else
                {
                    // If the perimeter provider returned no locations (no access), return empty list
                    _logger.LogDebug("User has no visible locations, returning empty stock list");
                    list = new List<StocReportItemDto>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to apply user perimeter filtering; returning unfiltered results as fallback");
            }

            // Handle export/filter choice requests (no paging, return all results count)
            if (!dm.LazyLoad && dm.Take == 0 && !_gridOperations.RequiresGrouping(dm))
            {
                return new DataResult
                {
                    Result = list,
                    Count = list.Count
                };
            }

            // If grouping is requested and not lazy-load, apply full grouping
            if (_gridOperations.RequiresGrouping(dm) && !dm.LazyLoad)
            {
                // Apply grouping and return DataResult with counts
                var grouped = _gridOperations.ApplyGrouping(list, dm, list.Count);
                return grouped;
            }

            // Lazy load expand (group expand) - apply filtering and return items for that group
            if (isLazyLoadExpand)
            {
                var filtered = _gridOperations.ApplyFiltering(list, dm);
                return filtered;
            }

            // Normal filter requests - apply filtering and return DataResult
            if (hasFilters && !_gridOperations.RequiresGrouping(dm))
            {
                var filtered = _gridOperations.ApplyFiltering(list, dm);
                var filteredCount = filtered.Count();

                return new DataResult
                {
                    Result = filtered,
                    Count = filteredCount
                };
            }

            // Grouping (lazy load) support
            if (_gridOperations.RequiresGrouping(dm) && list != null)
            {
                return _gridOperations.ApplyLazyLoadGrouping(list, dm, list.Count);
            }

            // Default: apply search/sort/paging operations
            return _gridOperations.ApplyOperations(list, dm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in StocAdaptor.ReadAsync");
            throw;
        }
    }
}
