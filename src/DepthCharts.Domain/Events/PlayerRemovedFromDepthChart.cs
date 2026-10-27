namespace DepthCharts.Domain.Events;

public sealed record PlayerRemovedFromDepthChart(
    Sport Sport, TeamId TeamId, PlayerId PlayerId, Position Position, DateTimeOffset OccurredAt) : IDomainEvent;
