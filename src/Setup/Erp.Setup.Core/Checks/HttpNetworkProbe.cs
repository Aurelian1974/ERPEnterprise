namespace Erp.Setup.Core.Checks;

/// <summary>C-19: cereri HTTPS prin proxy-ul sistemului; orice raspuns HTTP inseamna ca adresa e accesibila.</summary>
public sealed class HttpNetworkProbe : INetworkProbe, IDisposable
{
    private readonly HttpClient client = new(new SocketsHttpHandler { UseProxy = true }) { Timeout = Timeout.InfiniteTimeSpan };

    public async Task<bool> CanReachAsync(HttpMethod method, Uri uri, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            using var request = new HttpRequestMessage(method, uri);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return false;
        }
    }

    public void Dispose() => client.Dispose();
}
