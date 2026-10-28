using System.Net.Http.Json;
using DepthCharts.Application.Contracts;
using DepthCharts.Api;
using DepthCharts.Infrastructure.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Testcontainers.Kafka;

namespace DepthCharts.Tests.Integration;

// Requires Docker: runs the real publisher, broker, topic creation and consumer that unit tests fake.
public sealed class KafkaFixture : IAsyncLifetime
{
    public KafkaContainer Kafka { get; } = new KafkaBuilder("apache/kafka:4.1.0").Build();

    public Task InitializeAsync() => Kafka.StartAsync();

    public Task DisposeAsync() => Kafka.DisposeAsync().AsTask();
}

public class KafkaEndToEndTests(KafkaFixture fixture) : IClassFixture<KafkaFixture>
{
    private sealed class KafkaApiFactory(string bootstrapServers, string topic) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
            .UseSetting("Kafka:BootstrapServers", bootstrapServers)
            .UseSetting("Kafka:Topic", topic);
    }

    private KafkaOptions OptionsFor(string topic) => new() { BootstrapServers = fixture.Kafka.GetBootstrapAddress(), Topic = topic };

    private static async Task PublishAsync(KafkaOptions options, params DepthChartCommand[] commands)
    {
        using var publisher = new KafkaCommandPublisher(Options.Create(options));
        foreach (var command in commands) await publisher.PublishAsync(command, default);
    }

    private static AddPlayerCommand Add(int id, string name, int? depth) =>
        new(Guid.NewGuid(), "NFL", "tb", "WR", id, name, null, depth);

    // The consumer is asynchronous, so poll the Api until the expected chart appears.
    private static async Task<string[]> WaitForWrAsync(KafkaApiFactory factory, int count)
    {
        using var client = factory.CreateClient();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var chart = await client.GetFromJsonAsync<Dictionary<string, PlayerResponse[]>>("/depth-charts/nfl/tb");
            var names = chart!.GetValueOrDefault("WR", []).Select(p => p.Name).ToArray();
            if (names.Length >= count || DateTime.UtcNow > deadline) return names;
            await Task.Delay(200);
        }
    }

    [Fact]
    public async Task PublishedCommands_AreConsumedInOrder_AndServedByApi()
    {
        // Arrange
        var options = OptionsFor($"t-{Guid.NewGuid()}");
        await using var factory = new KafkaApiFactory(options.BootstrapServers, options.Topic);
        _ = factory.Server; // start the host so the consumer creates the topic and subscribes

        // Act
        await PublishAsync(options, Add(1, "Bob", 0), Add(2, "Alice", 0), Add(3, "Charlie", 2));

        // Assert
        Assert.Equal(["Alice", "Bob", "Charlie"], await WaitForWrAsync(factory, 3));
    }

    [Fact]
    public async Task Restart_ReplaysTopicFromEarliest()
    {
        // Arrange
        var options = OptionsFor($"t-{Guid.NewGuid()}");
        await using (var first = new KafkaApiFactory(options.BootstrapServers, options.Topic))
        {
            _ = first.Server;
            await PublishAsync(options, Add(1, "Bob", 0));
            Assert.Equal(["Bob"], await WaitForWrAsync(first, 1));
        }

        // Act: a new host starts with an empty in-memory store
        await using var second = new KafkaApiFactory(options.BootstrapServers, options.Topic);

        // Assert
        Assert.Equal(["Bob"], await WaitForWrAsync(second, 1));
    }
}
