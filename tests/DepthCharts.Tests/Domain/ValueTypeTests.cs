using DepthCharts.Domain;

namespace DepthCharts.Tests.Domain;

public class ValueTypeTests
{
    [Fact]
    public void Sport_NormalisesToUpper()
    {
        Assert.Equal("NFL", new Sport(" nfl ").Value);
        Assert.Equal(new Sport("NFL"), new Sport("nfl"));
    }

    [Fact]
    public void Position_NormalisesToUpper() => Assert.Equal("1B", new Position(" 1b ").Value);

    [Fact]
    public void TeamId_NormalisesToLower()
    {
        Assert.Equal("tb", new TeamId(" Tb ").Value);
        Assert.Equal("new-york-1", new TeamId("new-york-1").Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("t:b")]
    [InlineData("t b")]
    [InlineData("tb!")]
    public void TeamId_RejectsInvalid(string value) =>
        Assert.Throws<DomainValidationException>(() => new TeamId(value));

    [Theory]
    [InlineData("K")]
    [InlineData("N:FL")]
    [InlineData("$x")]
    [InlineData("N\nFL")]
    public void Sport_RejectsInvalid(string value) =>
        Assert.Throws<DomainValidationException>(() => new Sport(value));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Sport_And_Position_RejectEmpty(string value)
    {
        Assert.Throws<DomainValidationException>(() => new Sport(value));
        Assert.Throws<DomainValidationException>(() => new Position(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PlayerId_RejectsNonPositive(int value)
    {
        Assert.Throws<DomainValidationException>(() => new PlayerId(value));
        Assert.Equal(1, new PlayerId(1).Value);
    }

    [Fact]
    public void Player_RejectsEmptyName()
    {
        Assert.Throws<DomainValidationException>(() => new Player(new PlayerId(1), " ", null));
        Assert.Equal("Bob", new Player(new PlayerId(1), " Bob ", null).Name);
    }
}
