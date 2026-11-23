using Erp.Core.Infrastructure.Messaging;

namespace Erp.Worker.Jobs;

/// <summary>
/// Procesarea outbox-ului (02.01, 4.5): o trecere la fiecare 2 secunde (configurabil, Outbox:IntervalSeconds), cat timp procesarea de fundal
/// e pornita (baza compatibila, fara mentenanta). Un lot plin se reia imediat; o eroare a trecerii se jurnalizeaza si nu opreste bucla.
/// </summary>
public sealed class OutboxProcessorLoop(OutboxDispatcher dispatcher, TimeSpan interval, ILogger<OutboxProcessorLoop> log)
{
    public IAsyncDisposable Start()
    {
        var stop = new CancellationTokenSource();
        var running = Task.Run(() => RunAsync(stop.Token));
        return new Running(stop, running);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await dispatcher.RunOnceAsync(ct).ConfigureAwait(false);
                if (result.Fetched == OutboxDispatcher.DefaultBatchSize)
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Trecerea procesatorului de outbox a eșuat.");
            }

            try
            {
                await Task.Delay(interval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private sealed class Running(CancellationTokenSource stop, Task running) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await stop.CancelAsync().ConfigureAwait(false);
            await running.ConfigureAwait(false);
            stop.Dispose();
        }
    }
}
