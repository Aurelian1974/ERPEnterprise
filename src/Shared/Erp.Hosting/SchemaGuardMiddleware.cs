using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Erp.Hosting;

/// <summary>
/// Partea de business a Erp.Host (/api/*) raspunde 503 cat timp versiunea bazei nu corespunde aplicatiei (10.5).
/// Health-check-ul, paginile statice si autentificarea raman disponibile, ca problema sa poata fi diagnosticata.
/// </summary>
public sealed class SchemaGuardMiddleware(RequestDelegate next, SchemaVersionGuard guard)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) || guard.IsCompatible)
        {
            await next(context);
            return;
        }

        await Results.Problem(
                title: "Versiunea bazei de date nu corespunde aplicației.",
                detail: "Aplicația nu pornește partea de business până la rularea actualizării (ErpSetup.exe upgrade sau repair).",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                extensions: new Dictionary<string, object?> { ["code"] = "CORE_SCHEMA_VERSION_MISMATCH" })
            .ExecuteAsync(context);
    }
}

/// <summary>Procesarea in fundal a Erp.Worker (serverul Hangfire); pornita si oprita de <see cref="BackgroundProcessingLifecycle"/>.</summary>
public interface IBackgroundProcessingFactory
{
    IAsyncDisposable Start();
}

/// <summary>
/// Erp.Worker porneste procesarea doar cu baza compatibila (10.5) si fara maintenance.flag (10.4); cat timp fisierul exista,
/// procesarea e oprita, deci nu pornesc joburi noi. Starea se reevalueaza la fiecare interval.
/// </summary>
public sealed class BackgroundProcessingLifecycle(
    IBackgroundProcessingFactory factory,
    SchemaVersionGuard guard,
    MaintenanceFlag maintenance,
    ILogger<BackgroundProcessingLifecycle> log,
    TimeProvider time,
    TimeSpan? interval = null) : BackgroundService
{
    private IAsyncDisposable? running;

    public bool IsRunning => running is not null;

    public async Task ReconcileAsync()
    {
        var allowed = guard.IsCompatible && !maintenance.IsActive;
        if (allowed && running is null)
        {
            running = factory.Start();
            log.LogInformation("Procesarea în fundal a pornit");
        }
        else if (!allowed && running is not null)
        {
            await running.DisposeAsync().ConfigureAwait(false);
            running = null;
            log.LogWarning("Procesarea în fundal s-a oprit ({Reason})", maintenance.IsActive ? "mentenanță" : "versiunea bazei nu corespunde");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await ReconcileAsync().ConfigureAwait(false);
                await Task.Delay(interval ?? TimeSpan.FromSeconds(5), time, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (running is not null)
            {
                await running.DisposeAsync().ConfigureAwait(false);
                running = null;
            }
        }
    }
}
