using DepthCharts.Producer.Api;

namespace DepthCharts.Producer.Cli;

public static class ChartFormatter
{
    public static string FormatIds(IEnumerable<PlayerDto> players) =>
        $"[{string.Join(", ", players.Select(p => p.PlayerId))}]";

    public static string FormatChart(IReadOnlyDictionary<string, List<PlayerDto>> chart) =>
        string.Join(Environment.NewLine, chart.Select(e => $"{e.Key}: {FormatIds(e.Value)}"));
}
