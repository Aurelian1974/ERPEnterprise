using Dapper;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using ValyanERP.Web.Infrastructure.Data;
using ValyanERP.Web.Features.Rapoarte.Stoc.Models;

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
    }
}
