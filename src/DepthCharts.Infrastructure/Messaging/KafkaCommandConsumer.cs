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
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var msg = consumer.Consume(ct);
                    await processor.ProcessAsync(msg.Message.Value, msg.Message.Timestamp.UtcDateTime, ct);
                }
                catch (ConsumeException e)
                {
                    logger.LogError(e, "Kafka consume error");
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            consumer.Close();
        }
    }
}
