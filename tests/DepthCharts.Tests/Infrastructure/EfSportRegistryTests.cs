using DepthCharts.Domain;
using DepthCharts.Infrastructure.Persistence;

namespace DepthCharts.Tests.Infrastructure;

public class EfSportRegistryTests
{
    [Fact]
    public async Task Nfl_IsSeededWithBriefPositions()
    {
        var allowed = await TestStore.Sports().GetAllowedPositionsAsync(new Sport("NFL"), default);

        Assert.Equal(
            new[] { "QB", "WR", "RB", "TE", "K", "P", "KR", "PR" }.Order(),
            allowed!.Select(p => p.Value).Order());
    }

    [Fact]
    public async Task Mlb_IsSeededWithBriefPositions()
    {
        var allowed = await TestStore.Sports().GetAllowedPositionsAsync(new Sport("MLB"), default);

        Assert.Equal(11, allowed!.Count);
        Assert.Contains(new Position("1B"), allowed);
    }

    [Fact]
    public async Task UnknownSport_MeansAnyPosition() =>
        Assert.Null(await TestStore.Sports().GetAllowedPositionsAsync(new Sport("NHL"), default));

    [Fact]
    public async Task NullOrEmptyList_MeansAnyPosition()
    {
        var db = TestStore.NewContext();
        db.Sports.AddRange(new SportDefinition { Sport = "AAA" }, new SportDefinition { Sport = "BBB", AllowedPositions = [] });
        await db.SaveChangesAsync();
        var registry = new EfSportRegistry(db);

        Assert.Null(await registry.GetAllowedPositionsAsync(new Sport("AAA"), default));
        Assert.Null(await registry.GetAllowedPositionsAsync(new Sport("BBB"), default));
    }
}
