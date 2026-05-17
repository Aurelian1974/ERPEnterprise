namespace Shared.Contracts.Events.HR;

public sealed record EmployeeCreatedIntegrationEvent(
    Guid EventId,
    DateTime OccurredOnUtc,
    Guid TenantId,
    Guid EmployeeId,
    string FullName,
    string Email) : Events.IIntegrationEvent;
