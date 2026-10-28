using DepthCharts.Producer;
using DepthCharts.Producer.Api;

namespace DepthCharts.Tests.Producer;

public class DemoRunnerTests
{
    // The chart the demo's four commands produce, as the API returns it.
    public const string Ready =
        """{"WR":[{"playerId":2,"name":"Alice"},{"playerId":1,"name":"Bob"},{"playerId":3,"name":"Charlie"}],"KR":[{"playerId":1,"name":"Bob"}]}""";

    public const string Below = """[{"playerId":1,"name":"Bob"},{"playerId":3,"name":"Charlie"}]""";

    private static DemoRunner Runner(FakePublisher publisher, StubHandler handler, StringWriter output, StringWriter error, TimeSpan? timeout = null) =>
        new(publisher, new DepthChartApiClient(handler.Client()), output, error, timeout);

    [Fact]
    public async Task Run_PublishesThenPrintsChartAndBelow()
    {
        // Arrange
        var publisher = new FakePublisher();
        var handler = StubHandler.Json(uri => uri.EndsWith("/below") ? Below : Ready);
        var output = new StringWriter();

        // Act
        var code = await Runner(publisher, handler, output, new StringWriter()).RunAsync(default);

        // Assert
        Assert.Equal(0, code);
        Assert.Equal(4, publisher.Published.Count);
        Assert.Contains("below Alice WR:", output.ToString());
    }

    [Fact]
    public async Task Run_UsesConstantTeam_PrintsItAndQueriesIt()
    {
        // Arrange
        var publisher = new FakePublisher();
        var handler = StubHandler.Json(uri => uri.EndsWith("/below") ? "[]" : Ready);
        var output = new StringWriter();

        // Act
        await Runner(publisher, handler, output, new StringWriter()).RunAsync(default);

        // Assert
        Assert.All(publisher.Published, c => Assert.Equal("demo-team", c.Team));
        Assert.Contains("depth-charts/NFL/demo-team", handler.Uris[0]);
        Assert.Contains("sport: NFL, team: demo-team", output.ToString());
    }

    [Fact]
    public async Task Run_NeverReady_TimesOutWithExitCode1()
    {
        // Arrange
        var error = new StringWriter();
        var handler = StubHandler.Json(_ => "{}");

        // Act
        var code = await Runner(new FakePublisher(), handler, new StringWriter(), error, TimeSpan.FromMilliseconds(300)).RunAsync(default);

        // Assert
        Assert.Equal(1, code);
        Assert.Contains("Timed out", error.ToString());
    }

    [Fact]
    public async Task Run_CallerCancelled_PropagatesInsteadOfReportingTimeout()
    {
        // Arrange
        var error = new StringWriter();
        using var cts = new CancellationTokenSource(100);
        var handler = StubHandler.Json(_ => "{}");

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Runner(new FakePublisher(), handler, new StringWriter(), error).RunAsync(cts.Token));

        // Assert
        Assert.Empty(error.ToString());
    }
}
