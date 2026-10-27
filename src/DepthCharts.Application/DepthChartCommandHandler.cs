using DepthCharts.Application.Abstractions;
using DepthCharts.Application.Contracts;
using DepthCharts.Domain;
using DepthCharts.Domain.Events;

namespace DepthCharts.Application;

public sealed class DepthChartCommandHandler(IEventStore store)
{
    public async Task HandleAsync(DepthChartCommand command, DateTimeOffset occurredAt, CancellationToken ct)
    {
        // A missing "commandId" deserialises to Guid.Empty and would collide with every other id-less command.
        if (command.CommandId == Guid.Empty) throw new DomainValidationException("CommandId is required.");
        if (await store.HasProcessedAsync(command.CommandId, ct)) return;

        var sport = new Sport(command.Sport);
        var team = new Team(command.Team);
        var position = new Position(command.Position);
        // Value types are built before loading; chart invariants are checked before anything is appended.
        Func<DepthChart, IReadOnlyList<IDomainEvent>> execute = command switch
        {
            AddPlayerCommand add => AddPlayer(add, position, occurredAt),
            RemovePlayerCommand remove => chart => chart.RemovePlayer(remove.PlayerId, position, occurredAt),
            _ => throw new InvalidOperationException($"Unknown command type {command.GetType().Name}."),
        };

        var streamId = DepthChart.StreamIdFor(sport, team);
        var chart = DepthChart.Replay(sport, team, await store.LoadAsync(streamId, null, ct));
        var events = execute(chart);
        // Appended even when empty so a no-op is still recorded as processed and not replayed on redelivery.
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
