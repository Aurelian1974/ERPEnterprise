using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Syncfusion.Blazor;
using Syncfusion.Blazor.Data;
using ValyanERP.Web.Features.Administrare.TipuriArticole.Services;

namespace ValyanERP.Web.Features.Administrare.TipuriArticole.Adaptors
{
    public class ItemTypesAdaptor : DataAdaptor
    {
        private readonly IItemTypesService _service;

        public ItemTypesAdaptor(IItemTypesService service)
        {
            _service = service;
        }

        public override async Task<object> ReadAsync(DataManagerRequest dm, string? key = null)
        {
            var all = (await _service.GetAllAsync()).ToList();

            // Simple in-memory filtering/sorting/paging for now
            var result = all.AsQueryable();

            // Filtering: not implementing full DataManager parsing yet; repository returns all active items and grid will handle simple client-side filters

            var data = result.ToList();
            return new DataResult { Result = data, Count = data.Count };
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