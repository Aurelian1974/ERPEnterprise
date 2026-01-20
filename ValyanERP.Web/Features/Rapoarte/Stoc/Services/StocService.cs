using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ValyanERP.Web.Features.Rapoarte.Stoc.Models;
using ValyanERP.Web.Features.Rapoarte.Stoc.Repositories;

namespace ValyanERP.Web.Features.Rapoarte.Stoc.Services
{
    public class StocService : IStocService
    {
        private readonly IStocRepository _repo;

        public StocService(IStocRepository repo)
        {
            _repo = repo;
        }

        public async Task<IEnumerable<StocReportItemDto>> GetStocAsync(Guid? punctLucruId, Guid? tipArticolId, decimal? minStoc)
        {
            // Minimal validation / defaulting
            if (minStoc <= 0) minStoc = null;

            var data = await _repo.GetStocAsync(punctLucruId, tipArticolId, minStoc);
            return data;
        }
    }
}
