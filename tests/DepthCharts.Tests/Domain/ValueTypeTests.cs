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
    public void Team_NormalisesToLower()
    {
        Assert.Equal("tb", new Team(" Tb ").Value);
        Assert.Equal("new-york-1", new Team("new-york-1").Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("t:b")]
    [InlineData("t b")]
    [InlineData("tb!")]
    public void Team_RejectsInvalid(string value) =>
        Assert.Throws<DomainValidationException>(() => new Team(value));

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
    public void Player_RejectsNonPositiveId(int value) =>
        Assert.Throws<DomainValidationException>(() => new Player(value, "Bob", null));

    [Fact]
    public void Player_RejectsEmptyName()
    {
        Assert.Throws<DomainValidationException>(() => new Player(1, " ", null));
        Assert.Equal("Bob", new Player(1, " Bob ", null).Name);
    }
}
