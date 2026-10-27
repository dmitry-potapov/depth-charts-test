namespace DepthCharts.Infrastructure.Messaging;

public sealed class KafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string Topic { get; set; } = "depth-chart-commands";
    public bool ConsumerEnabled { get; set; } = true;
}
