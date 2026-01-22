using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ValyanERP.Web.Features.Rapoarte.Stoc.Models;
using ValyanERP.Web.Features.Rapoarte.Stoc.Services;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ValyanERP.Web.Controllers
{
    [ApiController]
    [Route("api/rapoarte")]
    [Authorize]
    public class RapoarteController : ControllerBase
    {
        private readonly IStocService _stocService;
        private readonly ILogger<RapoarteController> _logger;

        public RapoarteController(IStocService stocService, ILogger<RapoarteController> logger)
        {
            _stocService = stocService;
            _logger = logger;
        }

        [HttpGet("stoc")]
        public async Task<ActionResult<IEnumerable<StocReportItemDto>>> GetStoc([FromQuery] Guid? punctLucruId, [FromQuery] Guid? tipArticolId, [FromQuery] decimal? minStoc)
        {
            try
            {
                var data = await _stocService.GetStocAsync(punctLucruId, tipArticolId, minStoc);
                return Ok(data);
            }
            catch
            {
                return StatusCode(500, "Eroare server la generarea raportului stoc.");
            }
        }

        // SfDataManager UrlAdaptor may send POST requests with a DataManagerRequest-like payload.
        // Implement server-side handling (filtering, searching, sorting, paging) and return { result, count }.
        [HttpPost("stoc")]
        public async Task<ActionResult> PostStoc([FromBody] JsonElement dmPayload)
        {
            try
            {
                // Log raw JSON payload for mapping verification
                try
                {
                    var raw = dmPayload.GetRawText();
                    _logger.LogDebug("PostStoc raw payload: {Payload}", raw);
                }
                catch (Exception) { /* ignore logging errors */ }

                // Try to deserialize into our DTO (case-insensitive)
                DataManagerRequestDto? dm = null;
                try
                {
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    dm = JsonSerializer.Deserialize<DataManagerRequestDto>(dmPayload.GetRawText(), options);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to deserialize DataManagerRequestDto, will attempt to extract filters dynamically.");
                }

                // If deserialization failed or produced null, attempt to extract Where/Sorted/Search from raw payload
                if (dm == null)
                {
                    dm = new DataManagerRequestDto();
                    // Extract Where array if present
                    var whereList = ExtractWhereFromJson(dmPayload);
                    if (whereList != null && whereList.Any()) dm.Where = whereList;

                    var sortedList = ExtractSortedFromJson(dmPayload);
                    if (sortedList != null && sortedList.Any()) dm.Sorted = sortedList;

                    var searchList = ExtractSearchFromJson(dmPayload);
                    if (searchList != null && searchList.Any()) dm.Search = searchList;

                    // Skip/Take
                    if (dmPayload.TryGetProperty("skip", out var skipProp) && skipProp.TryGetInt32(out var sk)) dm.Skip = sk;
                    if (dmPayload.TryGetProperty("take", out var takeProp) && takeProp.TryGetInt32(out var tk)) dm.Take = tk;
                }

                // Build filter params from dm.Where
                Guid? punctLucruId = null;
                Guid? tipArticolId = null;
                decimal? minStoc = null;

                if (dm?.Where != null)
                {
                    foreach (var f in dm.Where)
                    {
                        try
                        {
                            if (string.Equals(f.Field, "PunctLucruId", StringComparison.OrdinalIgnoreCase))
                            {
                                if (Guid.TryParse(Convert.ToString(f.Value), out var g)) punctLucruId = g;
                            }
                            else if (string.Equals(f.Field, "TipArticolId", StringComparison.OrdinalIgnoreCase))
                            {
                                if (Guid.TryParse(Convert.ToString(f.Value), out var g2)) tipArticolId = g2;
                            }
                            else if (string.Equals(f.Field, "MinStoc", StringComparison.OrdinalIgnoreCase) || string.Equals(f.Field, "minStoc", StringComparison.OrdinalIgnoreCase))
                            {
                                if (decimal.TryParse(Convert.ToString(f.Value), out var d)) minStoc = d;
                            }
                        }
                        catch { }
                    }
                }

                var data = await _stocService.GetStocAsync(punctLucruId, tipArticolId, minStoc);
                var list = data?.ToList() ?? new List<StocReportItemDto>();

                // If no DataManagerRequest provided, return full data
                if (dm == null)
                {
                    return Ok(new { result = list, count = list.Count });
                }

                // Basic search implementation (search across Cod and Denumire)
                if (dm.Search != null && dm.Search.Any())
                {
                    foreach (var s in dm.Search)
                    {
                        var term = Convert.ToString(s.Value) ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(term))
                        {
                            list = list.Where(x => (x.Cod ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase) || (x.Denumire ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
                        }
                    }
                }

                // Apply sorting
                if (dm.Sorted != null && dm.Sorted.Any())
                {
                    IOrderedEnumerable<StocReportItemDto>? ordered = null;
                    for (int i = 0; i < dm.Sorted.Count; i++)
                    {
                        var sort = dm.Sorted[i];
                        Func<StocReportItemDto, object?> keySelector = item => GetPropertyValue(item, sort.Name ?? sort.Field);
                        if (i == 0)
                        {
                            ordered = string.Equals(sort.Direction, "desc", StringComparison.OrdinalIgnoreCase) ? list.OrderByDescending(keySelector) : list.OrderBy(keySelector);
                        }
                        else if (ordered != null)
                        {
                            ordered = string.Equals(sort.Direction, "desc", StringComparison.OrdinalIgnoreCase) ? ordered.ThenByDescending(keySelector) : ordered.ThenBy(keySelector);
                        }
                    }

                    if (ordered != null) list = ordered.ToList();
                }

                var totalCount = list.Count;

                // Apply paging (skip/take)
                if (dm.Skip > 0) list = list.Skip(dm.Skip).ToList();
                if (dm.Take > 0) list = list.Take(dm.Take).ToList();

                // Return shape expected by Syncfusion UrlAdaptor
                return Ok(new { result = list, count = totalCount });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in PostStoc");
                return StatusCode(500, "Eroare server la generarea raportului stoc.");
            }
        }

        // Minimal DTOs to bind DataManagerRequest payload from SfDataManager
        public class DataManagerRequestDto
        {
            public List<WhereFilterDto>? Where { get; set; }
            public List<SortDescriptorDto>? Sorted { get; set; }
            public List<object>? Group { get; set; }
            public List<SearchDescriptorDto>? Search { get; set; }
            public int Skip { get; set; }
            public int Take { get; set; }
            public bool LazyLoad { get; set; }
            public bool RequiresCounts { get; set; }
        }

        public class WhereFilterDto
        {
            public string? Field { get; set; }
            public string? Operator { get; set; }
            public object? Value { get; set; }
        }

        public class SortDescriptorDto
        {
            public string? Name { get; set; }
            public string? Field { get; set; }
            public string? Direction { get; set; }
        }

        public class SearchDescriptorDto
        {
            public string? Field { get; set; }
            public object? Value { get; set; }
            public string? Operator { get; set; }
        }

        private static object? GetPropertyValue(object obj, string? propertyName)
        {
            if (obj == null || string.IsNullOrWhiteSpace(propertyName)) return null;
            var prop = obj.GetType().GetProperty(propertyName, System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            return prop?.GetValue(obj);
        }

        // Helpers to extract arrays from arbitrary JSON payloads (used when deserialization fails)
        private static List<WhereFilterDto>? ExtractWhereFromJson(JsonElement payload)
        {
            try
            {
                if (payload.TryGetProperty("where", out var whereProp))
                {
                    var list = new List<WhereFilterDto>();
                    if (whereProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var e in whereProp.EnumerateArray())
                        {
                            var field = e.GetProperty("field").GetString();
                            object? value = null;
                            if (e.TryGetProperty("value", out var v))
                            {
                                value = v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();
                            }
                            list.Add(new WhereFilterDto { Field = field, Value = value });
                        }

                        return list;
                    }
                }
            }
            catch { }

            return null;
        }

        private static List<SortDescriptorDto>? ExtractSortedFromJson(JsonElement payload)
        {
            try
            {
                if (payload.TryGetProperty("sorted", out var sortedProp))
                {
                    var list = new List<SortDescriptorDto>();
                    if (sortedProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var e in sortedProp.EnumerateArray())
                        {
                            var field = e.GetProperty("field").GetString();
                            var dir = e.TryGetProperty("direction", out var d) ? d.GetString() : null;
                            list.Add(new SortDescriptorDto { Field = field, Direction = dir });
                        }

                        return list;
                    }
                }
            }
            catch { }

            return null;
        }

        private static List<SearchDescriptorDto>? ExtractSearchFromJson(JsonElement payload)
        {
            try
            {
                if (payload.TryGetProperty("search", out var searchProp))
                {
                    var list = new List<SearchDescriptorDto>();
                    if (searchProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var e in searchProp.EnumerateArray())
                        {
                            object? value = null;
                            if (e.TryGetProperty("value", out var v))
                            {
                                value = v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();
                            }
                            list.Add(new SearchDescriptorDto { Value = value });
                        }

                        return list;
                    }
                }
            }
            catch { }

            return null;
        }
    }
}
