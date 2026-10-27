using DepthCharts.Application.Contracts;
using DepthCharts.Domain;
using DepthCharts.Infrastructure.Messaging;

namespace DepthCharts.Tests.Infrastructure;

public class KafkaCommandPublisherTests
{
    [Fact]
    public void KeyFor_NormalisesToStreamId()
    {
        var command = new AddPlayerCommand(Guid.NewGuid(), "nfl", "TB", "WR", 1, "Bob", null, null);

        Assert.Equal("nfl:tb", KafkaCommandPublisher.KeyFor(command));
    }

    [Fact]
    public void KeyFor_InvalidTeam_ThrowsDomainValidation()
    {
        var command = new RemovePlayerCommand(Guid.NewGuid(), "NFL", "t:b", "WR", 1);

        Assert.Throws<DomainValidationException>(() => KafkaCommandPublisher.KeyFor(command));
    }
}
