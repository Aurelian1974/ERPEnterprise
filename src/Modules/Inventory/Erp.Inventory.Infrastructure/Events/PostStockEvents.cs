using Erp.Core.Contracts;
using Erp.Finance.Contracts.Accounting;
using Erp.Inventory.Contracts;

namespace Erp.Inventory.Infrastructure.Events;

/// <summary>
/// Contarea evenimentelor Stocurilor (02.05, S3 3.6; C2 4.1): blocul de contare al fiecarui eveniment ajunge la C2 prin portul
/// IEventAccountingPort (modelul tipului de eveniment, idempotent pe EventId, coada „De contat” la esec). Evenimentele fara bloc nu se conteaza.
/// </summary>
public sealed class PostStockEvents(IEventAccountingPort accounting) : IIntegrationEventHandler<MiscareStocValidata>, IIntegrationEventHandler<CostStocAjustat>,
    IIntegrationEventHandler<DescarcareAmanuntValidata>, IIntegrationEventHandler<ModificarePretAmanuntValidata>
{
    public Task HandleAsync(MiscareStocValidata evt, CancellationToken ct) => Post(evt, nameof(MiscareStocValidata), evt.Accounting, ct);

    public Task HandleAsync(CostStocAjustat evt, CancellationToken ct) => Post(evt, nameof(CostStocAjustat), evt.Accounting, ct);

    public Task HandleAsync(DescarcareAmanuntValidata evt, CancellationToken ct) => Post(evt, nameof(DescarcareAmanuntValidata), evt.Accounting, ct);

    public Task HandleAsync(ModificarePretAmanuntValidata evt, CancellationToken ct) => Post(evt, nameof(ModificarePretAmanuntValidata), evt.Accounting, ct);

    private Task Post(IntegrationEvent evt, string type, AccountingPayload? payload, CancellationToken ct) =>
        payload is null || payload.Components.Count == 0 ? Task.CompletedTask : accounting.PostAsync(evt.EventId, type, evt.CompanyId, payload, ct);
}
