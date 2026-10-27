using System.Globalization;
using DepthCharts.Application;
using DepthCharts.Domain;

namespace DepthCharts.Api;

public sealed record PlayerResponse(int PlayerId, string Name);

public static class DepthChartEndpoints
{
    public static IEndpointRouteBuilder MapDepthChartEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/depth-charts/{sport}/{team}", async (string sport, string team, string? asOf,
            DepthChartQueryHandler handler, CancellationToken ct) =>
        {
            if (!TryParseAsOf(asOf, out var at)) return InvalidAsOf();
            var chart = await handler.GetFullChartAsync(new GetFullDepthChartQuery(sport, team, at), ct);
            return Results.Ok(chart.ToDictionary(e => e.Key.Value, e => e.Value.Select(ToResponse).ToArray()));
        });

        app.MapGet("/depth-charts/{sport}/{team}/positions/{position}/players/{playerId:int}/below",
            async (string sport, string team, string position, int playerId, string? asOf,
                DepthChartQueryHandler handler, CancellationToken ct) =>
        {
            if (!TryParseAsOf(asOf, out var at)) return InvalidAsOf();
            var players = await handler.GetPlayersUnderAsync(
                new GetPlayersUnderQuery(sport, team, position, playerId, at), ct);
            return Results.Ok(players.Select(ToResponse).ToArray());
        });

        return app;
    }

    // ISO-8601 only; a lenient TryParse would read "10/11/2026" as a US date. Offset (K) is optional, defaulting to UTC.
    private static readonly string[] IsoFormats =
    [
        "yyyy-MM-dd",
        "yyyy-MM-dd'T'HH:mmK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
    ];

    private static PlayerResponse ToResponse(Player p) => new(p.Id, p.Name);

    private static IResult InvalidAsOf() => TypedResults.Problem(statusCode: 400, title: "Invalid asOf");

    private static bool TryParseAsOf(string? asOf, out DateTimeOffset? at)
    {
        at = null;
        if (string.IsNullOrEmpty(asOf)) return true;
        // An unencoded '+' in the offset arrives as a space; spaces are otherwise invalid, so restore it.
        if (!DateTimeOffset.TryParseExact(asOf.Replace(' ', '+'), IsoFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return false;
        at = parsed;
        return true;
    }
}
