using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DepthCharts.Infrastructure.Messaging;

public sealed class KafkaCommandConsumer(
    IOptions<KafkaOptions> options, CommandMessageProcessor processor, ILogger<KafkaCommandConsumer> logger)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        options.Value.ConsumerEnabled ? Task.Run(() => RunAsync(stoppingToken), stoppingToken) : Task.CompletedTask;

    private async Task RunAsync(CancellationToken ct)
    {
        var o = options.Value;
        var config = new ConsumerConfig
        {
            BootstrapServers = o.BootstrapServers,
            // A fresh group per start + no commits = replay from earliest every time.
            GroupId = $"depth-charts-processor-{Guid.NewGuid()}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            AllowAutoCreateTopics = true,
        };
        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(o.Topic);
        try
        {
            await LoopAsync(consumer.Consume, ct);
        }
        finally
        {
            consumer.Close();
        }
    }

    // Kafka timestamps become event OccurredAt, so replays must be deterministic: never fall back to "now".
    public static DateTimeOffset? ToOccurredAt(Timestamp timestamp) =>
        timestamp.Type == TimestampType.NotAvailable ? null : timestamp.UtcDateTime;

    public async Task LoopAsync(Func<CancellationToken, ConsumeResult<string, string>> consume, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var msg = consume(ct);
                    if (ToOccurredAt(msg.Message.Timestamp) is not { } occurredAt)
                    {
                        logger.LogWarning("Skipping message without a Kafka timestamp at {Offset}", msg.TopicPartitionOffset);
                        continue;
                    }
                    await processor.ProcessAsync(msg.Message.Value, occurredAt, ct);
                }
                catch (ConsumeException e) when (e.Error.IsFatal)
                {
                    // Unrecoverable: let the host stop rather than serve a chart that never updates.
                    logger.LogCritical(e, "Fatal Kafka error");
                    throw;
                }
                catch (ConsumeException e)
                {
                    logger.LogError(e, "Kafka consume error");
                }
            }
        }
        catch (OperationCanceledException) { }
    }
}
