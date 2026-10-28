using DepthCharts.Application.Abstractions;
using DepthCharts.Domain.Events;
using DepthCharts.Infrastructure.Messaging;
using Microsoft.Extensions.Logging;
using static DepthCharts.Tests.TestProcessor;

namespace DepthCharts.Tests.Infrastructure;

public class CommandMessageProcessorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private sealed class ThrowingStore(Exception error) : IEventStore
    {
        public Task<IReadOnlyList<IDomainEvent>> LoadAsync(string streamId, DateTimeOffset? asOf, CancellationToken ct) =>
            throw error;
        public Task<bool> HasProcessedAsync(Guid commandId, CancellationToken ct) => throw error;
        public Task AppendAsync(string streamId, int expectedVersion, Guid commandId,
            IReadOnlyList<IDomainEvent> events, CancellationToken ct) => throw error;
    }

    private static (CommandMessageProcessor Processor, IEventStore Store, ListLogger<CommandMessageProcessor> Logger) Create(
        IEventStore? store = null)
    {
        store ??= TestStore.Create();
        var logger = new ListLogger<CommandMessageProcessor>();
        return (TestProcessor.Create(store, logger), store, logger);
    }

    [Fact]
    public async Task Process_ValidCommand_Applied()
    {
        // Arrange
        var (processor, store, _) = Create();

        // Act
        await processor.ProcessAsync(AddJson(), T0, default);

        // Assert
        Assert.Single(await store.LoadAsync("nfl:tb", null, default));
    }

    [Fact]
    public async Task Process_UsesMessageTimestamp()
    {
        // Arrange
        var (processor, store, _) = Create();

        // Act
        await processor.ProcessAsync(AddJson(), T0, default);

        // Assert
        Assert.Equal(T0, Assert.Single(await store.LoadAsync("nfl:tb", null, default)).OccurredAt);
    }

    [Fact]
    public async Task Process_BadJson_LogsWarningAndSkips()
    {
        // Arrange
        var (processor, store, logger) = Create();

        // Act
        await processor.ProcessAsync("garbage", T0, default);

        // Assert
        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Levels));
        Assert.Empty(await store.LoadAsync("nfl:tb", null, default));
    }

    [Fact]
    public async Task Process_NullValue_LogsWarningNotError()
    {
        // Arrange
        var (processor, _, logger) = Create();

        // Act
        await processor.ProcessAsync(null, T0, default);

        // Assert
        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Levels));
    }

    [Fact]
    public async Task Process_InvalidDomainValue_LogsWarningAndSkips()
    {
        // Arrange
        var (processor, store, logger) = Create();

        // Act
        await processor.ProcessAsync(AddJson(playerId: 0), T0, default);

        // Assert
        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Levels));
        Assert.Empty(await store.LoadAsync("nfl:tb", null, default));
    }

    [Fact]
    public async Task Process_CancelledDuringHandling_RethrowsWithoutLogging()
    {
        // Arrange
        var (processor, _, logger) = Create(new ThrowingStore(new OperationCanceledException()));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.ProcessAsync(AddJson(), T0, cts.Token));

        // Assert
        Assert.Empty(logger.Levels);
    }

    [Fact]
    public async Task Process_UnexpectedException_LogsError()
    {
        // Arrange
        var (processor, _, logger) = Create(new ThrowingStore(new InvalidOperationException("boom")));

        // Act
        await processor.ProcessAsync(AddJson(), T0, default);

        // Assert
        Assert.Equal(LogLevel.Error, Assert.Single(logger.Levels));
    }

    [Fact]
    public async Task Process_BadMessageThenGood_GoodApplied()
    {
        // Arrange
        var (processor, store, _) = Create();

        // Act
        await processor.ProcessAsync("garbage", T0, default);
        await processor.ProcessAsync(AddJson(), T0, default);

        // Assert
        Assert.Single(await store.LoadAsync("nfl:tb", null, default));
    }
}
