namespace DepthCharts.Domain.Events;

public sealed record PlayerRemovedFromDepthChart(
    Sport Sport, Team Team, int PlayerId, Position Position, DateTimeOffset OccurredAt) : IDomainEvent;
