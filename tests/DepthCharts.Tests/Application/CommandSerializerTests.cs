using System.Text.Json;
using DepthCharts.Application.Contracts;

namespace DepthCharts.Tests.Application;

public class CommandSerializerTests
{
    [Fact]
    public void RoundTrip_AddPlayer()
    {
        // Arrange
        var original = new AddPlayerCommand(Guid.NewGuid(), "NFL", "tb", "WR", 1, "Bob", "KR", 2);

        // Act
        var json = CommandSerializer.Serialize(original);

        // Assert
        Assert.Contains("\"type\":\"add-player\"", json);
        Assert.Contains("\"playerName\"", json);
        Assert.Equal(original, CommandSerializer.Deserialize(json));
    }

    [Fact]
    public void RoundTrip_RemovePlayer()
    {
        // Arrange
        var original = new RemovePlayerCommand(Guid.NewGuid(), "NFL", "tb", "WR", 1);

        // Act
        var json = CommandSerializer.Serialize(original);

        // Assert
        Assert.Contains("\"type\":\"remove-player\"", json);
        Assert.Equal(original, CommandSerializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_TypeNotFirst_Works()
    {
        // Arrange
        var id = Guid.NewGuid();
        var json = $$"""{"commandId":"{{id}}","sport":"NFL","team":"tb","position":"WR","playerId":1,"playerName":"Bob","type":"add-player"}""";

        // Act
        var command = Assert.IsType<AddPlayerCommand>(CommandSerializer.Deserialize(json));

        // Assert
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
