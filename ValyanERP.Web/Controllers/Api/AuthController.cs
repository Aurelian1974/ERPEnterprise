using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ValyanERP.Web.Features.Infrastructure.Sessions;
using ValyanERP.Web.Features.Infrastructure.Security.Services;
using ValyanERP.Web.Infrastructure.Identity;

namespace ValyanERP.Web.Controllers.Api;

/// <summary>
/// API Controller for authentication operations.
/// Handles login via HTTP POST which can set Identity cookies correctly.
/// </summary>
[Route("api/auth")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ISessionService _sessionService;
    private readonly IWorkingContextService _workingContextService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ISessionService sessionService,
        IWorkingContextService workingContextService,
        ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _sessionService = sessionService;
        _workingContextService = workingContextService;
        _logger = logger;
    }

    /// <summary>
    /// Login endpoint - validates credentials and sets authentication cookie
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { success = false, message = "Email și parola sunt obligatorii." });
            }

            // Find user by email
            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user == null)
            {
                _logger.LogWarning("Login failed: user not found for email {Email}", request.Email);
                return Unauthorized(new { success = false, message = "Email sau parolă incorectă." });
            }

            // Check lockout
            if (await _userManager.IsLockedOutAsync(user))
            {
                _logger.LogWarning("Login failed: account locked for {Email}", request.Email);
                return Unauthorized(new { success = false, message = "Contul este blocat temporar. Încercați mai târziu." });
            }

            // Attempt sign in (this sets the Identity cookie!)
            var result = await _signInManager.PasswordSignInAsync(
                user,
                request.Password,
                isPersistent: false, // Session cookie
                lockoutOnFailure: true);

            if (result.Succeeded)
            {
                // Create custom session
                var session = await _sessionService.CreateSessionAsync(user.Id);
                
                // Set custom session cookie
                Response.Cookies.Append("ValyanERP.Session", session.Token.ToString(), new CookieOptions
                {
                    HttpOnly = true,
                    Secure = Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    Path = "/"
                });
                
                // Save working context entity in cookie (will be read by Blazor)
                if (!string.IsNullOrEmpty(request.EntityId) && !string.IsNullOrEmpty(request.EntityType))
                {
                    Response.Cookies.Append("ValyanERP.WorkingContext", $"{request.EntityType}:{request.EntityId}", new CookieOptions
                    {
                        HttpOnly = false, // Allow JavaScript to read if needed
                        Secure = Request.IsHttps,
                        SameSite = SameSiteMode.Lax,
                        Path = "/",
                        Expires = DateTimeOffset.UtcNow.AddDays(7) // Persist for a week
                    });
                    
                    _logger.LogInformation("Working context cookie set for {Email}: {EntityType}={EntityId}", 
                        request.Email, request.EntityType, request.EntityId);
                }

                _logger.LogInformation("Login successful for {Email}", request.Email);
                return Ok(new { success = true, redirectUrl = "/" });
            }
            else if (result.IsLockedOut)
            {
                return Unauthorized(new { success = false, message = "Contul este blocat temporar. Încercați mai târziu." });
            }
            else
            {
                _logger.LogWarning("Login failed: invalid password for {Email}", request.Email);
                return Unauthorized(new { success = false, message = "Email sau parolă incorectă." });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login error");
            return StatusCode(500, new { success = false, message = "A apărut o eroare. Încercați din nou." });
        }
    }
}

/// <summary>
/// Login request model
/// </summary>
public class LoginRequest
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string? EntityId { get; set; }
    public string? EntityType { get; set; }
}
