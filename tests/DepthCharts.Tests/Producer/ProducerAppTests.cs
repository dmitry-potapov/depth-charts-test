using System.Text;
using Confluent.Kafka;
using DepthCharts.Application.Abstractions;
using DepthCharts.Application.Contracts;
using DepthCharts.Producer;
using DepthCharts.Producer.Api;

namespace DepthCharts.Tests.Producer;

public class ProducerAppTests
{
    private sealed class FakePublisher(Exception? fail = null) : ICommandPublisher
    {
        public List<DepthChartCommand> Published { get; } = [];

        public Task PublishAsync(DepthChartCommand command, CancellationToken ct)
        {
            if (fail is not null) throw fail;
            Published.Add(command);
            return Task.CompletedTask;
        }
    }

    private sealed class StubHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Uris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Uris.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond());
        }
    }

    private static HttpResponseMessage Json(string json) =>
        new() { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static async Task<(int Code, string Out, string Err)> Run(
        string line, ICommandPublisher? publisher = null, StubHandler? handler = null, CancellationToken ct = default)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var http = new HttpClient(handler ?? new StubHandler(() => Json("{}"))) { BaseAddress = new Uri("http://x/") };
        var code = await ProducerApp.RunAsync(
            line.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            () => publisher ?? new FakePublisher(), () => new DepthChartApiClient(http), output, error, ct);
        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task Add_Publishes_AndExitsZero()
    {
        var publisher = new FakePublisher();

        var (code, output, _) = await Run("add NFL tb WR 1 Bob", publisher);

        Assert.Equal(0, code);
        Assert.IsType<AddPlayerCommand>(Assert.Single(publisher.Published));
        Assert.Contains("Published AddPlayerCommand", output);
    }

    [Fact]
    public async Task Chart_QueriesApi_AndPrintsChart()
    {
        var handler = new StubHandler(() => Json("""{"WR":[{"playerId":1,"name":"Bob"}]}"""));

        var (code, output, _) = await Run("chart NFL tb", handler: handler);

        Assert.Equal(0, code);
        Assert.EndsWith("/depth-charts/NFL/tb", Assert.Single(handler.Uris));
        Assert.Contains("WR: [1]", output);
    }

    [Fact]
    public async Task Below_QueriesBelowEndpoint()
    {
        var handler = new StubHandler(() => Json("""[{"playerId":3,"name":"Charlie"}]"""));

        var (code, output, _) = await Run("below NFL tb WR 2", handler: handler);

        Assert.Equal(0, code);
        Assert.EndsWith("/below", Assert.Single(handler.Uris));
        Assert.Contains("3", output);
    }

    [Fact]
    public async Task Demo_RoutesToDemoRunner_PublishingFourCommands()
    {
        var publisher = new FakePublisher();
        var handler = null as StubHandler;
        handler = new StubHandler(() => Json(handler!.Uris[^1].EndsWith("/below")
            ? """[{"playerId":1,"name":"Bob"}]"""
            : """{"WR":[{"playerId":2,"name":"Alice"},{"playerId":1,"name":"Bob"},{"playerId":3,"name":"Charlie"}],"KR":[{"playerId":1,"name":"Bob"}]}"""));

        var (code, _, _) = await Run("demo", publisher, handler);

        Assert.Equal(0, code);
        Assert.Equal(4, publisher.Published.Count);
    }

    [Fact]
    public async Task UsageError_PrintsUsage_ExitsOne()
    {
        var (code, _, error) = await Run("nope");

        Assert.Equal(1, code);
        Assert.Contains("producer add", error);
    }

    [Fact]
    public async Task CtrlC_PrintsCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var (code, _, error) = await Run("add NFL tb WR 1 Bob", new FakePublisher(new OperationCanceledException()), ct: cts.Token);

        Assert.Equal(1, code);
        Assert.Contains("Cancelled.", error);
    }

    [Fact]
    public async Task HttpClientTimeout_PrintsTimedOut()
    {
        var handler = new StubHandler(() => throw new TaskCanceledException("timeout"));

        var (code, _, error) = await Run("chart NFL tb", handler: handler);

        Assert.Equal(1, code);
        Assert.Contains("Request timed out.", error);
    }

    [Fact]
    public async Task ProduceFailure_PrintsMessage_ExitsOne()
    {
        var failure = new ProduceException<string, string>(new Error(ErrorCode.Local_MsgTimedOut), new DeliveryResult<string, string>());

        var (code, _, error) = await Run("add NFL tb WR 1 Bob", new FakePublisher(failure));

        Assert.Equal(1, code);
        Assert.Contains(failure.Message, error);
    }

    [Fact]
    public async Task HttpFailure_ExitsOne()
    {
        var handler = new StubHandler(() => new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));

        var (code, _, error) = await Run("chart NFL tb", handler: handler);

        Assert.Equal(1, code);
        Assert.Contains("500", error);
    }

    [Fact]
    public async Task NonJsonResponse_ExitsOne_NoCrash()
    {
        var handler = new StubHandler(() => new HttpResponseMessage
        {
            Content = new StringContent("<html>", Encoding.UTF8, "application/json"),
        });

        var (code, _, error) = await Run("chart NFL tb", handler: handler);

        Assert.Equal(1, code);
        Assert.NotEmpty(error);
    }

    [Theory]
    [InlineData("localhost:5050")] // parses as scheme "localhost"
    [InlineData("not a uri")]
    [InlineData("ftp://host/")]
    public async Task BadBaseUrl_ExitsOne_NoCrash(string baseUrl)
    {
        var error = new StringWriter();

        var code = await ProducerApp.RunAsync(
            ["chart", "NFL", "tb"], () => new FakePublisher(),
            () => new DepthChartApiClient(new HttpClient { BaseAddress = ProducerApp.ParseBaseUrl(baseUrl) }),
            new StringWriter(), error, default);

        Assert.Equal(1, code);
        Assert.Contains("Api:BaseUrl", error.ToString());
    }

    [Fact]
    public void ParseBaseUrl_AcceptsHttpAndHttps()
    {
        Assert.Equal("http://localhost:5050/", ProducerApp.ParseBaseUrl("http://localhost:5050").ToString());
        Assert.Equal("https://api/", ProducerApp.ParseBaseUrl("https://api").ToString());
    }

    [Fact]
    public async Task KafkaExceptionBuildingPublisher_ExitsOne()
    {
        var error = new StringWriter();

        var code = await ProducerApp.RunAsync(
            ["add", "NFL", "tb", "WR", "1", "Bob"], () => throw new KafkaException(ErrorCode.Local_Fail),
            () => throw new InvalidOperationException(), new StringWriter(), error, default);

        Assert.Equal(1, code);
        Assert.NotEmpty(error.ToString());
    }
}
