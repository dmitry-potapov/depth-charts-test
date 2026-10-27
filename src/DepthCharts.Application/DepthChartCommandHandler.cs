using DepthCharts.Application.Abstractions;
using DepthCharts.Application.Contracts;
using DepthCharts.Domain;
using DepthCharts.Domain.Events;

namespace DepthCharts.Application;

public sealed class DepthChartCommandHandler(IEventStore store)
{
    public async Task HandleAsync(DepthChartCommand command, DateTimeOffset occurredAt, CancellationToken ct)
    {
        if (await store.HasProcessedAsync(command.CommandId, ct)) return;

        var sport = new Sport(command.Sport);
        var team = new Team(command.Team);
        var position = new Position(command.Position);
        // Build everything up front so invalid input never touches the store.
        Func<DepthChart, IReadOnlyList<IDomainEvent>> execute = command switch
        {
            AddPlayerCommand add => AddPlayer(add, position, occurredAt),
            RemovePlayerCommand remove => chart => chart.RemovePlayer(remove.PlayerId, position, occurredAt),
            _ => throw new InvalidOperationException($"Unknown command type {command.GetType().Name}."),
        };

        var streamId = DepthChart.StreamIdFor(sport, team);
        var chart = DepthChart.Replay(sport, team, await store.LoadAsync(streamId, null, ct));
        var events = execute(chart);
        if (events.Count > 0)
            await store.AppendAsync(streamId, chart.Version, command.CommandId, events, ct);
    }

    private static Func<DepthChart, IReadOnlyList<IDomainEvent>> AddPlayer(
        AddPlayerCommand add, Position position, DateTimeOffset occurredAt)
    {
        var player = new Player(add.PlayerId, add.PlayerName,
            add.PlayerPosition is null ? null : new Position(add.PlayerPosition));
        return chart => chart.AddPlayer(player, position, add.Depth, occurredAt);
    }
}
