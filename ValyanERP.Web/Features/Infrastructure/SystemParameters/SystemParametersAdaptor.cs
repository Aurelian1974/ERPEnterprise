using Microsoft.Extensions.Logging;
using Syncfusion.Blazor;
using Syncfusion.Blazor.Data;
using ValyanERP.Web.Features.Infrastructure.SystemParameters.Models;
using ValyanERP.Web.Features.Infrastructure.SystemParameters.Repositories;
using ValyanERP.Web.Features.Infrastructure.SystemParameters.Services;
using ValyanERP.Web.Features.Infrastructure.DataGrid;

namespace ValyanERP.Web.Features.Infrastructure.SystemParameters;

public class SystemParametersAdaptor : DataAdaptor
{
    private readonly ISystemParametersRepository _repository;
    private readonly ISystemParametersService _service;
    private readonly IDataGridOperationsService _gridOperations;
    private readonly ILogger<SystemParametersAdaptor> _logger;

    public SystemParametersAdaptor(
        ISystemParametersRepository repository,
        ISystemParametersService service,
        IDataGridOperationsService gridOperations,
        ILogger<SystemParametersAdaptor> logger)
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
            var all = (await _service.GetAllAsync(includeReadOnly: true)).ToList();

            // For export/filter-choice requests (Take==0)
            if (!dm.LazyLoad && dm.Take == 0 && !_gridOperations.RequiresGrouping(dm))
            {
                var exportData = all;
                return new DataResult
                {
                    Result = exportData.ToList(),
                    Count = exportData.Count()
                };
            }

            var hasFilters = dm.Where != null && dm.Where.Count > 0;

            // Lazy grouping handling
            if (_gridOperations.RequiresGrouping(dm) && all != null)
            {
                return _gridOperations.ApplyLazyLoadGrouping(all, dm, all.Count);
            }

            if (hasFilters && !_gridOperations.RequiresGrouping(dm))
            {
                var filtered = _gridOperations.ApplyFiltering(all, dm);
                return new DataResult
                {
                    Result = filtered.ToList(),
                    Count = filtered.Count()
                };
            }

            // Apply full set of operations (filtering, sorting, paging, grouping)
            var result = _gridOperations.ApplyOperations(all, dm);
            return result;        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SystemParametersAdaptor.ReadAsync");
            throw;
        }
    }

    public override async Task<object> InsertAsync(DataManager dm, object data, string key)
    {
        if (data is SystemParameter param)
        {
            _logger.LogInformation("SystemParametersAdaptor.InsertAsync: Creating parameter {Key}", param.ParameterKey);
            var id = await _service.CreateAsync(param);
            param.Id = id;
            return param;
        }

        _logger.LogWarning("SystemParametersAdaptor.InsertAsync: Unknown data type {DataType}", data?.GetType().Name);
        return data;
    }

    public override async Task<object> UpdateAsync(DataManager dm, object data, string keyField, string key)
    {
        if (data is SystemParameter param)
        {
            _logger.LogInformation("SystemParametersAdaptor.UpdateAsync: Updating parameter {Id}", param.Id);
            await _service.UpdateAsync(param);
            return param;
        }

        _logger.LogWarning("SystemParametersAdaptor.UpdateAsync: Unknown data type {DataType}", data?.GetType().Name);
        return data;
    }

    public override async Task<object> RemoveAsync(DataManager dm, object primaryKeyValue, string keyField, string key)
    {
        try
        {
            if (primaryKeyValue is Guid id)
            {
                _logger.LogInformation("SystemParametersAdaptor.RemoveAsync: Deleting parameter {Id}", id);
                await _service.DeleteAsync(id);
                return primaryKeyValue;
            }

            if (Guid.TryParse(primaryKeyValue?.ToString(), out var parsed))
            {
                _logger.LogInformation("SystemParametersAdaptor.RemoveAsync: Deleting parameter {Id} (parsed)", parsed);
                await _service.DeleteAsync(parsed);
                return primaryKeyValue;
            }

            _logger.LogWarning("SystemParametersAdaptor.RemoveAsync: Invalid primary key {Key}", primaryKeyValue);
            return primaryKeyValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SystemParametersAdaptor.RemoveAsync");
            throw;
        }
    }

    public override async Task<object> BatchUpdateAsync(DataManager dm, object changedRecords, object addedRecords, object deletedRecords, string keyField, string key, int? dropIndex)
    {
        // Handle deletes
        if (deletedRecords is IEnumerable<SystemParameter> deleted)
        {
            foreach (var d in deleted)
            {
                await _service.DeleteAsync(d.Id);
            }
        }

        // Handle updates
        if (changedRecords is IEnumerable<SystemParameter> changed)
        {
            foreach (var c in changed)
            {
                await _service.UpdateAsync(c);
            }
        }

        // Handle adds
        if (addedRecords is IEnumerable<SystemParameter> added)
        {
            foreach (var a in added)
            {
                await _service.CreateAsync(a);
            }
        }

        return new { }; // Syncfusion ignores return value for batch
    }
}
