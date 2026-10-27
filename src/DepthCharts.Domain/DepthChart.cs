using DepthCharts.Domain.Events;

namespace DepthCharts.Domain;

public sealed class DepthChart
{
    // Keys are never removed so positions keep first-added order.
    private readonly Dictionary<Position, List<Player>> _chart = [];

    public DepthChart(Sport sport, Team team)
    {
        Sport = sport ?? throw new DomainValidationException("Sport is required.");
        Team = team ?? throw new DomainValidationException("Team is required.");
    }

    public Sport Sport { get; }
    public Team Team { get; }
    public string StreamId => StreamIdFor(Sport, Team);
    public int Version { get; private set; }

    public static string StreamIdFor(Sport sport, Team team) =>
        $"{sport.Value}:{team.Value}".ToLowerInvariant();

    public static DepthChart Replay(Sport sport, Team team, IEnumerable<IDomainEvent> events)
    {
        var chart = new DepthChart(sport, team);
        foreach (var e in events) chart.Apply(e);
        return chart;
    }

    public IReadOnlyList<IDomainEvent> AddPlayer(Player player, Position position, int? depth, DateTimeOffset occurredAt)
    {
        if (player is null) throw new DomainValidationException("Player is required.");
        if (position is null) throw new DomainValidationException("Position is required.");
        if (depth < 0) throw new DomainValidationException("Depth must not be negative.");

        var others = _chart.TryGetValue(position, out var list)
            ? list.Count(p => p.Id != player.Id)
            : 0;
        var resolved = depth is null || depth > others ? others : depth.Value;
        return [new PlayerAddedToDepthChart(Sport, Team, player, position, resolved, occurredAt)];
    }

    public IReadOnlyList<IDomainEvent> RemovePlayer(int playerId, Position position, DateTimeOffset occurredAt)
    {
        if (playerId <= 0) throw new DomainValidationException("Player id must be positive.");
        if (position is null) throw new DomainValidationException("Position is required.");

        return _chart.TryGetValue(position, out var list) && list.Any(p => p.Id == playerId)
            ? [new PlayerRemovedFromDepthChart(Sport, Team, playerId, position, occurredAt)]
            : [];
    }

    public void Apply(IDomainEvent @event)
    {
        if (@event.Sport != Sport || @event.Team != Team)
            throw new InvalidOperationException($"Event for {StreamIdFor(@event.Sport, @event.Team)} cannot be applied to {StreamId}.");

        switch (@event)
        {
            case PlayerAddedToDepthChart added:
                if (!_chart.TryGetValue(added.Position, out var list))
                    _chart[added.Position] = list = [];
                list.RemoveAll(p => p.Id == added.Player.Id);
                // Clamp both ends: a filtered or corrupt stream may carry any depth.
                list.Insert(Math.Clamp(added.Depth, 0, list.Count), added.Player);
                // A player has one name across all positions: latest event wins.
                foreach (var other in _chart.Values)
                    for (var i = 0; i < other.Count; i++)
                        if (other[i].Id == added.Player.Id) other[i] = added.Player;
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

    public IReadOnlyList<Player> GetPlayersUnder(int playerId, Position position)
    {
        if (playerId <= 0) throw new DomainValidationException("Player id must be positive.");
        if (position is null) throw new DomainValidationException("Position is required.");
        if (!_chart.TryGetValue(position, out var list)) return [];
        var index = list.FindIndex(p => p.Id == playerId);
        return index < 0 ? [] : list.Skip(index + 1).ToList();
    }
}
