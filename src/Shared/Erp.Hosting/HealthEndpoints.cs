using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;

namespace Erp.Hosting;

public enum HealthStatus
{
    Healthy,
    Unhealthy,
}

/// <summary>O verificare din /health/ready (8.14); numele e cheia din „checks”.</summary>
public interface IReadinessCheck
{
    string Name { get; }

    Task<HealthStatus> CheckAsync(CancellationToken ct);
}

/// <summary>Raspunsul /health/ready: contract fix intre installer si Erp.Host (8.14).</summary>
public sealed record ReadinessReport(
    [property: JsonPropertyName("status")] HealthStatus Status,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("schemaVersion")] string? SchemaVersion,
    [property: JsonPropertyName("checks")] IReadOnlyDictionary<string, HealthStatus> Checks)
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>database: conexiune deschisa si SELECT 1 in sub 2 s.</summary>
public sealed class DatabaseCheck(Func<SqlConnection> connectionFactory) : IReadinessCheck
{
    public string Name => "database";

    public async Task<HealthStatus> CheckAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        var watch = Stopwatch.StartNew();
        try
        {
            await using var connection = connectionFactory();
            await connection.OpenAsync(timeout.Token).ConfigureAwait(false);
            await using var command = new SqlCommand("SELECT 1;", connection) { CommandTimeout = 2 };
            await command.ExecuteScalarAsync(timeout.Token).ConfigureAwait(false);
            return watch.Elapsed < TimeSpan.FromSeconds(2) ? HealthStatus.Healthy : HealthStatus.Unhealthy;
        }
        catch (Exception ex) when (ex is SqlException or OperationCanceledException or InvalidOperationException && !ct.IsCancellationRequested)
        {
            return HealthStatus.Unhealthy;
        }
    }
}

/// <summary>schema: ultima migrare din core.SchemaVersion = lastMigration incorporat (10.5). Citita la fiecare cerere.</summary>
/// <param name="allowUnversionedBuild">Aceeasi regula ca SchemaVersionGuard, doar in mediul Development.</param>
public sealed class SchemaCheck(ISchemaVersionSource reader, ErpBuildInfo build, bool allowUnversionedBuild = false) : IReadinessCheck
{
    public string Name => "schema";

    public string? LastActual { get; private set; }

    public async Task<HealthStatus> CheckAsync(CancellationToken ct)
    {
        try
        {
            LastActual = await reader.ReadAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException && !ct.IsCancellationRequested)
        {
            LastActual = null;
            return HealthStatus.Unhealthy;
        }

        return SchemaVersionGuard.Evaluate(build.LastMigration, LastActual, allowUnversionedBuild) == SchemaCompatibility.Compatible
            ? HealthStatus.Healthy
            : HealthStatus.Unhealthy;
    }
}

/// <summary>license: licenta activa din baza e valida si incarcata (08, 5.14); starea vine din aplicatie.</summary>
public sealed class LicenseCheck(Func<bool> isValid) : IReadinessCheck
{
    public string Name => "license";

    public Task<HealthStatus> CheckAsync(CancellationToken ct) =>
        Task.FromResult(isValid()
            ? HealthStatus.Healthy
            : HealthStatus.Unhealthy);
}

/// <summary>worker: serverul Hangfire al serviciului are heartbeat in ultimele 60 s (hangfire.Server).</summary>
public sealed class WorkerCheck(Func<SqlConnection> connectionFactory) : IReadinessCheck
{
    public const string Query = """
        SELECT CAST(CASE WHEN EXISTS (
            SELECT 1 FROM hangfire.[Server] AS s WHERE s.LastHeartbeat >= DATEADD(SECOND, -60, SYSUTCDATETIME())) THEN 1 ELSE 0 END AS bit);
        """;

    public string Name => "worker";

    public async Task<HealthStatus> CheckAsync(CancellationToken ct)
    {
        try
        {
            await using var connection = connectionFactory();
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var command = new SqlCommand(Query, connection) { CommandTimeout = 5 };
            return (bool)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))! ? HealthStatus.Healthy : HealthStatus.Unhealthy;
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException && !ct.IsCancellationRequested)
        {
            return HealthStatus.Unhealthy;
        }
    }
}

/// <summary>/health/live si /health/ready (8.14). Raspunsul detaliat doar pentru cererile de pe loopback.</summary>
public static class HealthEndpoints
{
    /// <param name="checks">Verificarile se creeaza la fiecare cerere (SchemaCheck pastreaza versiunea citita).</param>
    public static IEndpointRouteBuilder MapErpHealth(this IEndpointRouteBuilder endpoints, ErpBuildInfo build, Func<IReadOnlyList<IReadinessCheck>> checks)
    {
        endpoints.MapGet("/health/live", () => Results.Ok(new { status = nameof(HealthStatus.Healthy) }));
        endpoints.MapGet("/health/ready", async (HttpContext context) =>
        {
            var report = await BuildReportAsync(build, checks(), context.RequestAborted);
            var statusCode = report.Status == HealthStatus.Healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.CacheControl = "no-store";
            return MaintenanceMiddleware.IsLoopback(context)
                ? Results.Json(report, ReadinessReport.JsonOptions, statusCode: statusCode)
                : Results.Json(new { status = report.Status.ToString() }, statusCode: statusCode);
        });
        return endpoints;
    }

    public static async Task<ReadinessReport> BuildReportAsync(ErpBuildInfo build, IReadOnlyList<IReadinessCheck> checks, CancellationToken ct)
    {
        var results = new Dictionary<string, HealthStatus>(StringComparer.Ordinal);
        foreach (var check in checks)
        {
            results[check.Name] = await check.CheckAsync(ct).ConfigureAwait(false);
        }

        var schemaVersion = checks.OfType<SchemaCheck>().FirstOrDefault()?.LastActual;
        var status = results.Values.All(s => s == HealthStatus.Healthy) ? HealthStatus.Healthy : HealthStatus.Unhealthy;
        return new ReadinessReport(status, build.Version, schemaVersion, results);
    }
}
