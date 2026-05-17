using MediatR;

namespace Shared.Contracts.Events;

public interface IIntegrationEvent : INotification
{
    Guid EventId { get; }
    DateTime OccurredOnUtc { get; }
}
