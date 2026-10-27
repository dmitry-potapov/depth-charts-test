using DepthCharts.Domain.Events;

namespace DepthCharts.Domain;

public sealed class DepthChart(Sport sport, TeamId teamId)
{
    // Keys are never removed so positions keep first-added order.
    private readonly Dictionary<Position, List<Player>> _chart = [];

    public Sport Sport { get; } = sport;
    public TeamId TeamId { get; } = teamId;
    public string StreamId => StreamIdFor(Sport, TeamId);
    public int Version { get; private set; }

    public static string StreamIdFor(Sport sport, TeamId teamId) =>
        $"{sport.Value}:{teamId.Value}".ToLowerInvariant();

    public static DepthChart Replay(Sport sport, TeamId teamId, IEnumerable<IDomainEvent> events)
    {
        var chart = new DepthChart(sport, teamId);
        foreach (var e in events) chart.Apply(e);
        return chart;
    }

    public IReadOnlyList<IDomainEvent> AddPlayer(Player player, Position position, int? depth, DateTimeOffset occurredAt)
    {
        if (depth < 0) throw new DomainValidationException("Depth must not be negative.");

        var others = _chart.TryGetValue(position, out var list)
            ? list.Count(p => p.Id != player.Id)
            : 0;
        var resolved = depth is null || depth > others ? others : depth.Value;
        return [new PlayerAddedToDepthChart(Sport, TeamId, player, position, resolved, occurredAt)];
    }

    public IReadOnlyList<IDomainEvent> RemovePlayer(PlayerId playerId, Position position, DateTimeOffset occurredAt) =>
        _chart.TryGetValue(position, out var list) && list.Any(p => p.Id == playerId)
            ? [new PlayerRemovedFromDepthChart(Sport, TeamId, playerId, position, occurredAt)]
            : [];

    public void Apply(IDomainEvent @event)
    {
        switch (@event)
        {
            case PlayerAddedToDepthChart added:
                if (!_chart.TryGetValue(added.Position, out var list))
                    _chart[added.Position] = list = [];
                list.RemoveAll(p => p.Id == added.Player.Id);
                list.Insert(added.Depth, added.Player);
                break;
            case PlayerRemovedFromDepthChart removed:
                if (_chart.TryGetValue(removed.Position, out var existing))
                    existing.RemoveAll(p => p.Id == removed.PlayerId);
                break;
            default:
                throw new InvalidOperationException($"Unknown event type {@event.GetType().Name}.");
        }
        Version++;
    }

    public IReadOnlyDictionary<Position, IReadOnlyList<Player>> GetFullChart() =>
        _chart.Where(kv => kv.Value.Count > 0)
            .ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Player>)kv.Value.ToList());

    public IReadOnlyList<Player> GetPlayersUnder(PlayerId playerId, Position position)
    {
        if (!_chart.TryGetValue(position, out var list)) return [];
        var index = list.FindIndex(p => p.Id == playerId);
        return index < 0 ? [] : list.Skip(index + 1).ToList();
    }
}
