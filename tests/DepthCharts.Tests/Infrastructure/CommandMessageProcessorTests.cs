using DepthCharts.Application;
using DepthCharts.Application.Abstractions;
using DepthCharts.Application.Contracts;
using DepthCharts.Domain.Events;
using DepthCharts.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DepthCharts.Tests.Infrastructure;

public class CommandMessageProcessorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private sealed class ListLogger : ILogger<CommandMessageProcessor>
    {
        public List<LogLevel> Levels { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
    }

    private sealed class ThrowingStore : IEventStore
    {
        public Task<IReadOnlyList<IDomainEvent>> LoadAsync(string streamId, DateTimeOffset? asOf, CancellationToken ct) =>
            throw new InvalidOperationException("boom");
        public Task<bool> HasProcessedAsync(Guid commandId, CancellationToken ct) =>
            throw new InvalidOperationException("boom");
        public Task AppendAsync(string streamId, int expectedVersion, Guid commandId,
            IReadOnlyList<IDomainEvent> events, CancellationToken ct) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class CancellingStore : IEventStore
    {
        public Task<IReadOnlyList<IDomainEvent>> LoadAsync(string streamId, DateTimeOffset? asOf, CancellationToken ct) =>
            throw new OperationCanceledException(ct);
        public Task<bool> HasProcessedAsync(Guid commandId, CancellationToken ct) =>
            throw new OperationCanceledException(ct);
        public Task AppendAsync(string streamId, int expectedVersion, Guid commandId,
            IReadOnlyList<IDomainEvent> events, CancellationToken ct) =>
            throw new OperationCanceledException(ct);
    }

    private static (CommandMessageProcessor Processor, IEventStore Store, ListLogger Logger) Create(IEventStore? store = null)
    {
        store ??= TestStore.Create();
        var logger = new ListLogger();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddSingleton(store);
        var sp = services.BuildServiceProvider();
        return (new CommandMessageProcessor(sp.GetRequiredService<IServiceScopeFactory>(), logger), store, logger);
    }

    private static string AddJson(string team = "tb", int playerId = 1) =>
        CommandSerializer.Serialize(new AddPlayerCommand(Guid.NewGuid(), "nfl", team, "WR", playerId, "Bob", null, null));

    [Fact]
    public async Task Process_ValidCommand_Applied()
    {
        var (processor, store, _) = Create();

        await processor.ProcessAsync(AddJson(), T0, default);

        Assert.Single(await store.LoadAsync("nfl:tb", null, default));
    }

    [Fact]
    public async Task Process_UsesMessageTimestamp()
    {
        var (processor, store, _) = Create();

        await processor.ProcessAsync(AddJson(), T0, default);

        Assert.Equal(T0, Assert.Single(await store.LoadAsync("nfl:tb", null, default)).OccurredAt);
    }

    [Fact]
    public async Task Process_BadJson_LogsWarningAndSkips()
    {
        var (processor, store, logger) = Create();

        await processor.ProcessAsync("garbage", T0, default);

        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Levels));
        Assert.Empty(await store.LoadAsync("nfl:tb", null, default));
    }

    [Fact]
    public async Task Process_NullValue_LogsWarningNotError()
    {
        var (processor, _, logger) = Create();

        await processor.ProcessAsync(null, T0, default);

        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Levels));
    }

    [Fact]
    public async Task Process_InvalidDomainValue_LogsWarningAndSkips()
    {
        var (processor, store, logger) = Create();

        await processor.ProcessAsync(AddJson(playerId: 0), T0, default);

        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Levels));
        Assert.Empty(await store.LoadAsync("nfl:tb", null, default));
    }

    [Fact]
    public async Task Process_CancelledDuringHandling_RethrowsWithoutLogging()
    {
        var (processor, _, logger) = Create(new CancellingStore());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.ProcessAsync(AddJson(), T0, cts.Token));

        Assert.Empty(logger.Levels);
    }

    [Fact]
    public async Task Process_UnexpectedException_LogsError()
    {
        var (processor, _, logger) = Create(new ThrowingStore());

        await processor.ProcessAsync(AddJson(), T0, default);

        Assert.Equal(LogLevel.Error, Assert.Single(logger.Levels));
    }

    [Fact]
    public async Task Process_BadMessageThenGood_GoodApplied()
    {
        var (processor, store, _) = Create();

        await processor.ProcessAsync("garbage", T0, default);
        await processor.ProcessAsync(AddJson(), T0, default);

        Assert.Single(await store.LoadAsync("nfl:tb", null, default));
    }
}
