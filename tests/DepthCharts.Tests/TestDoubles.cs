using System.Text;
using DepthCharts.Application;
using DepthCharts.Application.Abstractions;
using DepthCharts.Application.Contracts;
using DepthCharts.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DepthCharts.Tests;

public sealed class ListLogger<T> : ILogger<T>
{
    public List<LogLevel> Levels { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
}

public sealed class FakePublisher(Exception? fail = null) : ICommandPublisher
{
    public List<DepthChartCommand> Published { get; } = [];

    public Task PublishAsync(DepthChartCommand command, CancellationToken ct)
    {
        if (fail is not null) throw fail;
        Published.Add(command);
        return Task.CompletedTask;
    }
}

// Records request URIs and answers each request with the given response.
public sealed class StubHandler(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<string> Uris { get; } = [];

    public static StubHandler Json(Func<string, string> json) =>
        new(uri => new() { Content = new StringContent(json(uri), Encoding.UTF8, "application/json") });

    public HttpClient Client() => new(this) { BaseAddress = new Uri("http://x/") };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri!.ToString();
        Uris.Add(uri);
        return Task.FromResult(respond(uri));
    }
}

public static class TestProcessor
{
    public static CommandMessageProcessor Create(IEventStore store, ILogger<CommandMessageProcessor> logger)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddSingleton(store);
        services.AddSingleton<ISportRegistry>(TestStore.Sports());
        return new CommandMessageProcessor(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), logger);
    }

    public static string AddJson(int playerId = 1) =>
        CommandSerializer.Serialize(new AddPlayerCommand(Guid.NewGuid(), "nfl", "tb", "WR", playerId, "Bob", null, null));
}
