using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Syncfusion.Blazor.Data;
using ValyanERP.Web.Features.Rapoarte.Stoc.Models;

namespace ValyanERP.Web.Features.Rapoarte.Stoc.Repositories
{
    public interface IStocRepository
    {
        Task<IEnumerable<StocReportItemDto>> GetStocAsync(Guid? punctLucruId, Guid? tipArticolId, decimal? minStoc);
        Task<DataResult> GetPagedAsync(int skip, int take, string? sortField, string? sortDirection, Guid? punctLucruId, Guid? tipArticolId, decimal? minStoc);
    }
}
