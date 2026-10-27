namespace DepthCharts.Domain.Events;

public sealed record PlayerAddedToDepthChart(
    Sport Sport, Team Team, Player Player, Position Position, int Depth, DateTimeOffset OccurredAt) : IDomainEvent;
