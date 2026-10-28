using DepthCharts.Producer.Api;

namespace DepthCharts.Tests.Producer;

public class DepthChartApiClientTests
{
    [Fact]
    public void ChartUri_NoAsOf() =>
        Assert.Equal("depth-charts/NFL/demo", DepthChartApiClient.ChartUri("NFL", "demo", null));

    [Fact]
    public void ChartUri_EscapesAsOf()
    {
        // Arrange
        var asOf = new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.FromHours(2));

        // Act & Assert
        Assert.Contains("asOf=2026-10-27T10%3A00%3A00.0000000%2B02%3A00", DepthChartApiClient.ChartUri("NFL", "demo", asOf));
    }

    [Fact]
    public void BelowUri_Shape() =>
        Assert.Equal("depth-charts/NFL/demo/positions/WR/players/2/below",
            DepthChartApiClient.BelowUri("NFL", "demo", "WR", 2, null));

    [Fact]
    public async Task GetChart_DeserialisesInOrder()
    {
        // Arrange
        var http = StubHandler.Json(_ =>
            """{"WR":[{"playerId":2,"name":"Alice"}],"KR":[{"playerId":1,"name":"Bob"}]}""").Client();

        // Act
        var chart = await new DepthChartApiClient(http).GetChartAsync("NFL", "demo", null, CancellationToken.None);

        // Assert
        Assert.Equal(["WR", "KR"], chart.Keys);
        Assert.Equal(new PlayerDto(2, "Alice"), chart["WR"][0]);
    }
}
