using Erp.Core.Contracts;
using Erp.Finance.Application.Accounting;
using Erp.Finance.Contracts.Periods;
using Erp.Finance.Domain.Accounting;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Erp.Finance.Infrastructure.Accounting;

/// <summary>
/// Reincercarea automata a cozii „De contat” (C2 3.7, 4.4): la 15 minute, randurile in asteptare incercate ultima oara cu peste 10 minute in
/// urma (in afara lunilor inchise, care asteapta redeschiderea), fiecare in tranzactia lui. Abatere de la 4.4 pasul 3 („dupa salvarea
/// configurarii”): jobul acopera orice schimbare (cont, rol, model, articol) fara ca fiecare ecran sa stie de coada.
/// </summary>
public sealed class PostingQueueRetry(IServiceScopeFactory scopes, TimeProvider clock, ILogger<PostingQueueRetry> log)
{
    public const string JobId = "fin-posting-queue-retry";

    public const string Schedule = "*/15 * * * *";

    public async Task<int> RunAsync(CancellationToken ct)
    {
        IReadOnlyList<QueuedEvent> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            due = await scope.ServiceProvider.GetRequiredService<IPostingStore>()
                .DueAsync(new QueueFilter(OlderThan: clock.GetUtcNow().UtcDateTime.AddMinutes(-10), SkipClosedPeriod: true), ct).ConfigureAwait(false);
        }

        var posted = 0;
        foreach (var row in due)
        {
            if (await RetryOneAsync(scopes, row, ct).ConfigureAwait(false))
            {
                posted++;
            }
        }

        if (due.Count > 0)
        {
            log.LogInformation("Coada de contare: {Posted} din {Count} evenimente contate la reîncercare.", posted, due.Count);
        }

        return posted;
    }

    /// <summary>Reincearca un rand prin comanda RetryPostingQueue, in scope-ul si tranzactia ei, cu contextul de sistem al firmei.</summary>
    private static async Task<bool> RetryOneAsync(IServiceScopeFactory scopes, QueuedEvent row, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ISystemContextScope>().Enter(row.CompanyId);
        return (await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RetryPostingQueue(row.Id), ct).ConfigureAwait(false)).Posted > 0;
    }
}

/// <summary>Redeschiderea unei luni (C3, C2 4.4 pasul 4): evenimentele asteptand pentru luna inchisa se reincearca.</summary>
public sealed class RetryQueueOnReopen(IPostingStore store, PostingService posting) : IIntegrationEventHandler<PerioadaContabilaRedeschisa>
{
    public async Task HandleAsync(PerioadaContabilaRedeschisa evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        foreach (var row in await store.DueAsync(new QueueFilter(evt.CompanyId, Reason: PostingFailureReason.ClosedPeriod, From: evt.StartDate, To: evt.EndDate), ct)
                     .ConfigureAwait(false))
        {
            await posting.RetryAsync(row, ct).ConfigureAwait(false);
        }
    }
}
