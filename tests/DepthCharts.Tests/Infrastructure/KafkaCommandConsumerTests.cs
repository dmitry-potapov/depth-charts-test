using System.Reflection;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using DepthCharts.Application.Abstractions;
using DepthCharts.Infrastructure.Messaging;
using DepthCharts.Tests.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static DepthCharts.Tests.TestProcessor;

namespace DepthCharts.Tests.Infrastructure;

public class KafkaCommandConsumerTests
{
    private static readonly DateTime T0 = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    private static (KafkaCommandConsumer Consumer, IEventStore Store, ListLogger<KafkaCommandConsumer> Logger) Create(
        bool enabled = true)
    {
        IEventStore store = TestStore.Create();
        var processor = TestProcessor.Create(store, new ListLogger<CommandMessageProcessor>());
        var logger = new ListLogger<KafkaCommandConsumer>();
        var options = Options.Create(new KafkaOptions { ConsumerEnabled = enabled });
        return (new KafkaCommandConsumer(options, processor, logger), store, logger);
    }

    private static ConsumeResult<string, string> Msg(string? value, Timestamp timestamp) =>
        new() { Message = new Message<string, string> { Value = value!, Timestamp = timestamp } };

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
        // Arrange
        var (consumer, store, _) = Create();

        // Act
        await RunAsync(consumer, () => Msg(AddJson(), new Timestamp(T0, TimestampType.CreateTime)));

        // Assert
        Assert.Equal(T0, Assert.Single(await store.LoadAsync("nfl:tb", null, default)).OccurredAt);
    }

    [Fact]
    public async Task Loop_NoTimestamp_SkipsWithWarning()
    {
        // Arrange
        var (consumer, store, logger) = Create();

        // Act
        await RunAsync(consumer, () => Msg(AddJson(), Timestamp.Default));

        // Assert
        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Levels));
        Assert.Empty(await store.LoadAsync("nfl:tb", null, default));
    }

    [Fact]
    public async Task Loop_ConsumeException_LogsAndContinues()
    {
        // Arrange
        var (consumer, store, logger) = Create();

        // Act
        await RunAsync(consumer,
            () => throw new ConsumeException(null, new Error(ErrorCode.Local_Transport, "broker down")),
            () => Msg(AddJson(), new Timestamp(T0, TimestampType.CreateTime)));

        // Assert
        Assert.Equal(LogLevel.Error, Assert.Single(logger.Levels));
        Assert.Single(await store.LoadAsync("nfl:tb", null, default));
    }

    [Fact]
    public async Task Loop_FatalConsumeException_Rethrows()
    {
        // Arrange
        var (consumer, _, logger) = Create();

        // Act & Assert
        await Assert.ThrowsAsync<ConsumeException>(() => RunAsync(consumer,
            () => throw new ConsumeException(null, new Error(ErrorCode.Local_Fatal, "fatal", true))));

        // Assert
        Assert.Equal(LogLevel.Critical, Assert.Single(logger.Levels));
    }

    [Fact]
    public async Task Start_ConsumerDisabled_DoesNotRun()
    {
        // Arrange
        var (consumer, _, _) = Create(enabled: false);

        // Act & Assert
        await consumer.StartAsync(default);
        await consumer.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5)); // completes immediately; enabled would block on Kafka
    }

    [Fact]
    public async Task Options_ApiFactoryDisablesConsumer()
    {
        // Arrange
        await using var factory = new ApiFactory();

        // Act & Assert
        Assert.False(factory.Services.GetRequiredService<IOptions<KafkaOptions>>().Value.ConsumerEnabled);
    }

    public class FakeAdmin : DispatchProxy
    {
        public TopicSpecification? Spec;
        public Exception? Throw;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name != "CreateTopicsAsync") throw new NotSupportedException(method.Name);
            Spec = ((IEnumerable<TopicSpecification>)args![0]!).Single();
            return Throw is null ? Task.CompletedTask : Task.FromException(Throw);
        }
    }

    [Fact]
    public async Task EnsureTopic_CreatesSinglePartitionTopic()
    {
        // Arrange
        var admin = DispatchProxy.Create<IAdminClient, FakeAdmin>();

        // Act
        await KafkaCommandConsumer.EnsureTopicAsync(admin, "t", default);

        // Assert
        var spec = ((FakeAdmin)(object)admin).Spec!;
        Assert.Equal(("t", 1), (spec.Name, spec.NumPartitions));
    }

    [Fact]
    public async Task EnsureTopic_AlreadyExists_IsIgnored_OtherErrorsPropagate()
    {
        // Arrange
        var admin = DispatchProxy.Create<IAdminClient, FakeAdmin>();
        CreateTopicsException Ex(ErrorCode code) => new([new CreateTopicReport { Topic = "t", Error = new Error(code) }]);

        // Act & Assert: already exists completes without throwing
        ((FakeAdmin)(object)admin).Throw = Ex(ErrorCode.TopicAlreadyExists);
        await KafkaCommandConsumer.EnsureTopicAsync(admin, "t", default);

        // Act & Assert: any other error propagates
        ((FakeAdmin)(object)admin).Throw = Ex(ErrorCode.TopicAuthorizationFailed);
        await Assert.ThrowsAsync<CreateTopicsException>(() => KafkaCommandConsumer.EnsureTopicAsync(admin, "t", default));
    }
}
