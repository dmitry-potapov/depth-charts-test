using DepthCharts.Application.Abstractions;
using DepthCharts.Application.Contracts;
using DepthCharts.Producer.Api;
using DepthCharts.Producer.Cli;

namespace DepthCharts.Producer;

public sealed class DemoRunner(
    ICommandPublisher publisher, DepthChartApiClient api, TextWriter output, TextWriter error, TimeSpan? timeoutAfter = null)
{
    private const string Sport = "NFL";
    // Fresh team per run, so state left by an earlier run can't satisfy the readiness check.
    private readonly string _team = $"demo-{Guid.NewGuid():N}";

    public async Task<int> RunAsync(CancellationToken ct)
    {
        DepthChartCommand[] commands =
        [
            Add(1, "Bob", "WR", 0),
            Add(2, "Alice", "WR", 0),
            Add(3, "Charlie", "WR", 2),
            Add(1, "Bob", "KR", null),
        ];
        foreach (var command in commands) await publisher.PublishAsync(command, ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(timeoutAfter ?? TimeSpan.FromSeconds(10));
        try
        {
            while (true)
            {
                var chart = await api.GetChartAsync(Sport, _team, null, timeout.Token);
                if (chart.GetValueOrDefault("WR")?.Count == 3 && chart.GetValueOrDefault("KR")?.Count == 1)
                {
                    output.WriteLine(ChartFormatter.FormatChart(chart));
                    var below = await api.GetBelowAsync(Sport, _team, "WR", 2, null, timeout.Token);
                    output.WriteLine($"below Alice WR: {ChartFormatter.FormatIds(below)}");
                    return 0;
                }
                await Task.Delay(250, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await error.WriteLineAsync("Timed out waiting for the Api to process the demo commands.");
            return 1;
        }
    }

    private AddPlayerCommand Add(int id, string name, string position, int? depth) =>
        new(Guid.NewGuid(), Sport, _team, position, id, name, null, depth);
}
