namespace Shared.Contracts.Events.Finance;

public sealed record InvoicePaidIntegrationEvent(
    Guid EventId,
    DateTime OccurredOnUtc,
    Guid TenantId,
    Guid InvoiceId,
    decimal Amount,
    string Currency) : Events.IIntegrationEvent;
