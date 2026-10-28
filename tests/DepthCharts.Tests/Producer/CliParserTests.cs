using DepthCharts.Application.Contracts;
using DepthCharts.Producer.Cli;

namespace DepthCharts.Tests.Producer;

public class CliParserTests
{
    private static AddPlayerCommand ParseAdd(params string[] args) =>
        Assert.IsType<AddPlayerCommand>(Assert.IsType<PublishRequest>(CliParser.Parse(args)).Command);

    [Fact]
    public void Parse_Add_WithDepth()
    {
        // Act
        var add = ParseAdd("add", "NFL", "tb", "WR", "1", "Bob", "0");

        // Assert
        Assert.Equal(1, add.PlayerId);
        Assert.Equal("Bob", add.PlayerName);
        Assert.Equal(0, add.Depth);
        Assert.Equal("WR", add.Position);
        Assert.Null(add.PlayerPosition);
        Assert.NotEqual(Guid.Empty, add.CommandId);
    }

    [Fact]
    public void Parse_Add_WithoutDepth() =>
        Assert.Null(ParseAdd("add", "NFL", "tb", "WR", "1", "Bob").Depth);

    [Fact]
    public void Parse_Add_NameWithSpaces() =>
        Assert.Equal("Tom Brady", ParseAdd("add", "NFL", "tb", "QB", "12", "Tom Brady").PlayerName);

    [Fact]
    public void Parse_Add_FreshCommandIdEachTime() =>
        Assert.NotEqual(
            ParseAdd("add", "NFL", "tb", "WR", "1", "Bob").CommandId,
            ParseAdd("add", "NFL", "tb", "WR", "1", "Bob").CommandId);

    [Fact]
    public void Parse_Remove()
    {
        // Act
        var remove = Assert.IsType<RemovePlayerCommand>(
            Assert.IsType<PublishRequest>(CliParser.Parse(["remove", "NFL", "tb", "WR", "1"])).Command);

        // Assert
        Assert.Equal(1, remove.PlayerId);
        Assert.Equal("WR", remove.Position);
    }

    [Fact]
    public void Parse_Chart_WithAsOf()
    {
        // Act
        var chart = Assert.IsType<ChartRequest>(
            CliParser.Parse(["chart", "NFL", "tb", "--as-of", "2026-10-27T10:00:00+02:00"]));

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.Zero), chart.AsOf);
    }

    [Fact]
    public void Parse_Below_WithoutAsOf()
    {
        // Act
        var below = Assert.IsType<BelowRequest>(CliParser.Parse(["below", "NFL", "tb", "WR", "2"]));

        // Assert
        Assert.Equal(2, below.PlayerId);
        Assert.Null(below.AsOf);
    }

    [Fact]
    public void Parse_Below_WithAsOf()
    {
        // Act
        var below = Assert.IsType<BelowRequest>(
            CliParser.Parse(["below", "NFL", "tb", "WR", "2", "--as-of", "2026-10-27T08:00:00Z"]));

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.Zero), below.AsOf);
    }

    [Fact]
    public void Parse_Demo() => Assert.IsType<DemoRequest>(CliParser.Parse(["demo"]));

    [Theory]
    [InlineData("add NFL tb WR 1 Bob -1")]
    [InlineData("add NFL tb WR 0 Bob")]
    [InlineData("add NFL t:b WR 1 Bob")]
    [InlineData("add NFL tb WR x Bob")]
    [InlineData("chart NFL tb --as-of yesterday")]
    [InlineData("chart NFL tb --as-of 10/11/2026")]
    [InlineData("below NFL tb WR 2 --as-of yesterday")]
    [InlineData("below NFL tb WR 2 --asof 2026-10-27T08:00:00Z")]
    [InlineData("below NFL tb WR 2 --as-of 2026-10-27T08:00:00Z extra")]
    [InlineData("chart NFL tb --as-of 2026-10-27T08:00:00Z extra")]
    [InlineData("chart NFL tb --asof 2026-10-27T08:00:00Z")]
    [InlineData("remove NFL tb WR 0")]
    [InlineData("remove NFL tb WR -1")]
    [InlineData("below NFL tb WR 0")]
    [InlineData("below NFL tb WR -1")]
    public void Parse_InvalidValues_UsageError(string line) =>
        Assert.Throws<CliUsageException>(() => CliParser.Parse(line.Split(' ')));

    [Theory]
    [InlineData]
    [InlineData("nope")]
    [InlineData("add", "NFL")]
    [InlineData("remove", "NFL", "tb", "WR")]
    [InlineData("chart", "NFL")]
    [InlineData("below", "NFL", "tb", "WR")]
    [InlineData("chart", "NFL", "tb", "--as-of")]
    public void Parse_BadShape_UsageError(params string[] args) =>
        Assert.Throws<CliUsageException>(() => CliParser.Parse(args));
}
