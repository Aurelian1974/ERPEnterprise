using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Syncfusion.Blazor;
using Syncfusion.Blazor.Data;
using Microsoft.Extensions.Logging;
using ValyanERP.Web.Features.Administrare.TipuriArticole.Services;

namespace ValyanERP.Web.Features.Administrare.TipuriArticole.Adaptors
{
    public class ItemTypesAdaptor : DataAdaptor
    {
        private readonly IItemTypesService _service;
        private readonly ILogger<ItemTypesAdaptor> _logger;

        public ItemTypesAdaptor(IItemTypesService service, ILogger<ItemTypesAdaptor> logger)
        {
            _service = service;
            _logger = logger;
            _logger.LogDebug("ItemTypesAdaptor created");
        }

        public override async Task<object> ReadAsync(DataManagerRequest dm, string? key = null)
        {
            try
            {
                _logger.LogDebug("ItemTypesAdaptor.ReadAsync called with Skip={Skip}, Take={Take}", dm.Skip, dm.Take);
                
                var all = (await _service.GetAllAsync()).ToList();
                _logger.LogDebug("Service returned {Count} items", all.Count);

                // Handle paging
                var data = all.AsQueryable();
                if (dm.Skip > 0)
                {
                    data = data.Skip(dm.Skip);
                }
                if (dm.Take > 0)
                {
                    data = data.Take(dm.Take);
                }

                var result = data.ToList();
                _logger.LogDebug("Returning {Count} items after paging (total: {Total})", result.Count, all.Count);
                
                return new DataResult { Result = result, Count = all.Count };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ItemTypesAdaptor.ReadAsync");
                throw;
            }
        }

        public override async Task<object> InsertAsync(DataManager dm, object value, string? key)
        {
            var json = value as IDictionary<string, object>;
            var code = json != null && json.ContainsKey("ItemTypeCode") ? json["ItemTypeCode"]?.ToString() ?? string.Empty : string.Empty;
            var name = json != null && json.ContainsKey("ItemTypeName") ? json["ItemTypeName"]?.ToString() ?? string.Empty : string.Empty;
            var id = await _service.CreateAsync(code, name);
            return new { Id = id };
        }

        public override async Task<object> UpdateAsync(DataManager dm, object value, string keyField, string? key)
        {
            var json = value as IDictionary<string, object>;
            if (json == null || !json.ContainsKey("Id")) return value;
            if (!Guid.TryParse(json["Id"]?.ToString(), out var id)) return value;
            var code = json.ContainsKey("ItemTypeCode") ? json["ItemTypeCode"]?.ToString() ?? string.Empty : string.Empty;
            var name = json.ContainsKey("ItemTypeName") ? json["ItemTypeName"]?.ToString() ?? string.Empty : string.Empty;
            await _service.UpdateAsync(id, code, name);
            return value;
        }

        public override async Task<object> RemoveAsync(DataManager dm, object value, string keyField, string? key)
        {
            if (!Guid.TryParse(value?.ToString(), out var id)) return value;
            await _service.DeleteAsync(id);
            return value;
        }
    }
}