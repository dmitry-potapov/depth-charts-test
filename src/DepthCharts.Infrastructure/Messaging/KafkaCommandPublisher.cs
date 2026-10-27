using Confluent.Kafka;
using DepthCharts.Application.Abstractions;
using DepthCharts.Application.Contracts;
using DepthCharts.Domain;
using Microsoft.Extensions.Options;

namespace DepthCharts.Infrastructure.Messaging;

public sealed class KafkaCommandPublisher : ICommandPublisher, IDisposable
{
    private readonly string _topic;
    private readonly IProducer<string, string> _producer;

    public KafkaCommandPublisher(IOptions<KafkaOptions> options)
        : this(options.Value.Topic, new ProducerBuilder<string, string>(ConfigFor(options.Value)).Build())
    {
    }

    // MessageTimeoutMs makes a down broker fail in 10 s instead of retrying forever.
    // Idempotence keeps a retried batch from landing behind a later one: command order decides the chart.
    internal static ProducerConfig ConfigFor(KafkaOptions options) => new()
    {
        BootstrapServers = options.BootstrapServers,
        MessageTimeoutMs = 10000,
        EnableIdempotence = true,
    };

    // Test seam: lets a fake producer capture what is produced.
    internal KafkaCommandPublisher(string topic, IProducer<string, string> producer)
    {
        _topic = topic;
        _producer = producer;
    }

    public static string KeyFor(DepthChartCommand command) =>
        DepthChart.StreamIdFor(new Sport(command.Sport), new Team(command.Team));

    // ct is only checked up front: cancelling ProduceAsync stops the wait, not the send, so a caller
    // told "cancelled" could still see the message delivered. Once queued, we wait for the outcome.
    public Task PublishAsync(DepthChartCommand command, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return _producer.ProduceAsync(_topic,
            new Message<string, string> { Key = KeyFor(command), Value = CommandSerializer.Serialize(command) });
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
