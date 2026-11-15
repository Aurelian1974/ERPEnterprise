using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Http;

namespace Erp.Hosting;

/// <summary>Fisierul maintenance.flag (10.4), verificat cel mult o data la 2 s.</summary>
public sealed class MaintenanceFlag(string? flagPath, TimeProvider time)
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);

    private readonly Lock gate = new();
    private DateTimeOffset checkedAt = DateTimeOffset.MinValue;
    private bool active;

    public bool IsActive
    {
        get
        {
            if (string.IsNullOrEmpty(flagPath))
            {
                return false;
            }

            lock (gate)
            {
                var now = time.GetUtcNow();
                if (now - checkedAt >= CheckInterval)
                {
                    active = File.Exists(flagPath);
                    checkedAt = now;
                }

                return active;
            }
        }
    }
}

/// <summary>
/// Primul middleware din pipeline (10.4): cat timp exista maintenance.flag, orice cerere care nu e /health/* de pe loopback
/// primeste 503, Retry-After: 300 si pagina maintenance.html.
/// </summary>
public sealed class MaintenanceMiddleware(RequestDelegate next, MaintenanceFlag flag)
{
    private static readonly Lazy<byte[]> Page = new(LoadPage);

    public async Task InvokeAsync(HttpContext context)
    {
        if (!flag.IsActive || (IsHealthPath(context.Request.Path) && IsLoopback(context)))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.RetryAfter = "300";
        context.Response.Headers.CacheControl = "no-store";
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.Body.WriteAsync(Page.Value, context.RequestAborted);
    }

    internal static bool IsHealthPath(PathString path) => path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);

    internal static bool IsLoopback(HttpContext context) =>
        context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address);

    private static byte[] LoadPage()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Erp.Hosting.maintenance.html")
            ?? throw new InvalidOperationException("maintenance.html lipsește din Erp.Hosting.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
