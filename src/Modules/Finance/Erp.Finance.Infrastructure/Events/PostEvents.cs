using Erp.Core.Contracts;
using Erp.Finance.Application.Accounting;
using Erp.Finance.Contracts.Settlement;
using Erp.Finance.Contracts.Treasury;

namespace Erp.Finance.Infrastructure.Events;

/// <summary>
/// Consumatorul contabil (02.02, C2 4.1): fiecare eveniment cu model de contare devine o nota validata sau un rand in coada „De contat”.
/// Idempotent pe EventId (core.InboxProcessed si indexul unic din fin.JournalEntry). Reevaluarea valutara si efectele comerciale vin dupa
/// (RAMAS-DE-FACUT); alocarile si avansurile acordate nu produc nota (sunt acoperite de plata si de evenimentele efectelor).
/// </summary>
public sealed class PostEvents(PostingService posting) :
    IIntegrationEventHandler<FacturaEmisa>, IIntegrationEventHandler<FacturaFurnizorValidata>, IIntegrationEventHandler<IncasareInregistrata>,
    IIntegrationEventHandler<PlataInregistrata>, IIntegrationEventHandler<TvaLaIncasareExigibil>, IIntegrationEventHandler<DiferentaCursInregistrata>,
    IIntegrationEventHandler<CompensareValidata>, IIntegrationEventHandler<DecontCheltuieliValidat>
{
    public Task HandleAsync(FacturaEmisa evt, CancellationToken ct) => posting.PostAsync(PostingAdapters.SalesInvoice(evt), ct);

    public Task HandleAsync(FacturaFurnizorValidata evt, CancellationToken ct) => posting.PostAsync(PostingAdapters.SupplierInvoice(evt), ct);

    public Task HandleAsync(IncasareInregistrata evt, CancellationToken ct) => posting.PostAsync(PostingAdapters.Receipt(evt), ct);

    public Task HandleAsync(PlataInregistrata evt, CancellationToken ct) => posting.PostAsync(PostingAdapters.Payment(evt), ct);

    public Task HandleAsync(TvaLaIncasareExigibil evt, CancellationToken ct) => posting.PostAsync(PostingAdapters.VatOnCollection(evt), ct);

    public Task HandleAsync(DiferentaCursInregistrata evt, CancellationToken ct) =>
        PostingAdapters.FxDifference(evt) is { } request ? posting.PostAsync(request, ct) : Task.CompletedTask;

    public Task HandleAsync(CompensareValidata evt, CancellationToken ct) => posting.PostAsync(PostingAdapters.Compensation(evt), ct);

    public Task HandleAsync(DecontCheltuieliValidat evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return posting.PostAsync(PostingAdapters.ExpenseReport(evt, DateOnly.FromDateTime(evt.OccurredAtUtc)), ct);
    }
}
