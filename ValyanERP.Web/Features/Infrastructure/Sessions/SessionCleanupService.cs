using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Threading;
using System;
using Microsoft.Extensions.DependencyInjection;

namespace ValyanERP.Web.Features.Infrastructure.Sessions;

public class SessionCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SessionCleanupService> _logger;

    public SessionCleanupService(IServiceProvider serviceProvider, ILogger<SessionCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<ISessionsRepository>();

                // Invalidate sessions that have not sent heartbeat in the last 90 seconds (Romania time)
                var now = ValyanERP.Web.Infrastructure.Time.TimeUtils.NowRomania();
                var threshold = now.AddSeconds(-90);
                await repo.InvalidateStaleSessionsAsync(threshold);

                // Delete sessions older than 7 days that are inactive
                var deleteThreshold = now.AddDays(-7);
                await repo.DeleteExpiredAsync(deleteThreshold);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Session cleanup failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }

    // ⚠️ SECURITY: Invalidate ALL sessions when application stops
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogWarning("Application stopping - invalidating all active sessions for security");
        
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ISessionsRepository>();
            
            // Invalidate all active sessions
            await repo.InvalidateAllSessionsAsync();
            
            _logger.LogInformation("All sessions invalidated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate sessions on application stop");
        }

        await base.StopAsync(cancellationToken);
    }
}