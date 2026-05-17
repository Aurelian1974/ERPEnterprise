using Shared.Kernel.Domain;

namespace Finance.Domain.DomainEvents;

public sealed record InvoiceCreatedDomainEvent(
    Guid EventId,
    DateTime OccurredOnUtc,
    Guid InvoiceId,
    Guid TenantId,
    Guid CustomerId) : IDomainEvent;

public sealed record InvoiceApprovedDomainEvent(
    Guid EventId,
    DateTime OccurredOnUtc,
    Guid InvoiceId,
    Guid TenantId) : IDomainEvent;

public sealed record InvoicePaidDomainEvent(
    Guid EventId,
    DateTime OccurredOnUtc,
    Guid InvoiceId,
    Guid TenantId,
    decimal Amount,
    string Currency) : IDomainEvent;
