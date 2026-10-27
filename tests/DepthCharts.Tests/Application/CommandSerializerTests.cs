using System.Text.Json;
using DepthCharts.Application.Contracts;

namespace DepthCharts.Tests.Application;

public class CommandSerializerTests
{
    [Fact]
    public void RoundTrip_AddPlayer()
    {
        var original = new AddPlayerCommand(Guid.NewGuid(), "NFL", "tb", "WR", 1, "Bob", "KR", 2);

        var json = CommandSerializer.Serialize(original);

        Assert.Contains("\"type\":\"add-player\"", json);
        Assert.Contains("\"playerName\"", json);
        Assert.Equal(original, CommandSerializer.Deserialize(json));
    }

    [Fact]
    public void RoundTrip_RemovePlayer()
    {
        var original = new RemovePlayerCommand(Guid.NewGuid(), "NFL", "tb", "WR", 1);

        var json = CommandSerializer.Serialize(original);

        Assert.Contains("\"type\":\"remove-player\"", json);
        Assert.Equal(original, CommandSerializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_TypeNotFirst_Works()
    {
        var id = Guid.NewGuid();
        var json = $$"""{"commandId":"{{id}}","sport":"NFL","team":"tb","position":"WR","playerId":1,"playerName":"Bob","type":"add-player"}""";

        var command = Assert.IsType<AddPlayerCommand>(CommandSerializer.Deserialize(json));

        Assert.Equal(id, command.CommandId);
        Assert.Null(command.Depth);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("""{"type":"nope"}""")]
    public void Deserialize_Invalid_ThrowsJsonException(string json) =>
        Assert.ThrowsAny<JsonException>(() => CommandSerializer.Deserialize(json));
}
