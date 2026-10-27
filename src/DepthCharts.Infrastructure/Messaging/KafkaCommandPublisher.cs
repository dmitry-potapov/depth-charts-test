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

    // MessageTimeoutMs makes a down broker fail in 10 s instead of retrying forever.
    public KafkaCommandPublisher(IOptions<KafkaOptions> options)
        : this(options.Value.Topic, new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            MessageTimeoutMs = 10000,
        }).Build())
    {
    }

    // Test seam: lets a fake producer capture what is produced.
    internal KafkaCommandPublisher(string topic, IProducer<string, string> producer)
    {
        _topic = topic;
        _producer = producer;
    }

    public static string KeyFor(DepthChartCommand command) =>
        DepthChart.StreamIdFor(new Sport(command.Sport), new Team(command.Team));

    public Task PublishAsync(DepthChartCommand command, CancellationToken ct) =>
        _producer.ProduceAsync(_topic,
            new Message<string, string> { Key = KeyFor(command), Value = CommandSerializer.Serialize(command) }, ct);

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
