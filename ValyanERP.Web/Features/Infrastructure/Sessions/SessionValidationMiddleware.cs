using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using System;
using Microsoft.AspNetCore.Authentication;

namespace ValyanERP.Web.Features.Infrastructure.Sessions;

public class SessionValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IServiceProvider _serviceProvider;

    public SessionValidationMiddleware(RequestDelegate next, IServiceProvider serviceProvider)
    {
        _next = next;
        _serviceProvider = serviceProvider;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            // Only check if user is authenticated and cookie exists
            if (context.User?.Identity?.IsAuthenticated == true)
            {
                if (context.Request.Cookies.TryGetValue("ValyanERP.Session", out var cookie))
                {
                    if (Guid.TryParse(cookie, out var token))
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var sessionService = scope.ServiceProvider.GetRequiredService<ISessionService>();
                        var session = await sessionService.ValidateTokenAsync(token);
                        
                        if (session == null)
                        {
                            // Session invalid - sign the user out and delete cookie
                            await context.SignOutAsync();
                            context.Response.Cookies.Delete("ValyanERP.Session");

                            // Redirect to login
                            context.Response.Redirect("/Account/Login");
                            return;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR validating session: " + ex);
        }

        await _next(context);
    }
}