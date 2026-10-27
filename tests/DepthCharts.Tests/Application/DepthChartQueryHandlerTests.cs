using DepthCharts.Application;
using DepthCharts.Application.Contracts;
using DepthCharts.Domain;

namespace DepthCharts.Tests.Application;

public class DepthChartQueryHandlerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static AddPlayerCommand Add(int id, string name, string position, int? depth) =>
        new(Guid.NewGuid(), "NFL", "tb", position, id, name, null, depth);

    private static (DepthChartQueryHandler Query, DepthChartCommandHandler Command) Handlers()
    {
        var store = TestStore.Create();
        return (new DepthChartQueryHandler(store), new DepthChartCommandHandler(store, TestStore.Sports()));
    }

    private static async Task SeedBriefExample(DepthChartCommandHandler command)
    {
        await command.HandleAsync(Add(1, "Bob", "WR", 0), T0, default);
        await command.HandleAsync(Add(2, "Alice", "WR", 0), T0, default);
        await command.HandleAsync(Add(3, "Charlie", "WR", 2), T0, default);
        await command.HandleAsync(Add(1, "Bob", "KR", null), T0, default);
    }

    [Fact]
    public async Task FullChart_BriefExample()
    {
        var (query, command) = Handlers();
        await SeedBriefExample(command);

        var chart = await query.GetFullChartAsync(new GetFullDepthChartQuery("NFL", "tb", null), default);

        Assert.Equal([2, 1, 3], chart[new Position("WR")].Select(p => p.Id));
        Assert.Equal([1], chart[new Position("KR")].Select(p => p.Id));
    }

    [Fact]
    public async Task PlayersUnder_BriefExample()
    {
        var (query, command) = Handlers();
        await SeedBriefExample(command);

        var under = await query.GetPlayersUnderAsync(new GetPlayersUnderQuery("NFL", "tb", "WR", 2, null), default);

        Assert.Equal([1, 3], under.Select(p => p.Id));
    }

    [Fact]
    public async Task FullChart_AsOf_ReturnsEarlierChart()
    {
        var (query, command) = Handlers();
        await command.HandleAsync(Add(1, "Bob", "WR", null), T0, default);
        await command.HandleAsync(Add(2, "Alice", "WR", null), T0.AddMinutes(1), default);

        var chart = await query.GetFullChartAsync(new GetFullDepthChartQuery("NFL", "tb", T0.AddSeconds(30)), default);

        Assert.Equal([1], chart[new Position("WR")].Select(p => p.Id));
    }

    [Fact]
    public async Task UnknownTeam_EmptyResults()
    {
        var (query, _) = Handlers();

        Assert.Empty(await query.GetFullChartAsync(new GetFullDepthChartQuery("NFL", "zz", null), default));
        Assert.Empty(await query.GetPlayersUnderAsync(new GetPlayersUnderQuery("NFL", "zz", "WR", 1, null), default));
    }

    [Fact]
    public async Task InvalidQueryInput_ThrowsDomainValidation()
    {
        var (query, _) = Handlers();

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            query.GetFullChartAsync(new GetFullDepthChartQuery("NFL", "", null), default));
    }
}
