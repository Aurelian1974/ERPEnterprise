using System.Net;
using System.Security.Cryptography.X509Certificates;
using Erp.Setup.Core.Health;
using Erp.Setup.Windows.Iis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Erp.Setup.Tests.Hosting;

/// <summary>Health-check-ul installer-ului (8.14) contra unui server HTTPS real pe loopback, cu SNI pe numele gazdei.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class InstallerHealthCheckTests : IAsyncDisposable
{
    private const string HostName = "erp-test.firma.local";
    private const string Version = "2027.1.0";
    private const string Schema = "core/2027.01.004_Add_User_IsSystem.sql";

    private readonly X509Certificate2 certificate;
    private WebApplication? app;
    private int readyCalls;

    public InstallerHealthCheckTests()
    {
        using var ephemeral = SelfSignedCertificate.Create(HostName, DateTimeOffset.UtcNow);
        // SChannel nu foloseste chei efemere pentru TLS server; certificatul se reincarca din PFX.
        certificate = X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pkcs12), null);
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null)
        {
            await app.DisposeAsync();
        }

        certificate.Dispose();
    }

    private string? SniSeen { get; set; }

    private async Task<int> StartAsync(Func<int, string> readyJson, string pagesContentType = "text/html")
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(https =>
        {
            https.ServerCertificateSelector = (_, name) =>
            {
                SniSeen = name;
                return certificate;
            };
        })));
        app = builder.Build();
        app.MapGet("/health/ready", (HttpContext context) =>
        {
            var json = readyJson(Interlocked.Increment(ref readyCalls));
            return Results.Content(json, "application/json", statusCode: json.Contains("\"Healthy\",\"version\"", StringComparison.Ordinal) ? 200 : 503);
        });
        app.MapGet("/", () => Results.Content("<html></html>", pagesContentType));
        app.MapGet("/account/login", (HttpContext context) => Results.Content("<html></html>", pagesContentType));
        await app.StartAsync(TestContext.Current.CancellationToken);
        return new Uri(app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()).Port;
    }

    private static string Ready(string status = "Healthy", string version = Version, string schema = Schema, string worker = "Healthy") =>
        "{\"status\":\"" + status + "\",\"version\":\"" + version + "\",\"schemaVersion\":\"" + schema
        + "\",\"checks\":{\"database\":\"Healthy\",\"schema\":\"Healthy\",\"license\":\"Healthy\",\"worker\":\"" + worker + "\"}}";

    private InstallerHealthCheck Client(int port, string? thumbprint) => new(HostName, port, thumbprint, TimeProvider.System);

    private static Task<HealthCheckOutcome> Wait(InstallerHealthCheck client, TimeSpan? timeout = null) =>
        client.WaitUntilHealthyAsync(Version, Schema, TestContext.Current.CancellationToken, TimeSpan.FromMilliseconds(50), timeout ?? TimeSpan.FromSeconds(5));

    [Fact]
    public async Task Healthy_application_with_pinned_self_signed_certificate_passes_using_sni()
    {
        var port = await StartAsync(_ => Ready());
        using var client = Client(port, certificate.Thumbprint);

        var outcome = await Wait(client);

        Assert.True(outcome.Succeeded, outcome.Message);
        Assert.Equal(HostName, SniSeen);
        Assert.Equal(Schema, outcome.LastResponse!.SchemaVersion);
    }

    [Fact]
    public async Task Application_becoming_healthy_after_a_few_polls_passes()
    {
        var port = await StartAsync(call => call < 3 ? Ready("Unhealthy", worker: "Unhealthy") : Ready());
        using var client = Client(port, certificate.Thumbprint);

        var outcome = await Wait(client);

        Assert.True(outcome.Succeeded, outcome.Message);
        Assert.True(readyCalls >= 3);
    }

    [Fact]
    public async Task Untrusted_or_differently_pinned_certificate_fails()
    {
        var port = await StartAsync(_ => Ready());
        using var untrusted = Client(port, null);
        using var otherPin = Client(port, new string('A', 40));

        Assert.False((await Wait(untrusted, TimeSpan.FromMilliseconds(300))).Succeeded);
        Assert.False((await Wait(otherPin, TimeSpan.FromMilliseconds(300))).Succeeded);
    }

    [Fact]
    public async Task Unhealthy_checks_and_version_mismatch_are_reported()
    {
        var port = await StartAsync(_ => Ready("Unhealthy", version: "2026.4.2", worker: "Unhealthy"));
        using var client = Client(port, certificate.Thumbprint);

        var outcome = await Wait(client, TimeSpan.FromMilliseconds(300));

        Assert.False(outcome.Succeeded);
        Assert.Contains("worker", outcome.Message);
        Assert.Contains("2026.4.2", outcome.Message);
    }

    [Fact]
    public async Task Pages_must_answer_html()
    {
        var port = await StartAsync(_ => Ready(), "application/json");
        using var client = Client(port, certificate.Thumbprint);

        var outcome = await Wait(client);

        Assert.False(outcome.Succeeded);
        Assert.Contains("200 HTML", outcome.Message);
    }

    [Fact]
    public void Problems_compare_version_and_schema_exactly()
    {
        var response = new HealthReadyResponse("Healthy", Version, "core/x.sql", new Dictionary<string, string> { ["database"] = "Healthy" });

        Assert.Equal(string.Empty, InstallerHealthCheck.Problems(response with { SchemaVersion = Schema }, Version, Schema));
        Assert.Contains("core/x.sql", InstallerHealthCheck.Problems(response, Version, Schema));
        Assert.Contains("Răspuns gol", InstallerHealthCheck.Problems(null, Version, Schema));
    }
}
