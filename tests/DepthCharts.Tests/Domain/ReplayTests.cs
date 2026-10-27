using DepthCharts.Domain;
using DepthCharts.Domain.Events;

namespace DepthCharts.Tests.Domain;

public class ReplayTests
{
    static readonly Sport NFL = new("NFL");
    static readonly TeamId TB = new("tb");
    static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    static Player Pl(int i) => new(new PlayerId(i), $"P{i}", null);
    static Position P(string p) => new(p);

    static void Run(DepthChart c, List<IDomainEvent> log, IReadOnlyList<IDomainEvent> events)
    {
        foreach (var e in events) c.Apply(e);
        log.AddRange(events);
    }

    static string Snapshot(DepthChart c) =>
        string.Join(";", c.GetFullChart().Select(kv => $"{kv.Key}={string.Join(",", kv.Value.Select(p => p.Id.Value))}"));

    [Fact]
    public void Replay_EmittedEvents_RebuildsEqualState()
    {
        var a = new DepthChart(NFL, TB);
        var log = new List<IDomainEvent>();
        Run(a, log, a.AddPlayer(Pl(1), P("WR"), 0, T0));
        Run(a, log, a.AddPlayer(Pl(2), P("WR"), 0, T0));
        Run(a, log, a.AddPlayer(Pl(3), P("WR"), 2, T0));
        Run(a, log, a.AddPlayer(Pl(1), P("KR"), null, T0));
        Run(a, log, a.AddPlayer(Pl(3), P("WR"), 0, T0));
        Run(a, log, a.RemovePlayer(new PlayerId(2), P("WR"), T0));
        Run(a, log, a.RemovePlayer(new PlayerId(9), P("WR"), T0));

        var b = DepthChart.Replay(NFL, TB, log);

        Assert.Equal(Snapshot(a), Snapshot(b));
        Assert.Equal(6, a.Version);
        Assert.Equal(6, b.Version);
    }

    [Fact]
    public void Replay_FilteredByOccurredAt_GivesEarlierChart()
    {
        var a = new DepthChart(NFL, TB);
        var log = new List<IDomainEvent>();
        Run(a, log, a.AddPlayer(Pl(1), P("WR"), null, T0));
        Run(a, log, a.AddPlayer(Pl(2), P("WR"), 0, T0.AddMinutes(1)));
        var afterTwo = Snapshot(a);
        Run(a, log, a.RemovePlayer(new PlayerId(1), P("WR"), T0.AddMinutes(2)));

        var past = DepthChart.Replay(NFL, TB, log.Where(e => e.OccurredAt <= T0.AddMinutes(1)));

        Assert.Equal(afterTwo, Snapshot(past));
    }

    [Fact]
    public void Replay_NoEvents_IsEmptyChart()
    {
        var c = DepthChart.Replay(NFL, TB, []);
        Assert.Empty(c.GetFullChart());
        Assert.Equal(0, c.Version);
    }
}
