using System.Text;
using DepthCharts.Application.Abstractions;
using DepthCharts.Application.Contracts;
using DepthCharts.Producer;
using DepthCharts.Producer.Api;

namespace DepthCharts.Tests.Producer;

public class DemoRunnerTests
{
    private sealed class FakePublisher : ICommandPublisher
    {
        public List<DepthChartCommand> Published { get; } = [];

        public Task PublishAsync(DepthChartCommand command, CancellationToken ct)
        {
            Published.Add(command);
            return Task.CompletedTask;
        }
    }

    private sealed class StubHandler(Func<string, string> json) : HttpMessageHandler
    {
        public List<string> Uris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Uris.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage
            {
                Content = new StringContent(json(request.RequestUri.ToString()), Encoding.UTF8, "application/json"),
            });
        }
    }

    private const string Ready =
        """{"WR":[{"playerId":2,"name":"Alice"},{"playerId":1,"name":"Bob"},{"playerId":3,"name":"Charlie"}],"KR":[{"playerId":1,"name":"Bob"}]}""";

    private const string Below = """[{"playerId":1,"name":"Bob"},{"playerId":3,"name":"Charlie"}]""";

    private static DemoRunner Runner(FakePublisher publisher, StubHandler handler, StringWriter output, StringWriter error, TimeSpan? timeout = null) =>
        new(publisher, new DepthChartApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://x/") }), output, error, timeout);

    [Fact]
    public async Task Run_PublishesThenPrintsChartAndBelow()
    {
        var publisher = new FakePublisher();
        var handler = new StubHandler(uri => uri.EndsWith("/below") ? Below : Ready);
        var output = new StringWriter();

        var code = await Runner(publisher, handler, output, new StringWriter()).RunAsync(default);

        Assert.Equal(0, code);
        Assert.Equal(4, publisher.Published.Count);
        Assert.Contains("below Alice WR:", output.ToString());
    }

    [Fact]
    public async Task Run_UsesFreshTeamPerRun_AndQueriesThatTeam()
    {
        var first = new FakePublisher();
        var second = new FakePublisher();
        var handler = new StubHandler(uri => uri.EndsWith("/below") ? "[]" : Ready);

        await Runner(first, handler, new StringWriter(), new StringWriter()).RunAsync(default);
        await Runner(second, handler, new StringWriter(), new StringWriter()).RunAsync(default);

        var team1 = first.Published[0].Team;
        Assert.NotEqual(team1, second.Published[0].Team);
        Assert.All(first.Published, c => Assert.Equal(team1, c.Team));
        Assert.Contains($"depth-charts/NFL/{team1}", handler.Uris[0]);
    }

    [Fact]
    public async Task Run_NeverReady_TimesOutWithExitCode1()
    {
        var error = new StringWriter();
        var handler = new StubHandler(_ => "{}");

        var code = await Runner(new FakePublisher(), handler, new StringWriter(), error, TimeSpan.FromMilliseconds(300)).RunAsync(default);

        Assert.Equal(1, code);
        Assert.Contains("Timed out", error.ToString());
    }

    [Fact]
    public async Task Run_CallerCancelled_PropagatesInsteadOfReportingTimeout()
    {
        var error = new StringWriter();
        using var cts = new CancellationTokenSource(100);
        var handler = new StubHandler(_ => "{}");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Runner(new FakePublisher(), handler, new StringWriter(), error).RunAsync(cts.Token));

        Assert.Empty(error.ToString());
    }
}
