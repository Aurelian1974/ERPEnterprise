using Microsoft.AspNetCore.Components.Server.Circuits;
using System.Threading.Tasks;
using System;
using Microsoft.Extensions.DependencyInjection;

namespace ValyanERP.Web.Infrastructure.Identity;

public class SessionCircuitHandler : CircuitHandler
{
    private readonly IServiceProvider _serviceProvider;

    public SessionCircuitHandler(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        try
        {
            var circuitId = circuit?.Id;
            if (!string.IsNullOrWhiteSpace(circuitId))
            {
                using var scope = _serviceProvider.CreateScope();
                var sessionService = scope.ServiceProvider.GetRequiredService<Features.Infrastructure.Sessions.ISessionService>();
                sessionService.InvalidateByCircuitIdAsync(circuitId);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR invalidating session by circuit: " + ex);
        }

        return Task.CompletedTask;
    }
}