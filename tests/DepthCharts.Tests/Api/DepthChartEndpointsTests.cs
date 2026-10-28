using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DepthCharts.Application.Contracts;

namespace DepthCharts.Tests.Api;

public class DepthChartEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
    private readonly HttpClient _client = factory.CreateClient();

    private static AddPlayerCommand Add(string team, int id, string name, string position, int? depth) =>
        new(Guid.NewGuid(), "NFL", team, position, id, name, null, depth);

    [Fact]
    public async Task Seed_PositionNotAllowedForSport_Throws() =>
        await Assert.ThrowsAsync<DepthCharts.Domain.DomainValidationException>(
            () => factory.SeedAsync(Add("tpos", 1, "Bob", "SS", null), T0));

    private async Task SeedBrief(string team)
    {
        await factory.SeedAsync(Add(team, 1, "Bob", "WR", 0), T0);
        await factory.SeedAsync(Add(team, 2, "Alice", "WR", 0), T0);
        await factory.SeedAsync(Add(team, 3, "Charlie", "WR", 2), T0);
        await factory.SeedAsync(Add(team, 1, "Bob", "KR", null), T0);
    }

    private static void AssertProblem(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Chart_BriefExample_ShapeAndOrder()
    {
        // Arrange
        await SeedBrief("t1");

        // Act
        var response = await _client.GetAsync("/depth-charts/nfl/t1");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["WR", "KR"], doc.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal("[{\"playerId\":2,\"name\":\"Alice\"},{\"playerId\":1,\"name\":\"Bob\"},{\"playerId\":3,\"name\":\"Charlie\"}]",
            doc.RootElement.GetProperty("WR").GetRawText());
    }

    [Fact]
    public async Task Below_BriefExample()
    {
        // Arrange
        await SeedBrief("t2");

        // Act
        var players = await _client.GetFromJsonAsync<JsonElement>("/depth-charts/nfl/t2/positions/wr/players/2/below");

        // Assert
        Assert.Equal([1, 3], players.EnumerateArray().Select(p => p.GetProperty("playerId").GetInt32()));
    }

    [Fact]
    public async Task Chart_UnknownTeam_EmptyObject()
    {
        // Act
        var response = await _client.GetAsync("/depth-charts/nfl/nobody");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Below_UnknownPlayer_EmptyArray()
    {
        // Arrange
        await SeedBrief("t3");

        // Act
        var response = await _client.GetAsync("/depth-charts/nfl/t3/positions/wr/players/99/below");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Chart_AsOf_ReturnsEarlierChart()
    {
        // Arrange
        await factory.SeedAsync(Add("t4", 1, "Bob", "WR", null), T0);
        await factory.SeedAsync(Add("t4", 2, "Alice", "WR", null), T0.AddHours(1));
        var asOf = Uri.EscapeDataString(T0.AddMinutes(30).ToString("O"));

        // Act
        var chart = await _client.GetFromJsonAsync<JsonElement>($"/depth-charts/nfl/t4?asOf={asOf}");

        // Assert
        Assert.Equal(1, chart.GetProperty("WR").GetArrayLength());
    }

    [Fact]
    public async Task Chart_AsOfWithOffset_ComparesAsInstant()
    {
        // Arrange
        await factory.SeedAsync(Add("t5", 1, "Bob", "WR", null), T0);

        // Act
        // 10:30+02:00 is 08:30Z (before the 09:00Z event); 11:30+02:00 is 09:30Z.
        var before = await _client.GetStringAsync("/depth-charts/nfl/t5?asOf=" + Uri.EscapeDataString("2026-10-27T10:30:00+02:00"));
        var after = await _client.GetFromJsonAsync<JsonElement>("/depth-charts/nfl/t5?asOf=" + Uri.EscapeDataString("2026-10-27T11:30:00+02:00"));

        // Assert
        Assert.Equal("{}", before);
        Assert.Equal(1, after.GetProperty("WR").GetArrayLength());
    }

    [Fact]
    public async Task Chart_InvalidTeam_400() => AssertProblem(await _client.GetAsync("/depth-charts/nfl/t:b"));

    [Fact]
    public async Task Below_NonPositivePlayerId_400() =>
        AssertProblem(await _client.GetAsync("/depth-charts/nfl/t6/positions/wr/players/0/below"));

    [Fact]
    public async Task Chart_UnparseableAsOf_400() =>
        AssertProblem(await _client.GetAsync("/depth-charts/nfl/t7?asOf=yesterday"));

    [Theory]
    [InlineData("10/11/2026")]
    [InlineData("Oct 27 2026 9:00")]
    public async Task Chart_NonIsoAsOf_400(string asOf) =>
        AssertProblem(await _client.GetAsync("/depth-charts/nfl/t7?asOf=" + Uri.EscapeDataString(asOf)));

    [Theory]
    [InlineData("2026-10-27T10:30:00+02:00")] // unencoded '+' arrives as a space
    [InlineData("2026-10-27T10:30+02:00")]
    [InlineData("2026-10-27T10:30:00+0200")]
    [InlineData("2026-10-27T08:30Z")]
    public async Task Chart_ValidIsoAsOfForms_BeforeEvent_EmptyChart(string asOf)
    {
        // Arrange
        await factory.SeedAsync(Add("t10", 1, "Bob", "WR", null), T0);

        // Act & Assert
        Assert.Equal("{}", await _client.GetStringAsync("/depth-charts/nfl/t10?asOf=" + asOf));
    }

    [Fact]
    public async Task Chart_DateOnlyAsOf_IsMidnightUtc()
    {
        // Arrange
        await factory.SeedAsync(Add("t11", 1, "Bob", "WR", null), T0);

        // Act & Assert
        Assert.Equal("{}", await _client.GetStringAsync("/depth-charts/nfl/t11?asOf=2026-10-27"));
        Assert.Contains("WR", await _client.GetStringAsync("/depth-charts/nfl/t11?asOf=2026-10-28"));
    }

    [Fact]
    public async Task Below_AsOf_ReturnsEarlierChart()
    {
        // Arrange
        await factory.SeedAsync(Add("t8", 1, "Bob", "WR", null), T0);
        await factory.SeedAsync(Add("t8", 2, "Alice", "WR", null), T0.AddHours(1));
        var asOf = Uri.EscapeDataString(T0.AddMinutes(30).ToString("O"));

        // Act
        var now = await _client.GetFromJsonAsync<JsonElement>("/depth-charts/nfl/t8/positions/wr/players/1/below");
        var past = await _client.GetFromJsonAsync<JsonElement>($"/depth-charts/nfl/t8/positions/wr/players/1/below?asOf={asOf}");

        // Assert
        Assert.Equal(1, now.GetArrayLength());
        Assert.Equal(0, past.GetArrayLength());
    }

    [Fact]
    public async Task Below_UnparseableAsOf_400() =>
        AssertProblem(await _client.GetAsync("/depth-charts/nfl/t9/positions/wr/players/1/below?asOf=garbage"));

    [Fact]
    public async Task Chart_InvalidSport_400() => AssertProblem(await _client.GetAsync("/depth-charts/n-f-l/t1"));
}
