using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;

namespace Erp.Setup.Core.Health;

/// <summary>Raspunsul /health/ready (8.14), citit de installer.</summary>
public sealed record HealthReadyResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("schemaVersion")] string? SchemaVersion,
    [property: JsonPropertyName("checks")] IReadOnlyDictionary<string, string>? Checks);

public sealed record HealthCheckOutcome(bool Succeeded, string Message, HealthReadyResponse? LastResponse);

/// <summary>
/// Health-check-ul installer-ului (8.14, I-23 si U-14): /health/ready la 5 s, maximum 180 s, conectat la 127.0.0.1 dar cu SNI
/// si Host = hostName, ca sa nu depinda de DNS. Certificatul se valideaza normal; un certificat autosemnat permis se accepta
/// doar dupa amprenta. Apoi GET / si GET /account/login trebuie sa raspunda 200 HTML.
/// </summary>
public sealed class InstallerHealthCheck : IDisposable
{
    private readonly HttpClient client;
    private readonly Uri baseAddress;
    private readonly TimeProvider time;

    public InstallerHealthCheck(string hostName, int httpsPort, string? pinnedThumbprint, TimeProvider time, IPAddress? connectTo = null)
    {
        this.time = time;
        baseAddress = new Uri($"https://{hostName}:{httpsPort}/");
        var target = new IPEndPoint(connectTo ?? IPAddress.Loopback, httpsPort);
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ConnectCallback = async (_, ct) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(target, ct).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, certificate, _, errors) => pinnedThumbprint is null
                    ? errors == SslPolicyErrors.None
                    : certificate is X509Certificate2 x509 && string.Equals(x509.Thumbprint, pinnedThumbprint, StringComparison.OrdinalIgnoreCase),
            },
        };
        client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<HealthCheckOutcome> WaitUntilHealthyAsync(string expectedVersion, string? expectedSchemaVersion, CancellationToken ct,
        TimeSpan? interval = null, TimeSpan? timeout = null)
    {
        var deadline = time.GetUtcNow() + (timeout ?? TimeSpan.FromSeconds(180));
        HealthReadyResponse? last = null;
        var lastProblem = "Aplicația nu a răspuns.";
        while (true)
        {
            try
            {
                using var response = await client.GetAsync(new Uri(baseAddress, "health/ready"), ct).ConfigureAwait(false);
                last = await response.Content.ReadFromJsonAsync<HealthReadyResponse>(ct).ConfigureAwait(false);
                lastProblem = Problems(last, expectedVersion, expectedSchemaVersion);
                if (lastProblem.Length == 0)
                {
                    return await CheckPagesAsync(last!, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                lastProblem = $"Aplicația nu răspunde corect: {ex.Message}";
            }

            if (time.GetUtcNow() >= deadline)
            {
                return new HealthCheckOutcome(false, lastProblem, last);
            }

            await Task.Delay(interval ?? TimeSpan.FromSeconds(5), time, ct).ConfigureAwait(false);
        }
    }

    internal static string Problems(HealthReadyResponse? response, string expectedVersion, string? expectedSchemaVersion)
    {
        if (response is null)
        {
            return "Răspuns gol de la /health/ready.";
        }

        var problems = new List<string>();
        if (!string.Equals(response.Status, "Healthy", StringComparison.Ordinal))
        {
            var unhealthy = response.Checks?.Where(c => !string.Equals(c.Value, "Healthy", StringComparison.Ordinal)).Select(c => c.Key) ?? [];
            problems.Add($"verificări care nu sunt Healthy: {string.Join(", ", unhealthy)}");
        }

        if (!string.Equals(response.Version, expectedVersion, StringComparison.Ordinal))
        {
            problems.Add($"versiunea aplicației este {response.Version}, nu {expectedVersion}");
        }

        if (expectedSchemaVersion is not null && !string.Equals(response.SchemaVersion, expectedSchemaVersion, StringComparison.Ordinal))
        {
            problems.Add($"versiunea bazei este {response.SchemaVersion}, nu {expectedSchemaVersion}");
        }

        return problems.Count == 0 ? string.Empty : string.Join("; ", problems) + ".";
    }

    private async Task<HealthCheckOutcome> CheckPagesAsync(HealthReadyResponse ready, CancellationToken ct)
    {
        foreach (var path in new[] { "", "account/login" })
        {
            using var response = await client.GetAsync(new Uri(baseAddress, path), ct).ConfigureAwait(false);
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (response.StatusCode != HttpStatusCode.OK || !string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase))
            {
                return new HealthCheckOutcome(false, $"GET /{path} a răspuns {(int)response.StatusCode} ({mediaType}), nu 200 HTML.", ready);
            }
        }

        return new HealthCheckOutcome(true, $"Aplicația {ready.Version} răspunde; toate verificările sunt Healthy.", ready);
    }

    public void Dispose() => client.Dispose();
}
