namespace DepthCharts.Domain.Events;

public interface IDomainEvent
{
    Sport Sport { get; }
    Team Team { get; }
    DateTimeOffset OccurredAt { get; }
}
