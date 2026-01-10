using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ValyanERP.Web.Controllers.Api;

[Route("api/sessions")]
[ApiController]
public class SessionsController : ControllerBase
{
    private readonly Features.Infrastructure.Sessions.ISessionService _sessionService;
    private readonly ILogger<SessionsController> _logger;

    public SessionsController(Features.Infrastructure.Sessions.ISessionService sessionService, ILogger<SessionsController> logger)
    {
        _sessionService = sessionService;
        _logger = logger;
    }

    [HttpPost("invalidate")]
    public async Task<IActionResult> Invalidate()
    {
        try
        {
            var cookie = Request.Cookies["ValyanERP.Session"];
            if (string.IsNullOrWhiteSpace(cookie)) return BadRequest();

            if (!Guid.TryParse(cookie, out var token)) return BadRequest();

            await _sessionService.InvalidateByTokenAsync(token);
            _logger.LogInformation("Session invalidated for token {token}", token);

            // Remove cookie client-side; set expired cookie in response
            Response.Cookies.Delete("ValyanERP.Session");
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate session");
            return StatusCode(500);
        }
    }

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat()
    {
        try
        {
            var cookie = Request.Cookies["ValyanERP.Session"];
            if (string.IsNullOrWhiteSpace(cookie)) return BadRequest();

            if (!Guid.TryParse(cookie, out var token)) return BadRequest();

            await _sessionService.RefreshHeartbeatAsync(token);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh heartbeat");
            return StatusCode(500);
        }
    }
}