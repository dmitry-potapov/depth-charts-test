using DepthCharts.Application.Abstractions;
using DepthCharts.Domain;

namespace DepthCharts.Application;

public sealed record GetFullDepthChartQuery(string Sport, string Team, DateTimeOffset? AsOf);

public sealed record GetPlayersUnderQuery(string Sport, string Team, string Position, int PlayerId, DateTimeOffset? AsOf);

public sealed class DepthChartQueryHandler(IEventStore store)
{
    public async Task<IReadOnlyDictionary<Position, IReadOnlyList<Player>>> GetFullChartAsync(
        GetFullDepthChartQuery query, CancellationToken ct)
    {
        var (sport, team) = (new Sport(query.Sport), new Team(query.Team));
        return (await LoadAsync(sport, team, query.AsOf, ct)).GetFullChart();
    }

    public async Task<IReadOnlyList<Player>> GetPlayersUnderAsync(GetPlayersUnderQuery query, CancellationToken ct)
    {
        var (sport, team, position) = (new Sport(query.Sport), new Team(query.Team), new Position(query.Position));
        return (await LoadAsync(sport, team, query.AsOf, ct)).GetPlayersUnder(query.PlayerId, position);
    }

    private async Task<DepthChart> LoadAsync(Sport sport, Team team, DateTimeOffset? asOf, CancellationToken ct) =>
        DepthChart.Replay(sport, team, await store.LoadAsync(DepthChart.StreamIdFor(sport, team), asOf, ct));
}
