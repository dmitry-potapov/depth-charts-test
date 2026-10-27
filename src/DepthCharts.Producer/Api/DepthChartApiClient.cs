using System.Net.Http.Json;

namespace DepthCharts.Producer.Api;

public sealed record PlayerDto(int PlayerId, string Name);

public sealed class DepthChartApiClient(HttpClient http)
{
    public async Task<Dictionary<string, List<PlayerDto>>> GetChartAsync(
        string sport, string team, DateTimeOffset? asOf, CancellationToken ct) =>
        await GetAsync<Dictionary<string, List<PlayerDto>>>(ChartUri(sport, team, asOf), ct);

    public async Task<List<PlayerDto>> GetBelowAsync(
        string sport, string team, string position, int playerId, DateTimeOffset? asOf, CancellationToken ct) =>
        await GetAsync<List<PlayerDto>>(BelowUri(sport, team, position, playerId, asOf), ct);

    public static string ChartUri(string sport, string team, DateTimeOffset? asOf) =>
        $"depth-charts/{Esc(sport)}/{Esc(team)}{AsOfQuery(asOf)}";

    public static string BelowUri(string sport, string team, string position, int playerId, DateTimeOffset? asOf) =>
        $"depth-charts/{Esc(sport)}/{Esc(team)}/positions/{Esc(position)}/players/{playerId}/below{AsOfQuery(asOf)}";

    private static string Esc(string s) => Uri.EscapeDataString(s);

    private static string AsOfQuery(DateTimeOffset? asOf) =>
        asOf is { } t ? $"?asOf={Uri.EscapeDataString(t.ToString("O"))}" : "";

    private async Task<T> GetAsync<T>(string uri, CancellationToken ct)
    {
        using var response = await http.GetAsync(uri, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(ct))!;
    }
}
