using Confluent.Kafka;
using DepthCharts.Application.Abstractions;
using DepthCharts.Application.Contracts;
using DepthCharts.Producer;
using DepthCharts.Producer.Api;

namespace DepthCharts.Tests.Producer;

public class ProducerAppTests
{
    private static async Task<(int Code, string Out, string Err)> Run(
        string line, ICommandPublisher? publisher = null, StubHandler? handler = null, CancellationToken ct = default)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var http = (handler ?? StubHandler.Json(_ => "{}")).Client();
        var code = await ProducerApp.RunAsync(
            line.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            () => publisher ?? new FakePublisher(), () => new DepthChartApiClient(http), output, error, ct);
        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task Add_Publishes_AndExitsZero()
    {
        // Arrange
        var publisher = new FakePublisher();

        // Act
        var (code, output, _) = await Run("add NFL tb WR 1 Bob", publisher);

        // Assert
        Assert.Equal(0, code);
        Assert.IsType<AddPlayerCommand>(Assert.Single(publisher.Published));
        Assert.Contains("Published AddPlayerCommand", output);
    }

    [Fact]
    public async Task Chart_QueriesApi_AndPrintsChart()
    {
        // Arrange
        var handler = StubHandler.Json(_ => """{"WR":[{"playerId":1,"name":"Bob"}]}""");

        // Act
        var (code, output, _) = await Run("chart NFL tb", handler: handler);

        // Assert
        Assert.Equal(0, code);
        Assert.EndsWith("/depth-charts/NFL/tb", Assert.Single(handler.Uris));
        Assert.Contains("WR: [1]", output);
    }

    [Fact]
    public async Task Below_QueriesBelowEndpoint()
    {
        // Arrange
        var handler = StubHandler.Json(_ => """[{"playerId":3,"name":"Charlie"}]""");

        // Act
        var (code, output, _) = await Run("below NFL tb WR 2", handler: handler);

        // Assert
        Assert.Equal(0, code);
        Assert.EndsWith("/below", Assert.Single(handler.Uris));
        Assert.Contains("3", output);
    }

    [Fact]
    public async Task Demo_RoutesToDemoRunner_PublishingFourCommands()
    {
        // Arrange
        var publisher = new FakePublisher();
        var handler = StubHandler.Json(uri => uri.EndsWith("/below") ? DemoRunnerTests.Below : DemoRunnerTests.Ready);

        // Act
        var (code, _, _) = await Run("demo", publisher, handler);

        // Assert
        Assert.Equal(0, code);
        Assert.Equal(4, publisher.Published.Count);
    }

    [Fact]
    public async Task UsageError_PrintsUsage_ExitsOne()
    {
        // Act
        var (code, _, error) = await Run("nope");

        // Assert
        Assert.Equal(1, code);
        Assert.Contains("producer add", error);
    }

    [Fact]
    public async Task CtrlC_PrintsCancelled()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var (code, _, error) = await Run("add NFL tb WR 1 Bob", new FakePublisher(new OperationCanceledException()), ct: cts.Token);

        // Assert
        Assert.Equal(1, code);
        Assert.Contains("Cancelled.", error);
    }

    [Fact]
    public async Task HttpClientTimeout_PrintsTimedOut()
    {
        // Arrange
        var handler = new StubHandler(_ => throw new TaskCanceledException("timeout"));

        // Act
        var (code, _, error) = await Run("chart NFL tb", handler: handler);

        // Assert
        Assert.Equal(1, code);
        Assert.Contains("Request timed out.", error);
    }

    [Fact]
    public async Task ProduceFailure_PrintsMessage_ExitsOne()
    {
        // Arrange
        var failure = new ProduceException<string, string>(new Error(ErrorCode.Local_MsgTimedOut), new DeliveryResult<string, string>());

        // Act
        var (code, _, error) = await Run("add NFL tb WR 1 Bob", new FakePublisher(failure));

        // Assert
        Assert.Equal(1, code);
        Assert.Contains(failure.Message, error);
    }

    [Fact]
    public async Task HttpFailure_ExitsOne()
    {
        // Arrange
        var handler = new StubHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));

        // Act
        var (code, _, error) = await Run("chart NFL tb", handler: handler);

        // Assert
        Assert.Equal(1, code);
        Assert.Contains("500", error);
    }

    [Fact]
    public async Task NonJsonResponse_ExitsOne_NoCrash()
    {
        // Arrange
        var handler = StubHandler.Json(_ => "<html>");

        // Act
        var (code, _, error) = await Run("chart NFL tb", handler: handler);

        // Assert
        Assert.Equal(1, code);
        Assert.NotEmpty(error);
    }

    [Theory]
    [InlineData("localhost:5050")] // parses as scheme "localhost"
    [InlineData("not a uri")]
    [InlineData("ftp://host/")]
    public async Task BadBaseUrl_ExitsOne_NoCrash(string baseUrl)
    {
        // Arrange
        var error = new StringWriter();

        // Act
        var code = await ProducerApp.RunAsync(
            ["chart", "NFL", "tb"], () => new FakePublisher(),
            () => new DepthChartApiClient(new HttpClient { BaseAddress = ProducerApp.ParseBaseUrl(baseUrl) }),
            new StringWriter(), error, default);

        // Assert
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
        // Arrange
        var error = new StringWriter();

        // Act
        var code = await ProducerApp.RunAsync(
            ["add", "NFL", "tb", "WR", "1", "Bob"], () => throw new KafkaException(ErrorCode.Local_Fail),
            () => throw new InvalidOperationException(), new StringWriter(), error, default);

        // Assert
        Assert.Equal(1, code);
        Assert.NotEmpty(error.ToString());
    }
}
