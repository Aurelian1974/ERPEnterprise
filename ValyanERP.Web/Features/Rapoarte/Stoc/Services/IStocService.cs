using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ValyanERP.Web.Features.Rapoarte.Stoc.Models;

namespace ValyanERP.Web.Features.Rapoarte.Stoc.Services
{
    public interface IStocService
    {
        Task<IEnumerable<StocReportItemDto>> GetStocAsync(Guid? punctLucruId, Guid? tipArticolId, decimal? minStoc);
    }
}
