using Confluent.Kafka;
using DepthCharts.Application;
using DepthCharts.Application.Abstractions;
using DepthCharts.Application.Contracts;
using DepthCharts.Infrastructure.Messaging;
using DepthCharts.Tests.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DepthCharts.Tests.Infrastructure;

public class KafkaCommandConsumerTests
{
    private static readonly DateTime T0 = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<LogLevel> Levels { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
    }

    private static (KafkaCommandConsumer Consumer, IEventStore Store, ListLogger<KafkaCommandConsumer> Logger) Create(
        bool enabled = true)
    {
        IEventStore store = TestStore.Create();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddSingleton(store);
        var sp = services.BuildServiceProvider();
        var processor = new CommandMessageProcessor(
            sp.GetRequiredService<IServiceScopeFactory>(), new ListLogger<CommandMessageProcessor>());
        var logger = new ListLogger<KafkaCommandConsumer>();
        var options = Options.Create(new KafkaOptions { ConsumerEnabled = enabled });
        return (new KafkaCommandConsumer(options, processor, logger), store, logger);
    }

    private static ConsumeResult<string, string> Msg(string? value, Timestamp timestamp) =>
        new() { Message = new Message<string, string> { Value = value!, Timestamp = timestamp } };

    private static string AddJson(int playerId = 1) =>
        CommandSerializer.Serialize(new AddPlayerCommand(Guid.NewGuid(), "nfl", "tb", "WR", playerId, "Bob", null, null));

    // Feeds the scripted steps to the loop, then cancels it.
    private static async Task RunAsync(KafkaCommandConsumer consumer, params Func<ConsumeResult<string, string>>[] steps)
    {
        using var cts = new CancellationTokenSource();
        var i = 0;
        await consumer.LoopAsync(ct =>
        {
            if (i == steps.Length) { cts.Cancel(); ct.ThrowIfCancellationRequested(); }
            return steps[i++]();
        }, cts.Token);
    }

    [Fact]
    public void ToOccurredAt_UsesKafkaTimestamp() =>
        Assert.Equal(T0, KafkaCommandConsumer.ToOccurredAt(new Timestamp(T0, TimestampType.CreateTime)));

    [Fact]
    public void ToOccurredAt_NotAvailable_IsNull() =>
        Assert.Null(KafkaCommandConsumer.ToOccurredAt(Timestamp.Default));

    [Fact]
    public async Task Loop_StoresKafkaTimestampAsOccurredAt()
    {
        var (consumer, store, _) = Create();

        await RunAsync(consumer, () => Msg(AddJson(), new Timestamp(T0, TimestampType.CreateTime)));

        Assert.Equal(T0, Assert.Single(await store.LoadAsync("nfl:tb", null, default)).OccurredAt);
    }

    [Fact]
    public async Task Loop_NoTimestamp_SkipsWithWarning()
    {
        var (consumer, store, logger) = Create();

        await RunAsync(consumer, () => Msg(AddJson(), Timestamp.Default));

        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Levels));
        Assert.Empty(await store.LoadAsync("nfl:tb", null, default));
    }

    [Fact]
    public async Task Loop_ConsumeException_LogsAndContinues()
    {
        var (consumer, store, logger) = Create();

        await RunAsync(consumer,
            () => throw new ConsumeException(null, new Error(ErrorCode.Local_Transport, "broker down")),
            () => Msg(AddJson(), new Timestamp(T0, TimestampType.CreateTime)));

        Assert.Equal(LogLevel.Error, Assert.Single(logger.Levels));
        Assert.Single(await store.LoadAsync("nfl:tb", null, default));
    }

    [Fact]
    public async Task Loop_FatalConsumeException_Rethrows()
    {
        var (consumer, _, logger) = Create();

        await Assert.ThrowsAsync<ConsumeException>(() => RunAsync(consumer,
            () => throw new ConsumeException(null, new Error(ErrorCode.Local_Fatal, "fatal", true))));

        Assert.Equal(LogLevel.Critical, Assert.Single(logger.Levels));
    }

    [Fact]
    public async Task Start_ConsumerDisabled_DoesNotRun()
    {
        var (consumer, _, _) = Create(enabled: false);

        await consumer.StartAsync(default);
        await consumer.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5)); // completes immediately; enabled would block on Kafka
    }

    [Fact]
    public async Task Options_ApiFactoryDisablesConsumer()
    {
        await using var factory = new ApiFactory();

        Assert.False(factory.Services.GetRequiredService<IOptions<KafkaOptions>>().Value.ConsumerEnabled);
    }
}
