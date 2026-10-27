namespace DepthCharts.Domain.Events;

public sealed record PlayerAddedToDepthChart(
    Sport Sport, TeamId TeamId, Player Player, Position Position, int Depth, DateTimeOffset OccurredAt) : IDomainEvent;
