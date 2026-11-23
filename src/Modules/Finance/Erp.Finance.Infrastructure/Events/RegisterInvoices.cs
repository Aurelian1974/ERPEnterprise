using Erp.Finance.Application.Settlement;
using Erp.Finance.Contracts.Settlement;
using Erp.Core.Contracts;

namespace Erp.Finance.Infrastructure.Events;

/// <summary>
/// Consumatorii facturilor (02.02, F3 4.1, 5.2): FacturaEmisa (Vanzari) si FacturaFurnizorValidata (Achizitii) devin partide deschise, idempotent pe
/// document (si pe EventId, prin core.InboxProcessed), apoi storno pe factura stornata si platile nealocate pe factura noua.
/// </summary>
public sealed class RegisterInvoices(InvoiceOpenItems invoices) : IIntegrationEventHandler<FacturaEmisa>, IIntegrationEventHandler<FacturaFurnizorValidata>
{
    public Task HandleAsync(FacturaEmisa evt, CancellationToken ct) => invoices.RegisterAsync(evt, ct);

    public Task HandleAsync(FacturaFurnizorValidata evt, CancellationToken ct) => invoices.RegisterAsync(evt, ct);
}
