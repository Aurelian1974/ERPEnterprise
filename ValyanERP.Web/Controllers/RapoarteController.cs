using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ValyanERP.Web.Features.Rapoarte.Stoc.Models;
using ValyanERP.Web.Features.Rapoarte.Stoc.Services;

namespace ValyanERP.Web.Controllers
{
    [ApiController]
    [Route("api/rapoarte")]
    [Authorize]
    public class RapoarteController : ControllerBase
    {
        private readonly IStocService _stocService;

        public RapoarteController(IStocService stocService)
        {
            _stocService = stocService;
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
    }
}
