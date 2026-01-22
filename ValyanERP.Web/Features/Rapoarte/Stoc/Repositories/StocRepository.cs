using Dapper;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using ValyanERP.Web.Infrastructure.Data;
using ValyanERP.Web.Features.Rapoarte.Stoc.Models;
using Syncfusion.Blazor.Data;
using System.Linq;

namespace ValyanERP.Web.Features.Rapoarte.Stoc.Repositories
{
    public class StocRepository : IStocRepository
    {
        private readonly DapperContext _context;

        public StocRepository(DapperContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<StocReportItemDto>> GetStocAsync(System.Guid? punctLucruId, System.Guid? tipArticolId, decimal? minStoc)
        {
            using var conn = _context.CreateConnection();
            var result = await conn.QueryAsync<StocReportItemDto>(
                "sp_Rapoarte_Stoc_Get", 
                new { PunctLucruId = punctLucruId, TipArticolId = tipArticolId, MinStoc = minStoc },
                commandType: CommandType.StoredProcedure);

            return result;
        }

        public async Task<DataResult> GetPagedAsync(int skip, int take, string? sortField, string? sortDirection, Guid? punctLucruId, Guid? tipArticolId, decimal? minStoc)
        {
            using var conn = _context.CreateConnection();
            try
            {
                // Try to call a dedicated paged stored procedure if it exists
                var parameters = new DynamicParameters();
                parameters.Add("@PunctLucruId", punctLucruId);
                parameters.Add("@TipArticolId", tipArticolId);
                parameters.Add("@MinStoc", minStoc);
                parameters.Add("@SortField", sortField ?? "Cod");
                parameters.Add("@SortDirection", sortDirection ?? "ASC");
                parameters.Add("@Skip", skip);
                parameters.Add("@Take", take == 0 ? 10000 : take);

                using var multi = await conn.QueryMultipleAsync("sp_Rapoarte_Stoc_GetPaged", parameters, commandType: CommandType.StoredProcedure);
                var items = (await multi.ReadAsync<StocReportItemDto>()).ToList();
                var count = await multi.ReadFirstOrDefaultAsync<int>();

                return new DataResult { Result = items, Count = count };
            }
            catch (Exception)
            {
                // If dedicated paged SP is not available, fallback to calling the main SP and apply paging/sorting in-memory
            }

            // Fallback path: call existing SP that returns full stock, then apply sort/skip/take in memory
            var all = (await conn.QueryAsync<StocReportItemDto>(
                "sp_Rapoarte_Stoc_Get",
                new { PunctLucruId = punctLucruId, TipArticolId = tipArticolId, MinStoc = minStoc },
                commandType: CommandType.StoredProcedure)).ToList();

            var total = all.Count;

            // Apply sorting via reflection (case-insensitive property name)
            if (!string.IsNullOrWhiteSpace(sortField))
            {
                var prop = typeof(StocReportItemDto).GetProperties().FirstOrDefault(p => string.Equals(p.Name, sortField, StringComparison.OrdinalIgnoreCase));
                if (prop != null)
                {
                    if (string.Equals(sortDirection, "DESC", StringComparison.OrdinalIgnoreCase))
                        all = all.OrderByDescending(x => prop.GetValue(x)).ToList();
                    else
                        all = all.OrderBy(x => prop.GetValue(x)).ToList();
                }
            }
            else
            {
                all = all.OrderBy(x => x.Cod).ToList();
            }

            var paged = all.Skip(skip).Take(take == 0 ? total : take).ToList();
            return new DataResult { Result = paged, Count = total };
        }
    }
}
