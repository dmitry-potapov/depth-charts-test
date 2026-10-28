using DepthCharts.Producer.Api;
using DepthCharts.Producer.Cli;

namespace DepthCharts.Tests.Producer;

public class ChartFormatterTests
{
    [Fact]
    public void FormatChart_BriefExample()
    {
        // Arrange
        var chart = new Dictionary<string, List<PlayerDto>>
        {
            ["WR"] = [new(2, "Alice"), new(1, "Bob"), new(3, "Charlie")],
            ["KR"] = [new(1, "Bob")],
        };

        // Act & Assert
        Assert.Equal("WR: [2, 1, 3]" + Environment.NewLine + "KR: [1]", ChartFormatter.FormatChart(chart));
    }

    [Fact]
    public void FormatIds_Empty() => Assert.Equal("[]", ChartFormatter.FormatIds([]));
}
