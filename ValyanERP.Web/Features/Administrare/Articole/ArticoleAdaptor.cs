using Microsoft.Extensions.Logging;
using Syncfusion.Blazor;
using Syncfusion.Blazor.Data;
using ValyanERP.Web.Features.Administrare.Articole.Models;
using ValyanERP.Web.Features.Administrare.Articole.Repositories;
using ValyanERP.Web.Features.Administrare.Articole.Services;
using ValyanERP.Web.Features.Infrastructure.DataGrid;

namespace ValyanERP.Web.Features.Administrare.Articole;

/// <summary>
/// Custom Syncfusion DataAdaptor for Articole grid.
/// Handles server-side paging, sorting, filtering, grouping, and CRUD operations.
/// </summary>
public class ArticoleAdaptor : DataAdaptor
{
    private readonly IArticoleRepository _repository;
    private readonly IArticoleService _service;
    private readonly IDataGridOperationsService _gridOperations;
    private readonly ILogger<ArticoleAdaptor> _logger;

    public ArticoleAdaptor(
        IArticoleRepository repository,
        IArticoleService service,
        IDataGridOperationsService gridOperations,
        ILogger<ArticoleAdaptor> logger)
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

            _logger.LogDebug("ArticoleAdaptor.ReadAsync: Skip={Skip}, Take={Take}, Sorted={SortCount}, Filtered={FilterCount}, Grouped={GroupCount}, LazyLoad={LazyLoad}, IsExpand={IsExpand}",
                dm.Skip, dm.Take, dm.Sorted?.Count ?? 0, dm.Where?.Count ?? 0, dm.Group?.Count ?? 0, dm.LazyLoad, isLazyLoadExpand);

            // Get paged data from repository
            var result = await _repository.GetPagedAsync(dm);

            // For filter choice requests and export operations (LazyLoad=false, Take=0):
            // - FilterChoiceRequest: Syncfusion needs DataResult to populate Excel filter popup
            // - Export: handled manually in code-behind, so returning DataResult is fine too
            if (!dm.LazyLoad && dm.Take == 0 && !_gridOperations.RequiresGrouping(dm))
            {
                var exportData = result.Result as IEnumerable<Articol> ?? Enumerable.Empty<Articol>();
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
                var articole = result.Result as IEnumerable<Articol> ?? Enumerable.Empty<Articol>();

                // Apply filtering to get only items for the expanded group
                var filteredArticole = _gridOperations.ApplyFiltering(articole, dm);

                _logger.LogDebug("Lazy load expand request - filtered from {TotalCount} to {FilteredCount} items",
                    articole.Count(), filteredArticole.Count());

                return filteredArticole;
            }

            // For normal filtering requests (from UI filter), apply filtering client-side
            // since repository doesn't support advanced filters yet
            if (hasFilters && !_gridOperations.RequiresGrouping(dm))
            {
                var articole = result.Result as IEnumerable<Articol> ?? Enumerable.Empty<Articol>();
                var filteredArticole = _gridOperations.ApplyFiltering(articole, dm);
                var filteredCount = filteredArticole.Count();

                _logger.LogDebug("Normal filter request - filtered from {TotalCount} to {FilteredCount} items",
                    articole.Count(), filteredCount);

                return new DataResult
                {
                    Result = filteredArticole,
                    Count = filteredCount
                };
            }

            // Apply lazy load grouping if requested
            if (_gridOperations.RequiresGrouping(dm) && result.Result != null)
            {
                var articole = result.Result as IEnumerable<Articol>;
                if (articole != null)
                {
                    _logger.LogDebug("Applying lazy load grouping for columns: {GroupColumns}",
                        string.Join(", ", dm.Group ?? []));

                    // Use ApplyLazyLoadGrouping which returns correct type based on RequiresCounts
                    return _gridOperations.ApplyLazyLoadGrouping(articole, dm, result.Count);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ArticoleAdaptor.ReadAsync");
            throw;
        }
    }

    /// <summary>
    /// Inserts a new articol.
    /// </summary>
    public override async Task<object> InsertAsync(DataManager dm, object value, string? key)
    {
        try
        {
            _logger.LogDebug("ArticoleAdaptor.InsertAsync: Inserting new articol");

            var articol = (Articol)value;
            var createDto = new ArticolCreateDto
            {
                ArticolCode = articol.ArticolCode,
                ArticolName = articol.ArticolName,
                Description = articol.Description,
                TipArticolId = articol.TipArticolId,
                UnitateMasura = articol.UnitateMasura,
                PretAchizitie = articol.PretAchizitie,
                PretVanzare = articol.PretVanzare,
                StocMinim = articol.StocMinim,
                StocMaxim = articol.StocMaxim
            };

            var success = await _service.CreateArticolAsync(createDto);
            if (!success)
            {
                throw new InvalidOperationException("Failed to create articol");
            }

            // Return the created articol with generated ID
            var createdArticol = await _service.GetArticolByCodeAsync(articol.ArticolCode);
            return createdArticol ?? throw new InvalidOperationException("Failed to retrieve created articol");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ArticoleAdaptor.InsertAsync");
            throw;
        }
    }

    /// <summary>
    /// Updates an existing articol.
    /// </summary>
    public override async Task<object> UpdateAsync(DataManager dm, object value, string? keyField, string? key)
    {
        try
        {
            _logger.LogDebug("ArticoleAdaptor.UpdateAsync: Updating articol with key {Key}", key);

            var articol = (Articol)value;
            var updateDto = new ArticolUpdateDto
            {
                Id = articol.Id,
                ArticolCode = articol.ArticolCode,
                ArticolName = articol.ArticolName,
                Description = articol.Description,
                TipArticolId = articol.TipArticolId,
                UnitateMasura = articol.UnitateMasura,
                PretAchizitie = articol.PretAchizitie,
                PretVanzare = articol.PretVanzare,
                StocMinim = articol.StocMinim,
                StocMaxim = articol.StocMaxim
            };

            var success = await _service.UpdateArticolAsync(updateDto);
            if (!success)
            {
                throw new InvalidOperationException("Failed to update articol");
            }

            return articol;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ArticoleAdaptor.UpdateAsync");
            throw;
        }
    }

    /// <summary>
    /// Deletes an articol.
    /// </summary>
    public override async Task<object> RemoveAsync(DataManager dm, object value, string? keyField, string? key)
    {
        try
        {
            _logger.LogDebug("ArticoleAdaptor.RemoveAsync: Deleting articol with key {Key}", key);

            var articol = (Articol)value;
            var success = await _service.DeleteArticolAsync(articol.Id);
            if (!success)
            {
                throw new InvalidOperationException("Failed to delete articol");
            }

            return value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ArticoleAdaptor.RemoveAsync");
            throw;
        }
    }
}