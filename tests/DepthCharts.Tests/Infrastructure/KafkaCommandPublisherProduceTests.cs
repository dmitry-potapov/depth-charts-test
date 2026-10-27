using System.Reflection;
using Confluent.Kafka;
using DepthCharts.Application.Abstractions;
using DepthCharts.Application.Contracts;
using DepthCharts.Infrastructure;
using DepthCharts.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DepthCharts.Tests.Infrastructure;

public class KafkaCommandPublisherProduceTests
{
    // Records ProduceAsync calls; every other IProducer member is unused and throws.
    public class RecordingProducer : DispatchProxy
    {
        public string? Topic;
        public Message<string, string>? Message;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "ProduceAsync":
                    Topic = (string)args![0]!;
                    Message = (Message<string, string>)args[1]!;
                    return Task.FromResult(new DeliveryResult<string, string>());
                case "Flush":
                    return 0;
                case "Dispose":
                    return null;
                default:
                    throw new NotSupportedException(method.Name);
            }
        }
    }

    [Fact]
    public async Task PublishAsync_ProducesToTopicWithKeyAndSerializedValue()
    {
        var producer = DispatchProxy.Create<IProducer<string, string>, RecordingProducer>();
        var recorder = (RecordingProducer)(object)producer;
        var command = new AddPlayerCommand(Guid.NewGuid(), "NFL", "TB", "WR", 1, "Bob", null, null);

        await new KafkaCommandPublisher("my-topic", producer).PublishAsync(command, default);

        Assert.Equal("my-topic", recorder.Topic);
        Assert.Equal("nfl:tb", recorder.Message!.Key);
        Assert.Equal(CommandSerializer.Serialize(command), recorder.Message.Value);
    }

    [Fact]
    public void AddCommandPublisher_ResolvesKafkaPublisher()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Kafka:Topic"] = "t" }).Build();
        using var provider = new ServiceCollection().AddCommandPublisher(config).BuildServiceProvider();

        Assert.IsType<KafkaCommandPublisher>(provider.GetRequiredService<ICommandPublisher>());
    }
}
