namespace Shared.Contracts.Events.Inventory;

public sealed record StockLevelChangedIntegrationEvent(
    Guid EventId,
    DateTime OccurredOnUtc,
    Guid TenantId,
    Guid ProductId,
    decimal NewQuantity,
    decimal PreviousQuantity) : Events.IIntegrationEvent;
