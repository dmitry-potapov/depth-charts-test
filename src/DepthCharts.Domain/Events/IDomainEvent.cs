namespace DepthCharts.Domain.Events;

public interface IDomainEvent
{
    Sport Sport { get; }
    TeamId TeamId { get; }
    DateTimeOffset OccurredAt { get; }
}
