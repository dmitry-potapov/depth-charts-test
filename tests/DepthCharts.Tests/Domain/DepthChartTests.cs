using DepthCharts.Domain;
using DepthCharts.Domain.Events;

namespace DepthCharts.Tests.Domain;

public class DepthChartTests
{
    static readonly Sport NFL = new("NFL");
    static readonly Team TB = new("tb");
    static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    static DepthChart New() => new(NFL, TB);
    static Position P(string p) => new(p);
    static Player Pl(int i, string? name = null) => new(i, name ?? $"P{i}", null);

    static void Do(DepthChart c, IEnumerable<IDomainEvent> es) { foreach (var e in es) c.Apply(e); }

    static int[] Ids(DepthChart c, string pos) =>
        c.GetFullChart().TryGetValue(P(pos), out var l) ? l.Select(p => p.Id).ToArray() : [];

    static void Add(DepthChart c, int id, string pos, int? depth) => Do(c, c.AddPlayer(Pl(id), P(pos), depth, T0));

    static DepthChart Chart(string pos, params int[] ids)
    {
        var c = New();
        foreach (var i in ids) Add(c, i, pos, null);
        return c;
    }

    [Fact]
    public void BriefExample_ProducesExpectedChart()
    {
        // Arrange
        var c = New();

        // Act
        Add(c, 1, "WR", 0);
        Add(c, 2, "WR", 0);
        Add(c, 3, "WR", 2);
        Add(c, 1, "KR", null);

        // Assert
        Assert.Equal([2, 1, 3], Ids(c, "WR"));
        Assert.Equal([1], Ids(c, "KR"));
        Assert.Equal(["WR", "KR"], c.GetFullChart().Keys.Select(k => k.Value));
        Assert.Equal([1, 3], c.GetPlayersUnder(2, P("WR")).Select(p => p.Id));
    }

    [Fact]
    public void AddPlayer_OccupiedDepth_BumpsDown()
    {
        // Arrange
        var c = Chart("WR", 1, 2, 3);

        // Act
        Add(c, 4, "WR", 1);

        // Assert
        Assert.Equal([1, 4, 2, 3], Ids(c, "WR"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(5)]
    public void AddPlayer_NullOrPastEnd_Appends(int? depth)
    {
        // Arrange
        var c = Chart("WR", 1, 2);

        // Act
        var events = c.AddPlayer(Pl(3), P("WR"), depth, T0);

        // Assert
        Assert.Equal(2, Assert.IsType<PlayerAddedToDepthChart>(Assert.Single(events)).Depth);
        Do(c, events);
        Assert.Equal([1, 2, 3], Ids(c, "WR"));
    }

    [Fact]
    public void AddPlayer_NegativeDepth_Throws() =>
        Assert.Throws<DomainValidationException>(() => New().AddPlayer(Pl(1), P("WR"), -1, T0));

    [Fact]
    public void AddPlayer_ExistingPlayer_Moves()
    {
        // Arrange
        var c = Chart("WR", 1, 2, 3);

        // Act
        Add(c, 3, "WR", 0);

        // Assert
        Assert.Equal([3, 1, 2], Ids(c, "WR"));
    }

    [Fact]
    public void AddPlayer_MovePastEnd_ResolvesAgainstListWithoutPlayer()
    {
        // Arrange
        var c = Chart("WR", 1, 2, 3);

        // Act
        var events = c.AddPlayer(Pl(1), P("WR"), 3, T0);

        // Assert
        Assert.Equal(2, Assert.IsType<PlayerAddedToDepthChart>(Assert.Single(events)).Depth);
        Do(c, events);
        Assert.Equal([2, 3, 1], Ids(c, "WR"));
    }

    [Fact]
    public void AddPlayer_ExistingPlayerNewName_UpdatesName()
    {
        // Arrange
        var c = New();
        Do(c, c.AddPlayer(Pl(1, "Bob"), P("WR"), null, T0));

        // Act
        Do(c, c.AddPlayer(Pl(1, "Robert"), P("WR"), null, T0));

        // Assert
        Assert.Equal("Robert", Assert.Single(c.GetFullChart()[P("WR")]).Name);
    }

    [Fact]
    public void AddPlayer_ExistingPlayerNewName_UpdatesNameInEveryPosition()
    {
        // Arrange
        var c = New();
        Do(c, c.AddPlayer(Pl(1, "Bob"), P("WR"), null, T0));
        Do(c, c.AddPlayer(Pl(1, "Bob"), P("KR"), null, T0));

        // Act
        Do(c, c.AddPlayer(Pl(1, "Robert"), P("WR"), null, T0));

        // Assert
        Assert.All(c.GetFullChart().Values, l => Assert.Equal("Robert", Assert.Single(l).Name));
    }

    [Fact]
    public void Apply_DepthBeyondList_AppendsInsteadOfThrowing()
    {
        // Arrange
        var c = New();

        // Act
        c.Apply(new PlayerAddedToDepthChart(NFL, TB, Pl(2), P("WR"), 1, T0));

        // Assert
        Assert.Equal([2], Ids(c, "WR"));
    }

    [Fact]
    public void Apply_UnknownEvent_ThrowsAndKeepsVersion()
    {
        // Arrange
        var c = New();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => c.Apply(new UnknownEvent(NFL, TB, T0)));
        Assert.Equal(0, c.Version);
    }

    [Fact]
    public void Constructors_RejectNulls()
    {
        Assert.Throws<DomainValidationException>(() => new DepthChart(null!, TB));
        Assert.Throws<DomainValidationException>(() => new DepthChart(NFL, null!));
    }

    sealed record UnknownEvent(Sport Sport, Team Team, DateTimeOffset OccurredAt) : IDomainEvent;

    [Fact]
    public void AddPlayer_EmitsEventWithChartIdentity()
    {
        // Act
        var e = Assert.IsType<PlayerAddedToDepthChart>(Assert.Single(New().AddPlayer(Pl(1), P("WR"), null, T0)));

        // Assert
        Assert.Equal(NFL, e.Sport);
        Assert.Equal(TB, e.Team);
        Assert.Equal(T0, e.OccurredAt);
    }

    [Fact]
    public void AddPlayer_DoesNotMutateUntilApplied()
    {
        // Arrange
        var c = New();

        // Act
        var events = c.AddPlayer(Pl(1), P("WR"), null, T0);

        // Assert
        Assert.Empty(c.GetFullChart());
        Assert.Equal(0, c.Version);

        // Act
        Do(c, events);

        // Assert
        Assert.Equal(1, c.Version);
    }

    [Fact]
    public void RemovePlayer_Present_EmitsEventAndRemoves()
    {
        // Arrange
        var c = Chart("WR", 1, 2);

        // Act
        var events = c.RemovePlayer(1, P("WR"), T0);

        // Assert
        var e = Assert.IsType<PlayerRemovedFromDepthChart>(Assert.Single(events));
        Assert.Equal(NFL, e.Sport);
        Assert.Equal(TB, e.Team);
        Do(c, events);
        Assert.Equal([2], Ids(c, "WR"));
    }

    [Fact]
    public void RemovePlayer_Absent_EmitsNothing()
    {
        // Arrange
        var c = New();

        // Act & Assert
        Assert.Empty(c.RemovePlayer(1, P("WR"), T0));

        // Arrange
        Add(c, 2, "WR", null);

        // Act & Assert
        Assert.Empty(c.RemovePlayer(1, P("WR"), T0));
    }

    [Fact]
    public void RemovePlayer_LastOnPosition_PositionNotInFullChart()
    {
        // Arrange
        var c = New();
        Add(c, 1, "WR", null);
        Add(c, 1, "KR", null);

        // Act
        Do(c, c.RemovePlayer(1, P("WR"), T0));

        // Assert
        Assert.Equal(["KR"], c.GetFullChart().Keys.Select(k => k.Value));
    }

    [Fact]
    public void GetPlayersUnder_LastPlayer_Empty() =>
        Assert.Empty(Chart("WR", 1, 2).GetPlayersUnder(2, P("WR")));

    [Fact]
    public void GetPlayersUnder_AbsentPlayer_Empty() =>
        Assert.Empty(Chart("WR", 1, 2).GetPlayersUnder(9, P("WR")));

    [Fact]
    public void GetPlayersUnder_AbsentPosition_Empty() =>
        Assert.Empty(Chart("WR", 1, 2).GetPlayersUnder(1, P("KR")));

    [Fact]
    public void SamePlayer_OnSeveralPositions_Independent()
    {
        // Arrange
        var c = New();
        Add(c, 1, "WR", null);
        Add(c, 1, "KR", null);

        // Act
        Do(c, c.RemovePlayer(1, P("WR"), T0));

        // Assert
        Assert.Equal([1], Ids(c, "KR"));
        Assert.Empty(Ids(c, "WR"));
    }

    [Fact]
    public void MlbPositions_Work()
    {
        // Arrange
        var c = new DepthChart(new Sport("MLB"), TB);

        // Act
        Add(c, 1, "1B", null);
        Add(c, 2, "SP", null);

        // Assert
        Assert.Equal([1], Ids(c, "1B"));
        Assert.Equal([2], Ids(c, "SP"));
    }

    [Fact]
    public void Apply_NegativeDepth_ClampsWithoutLosingPlayers()
    {
        // Arrange
        var c = Chart("WR", 1, 2);

        // Act
        c.Apply(new PlayerAddedToDepthChart(NFL, TB, Pl(1), P("WR"), -1, T0));

        // Assert
        Assert.Equal([1, 2], Ids(c, "WR"));
        Assert.Equal(3, c.Version);
    }

    [Fact]
    public void Apply_EventForOtherSport_Throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            New().Apply(new PlayerAddedToDepthChart(new Sport("MLB"), TB, Pl(2), P("WR"), 0, T0)));

    [Fact]
    public void Apply_EventForOtherTeam_Throws()
    {
        // Arrange
        var c = Chart("WR", 1);
        var foreign = new PlayerAddedToDepthChart(NFL, new Team("ne"), Pl(2), P("WR"), 0, T0);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => c.Apply(foreign));
        Assert.Throws<InvalidOperationException>(() => DepthChart.Replay(NFL, TB, [foreign]));
        Assert.Equal([1], Ids(c, "WR"));
    }

    [Fact]
    public void Methods_RejectNullArgumentsAndNonPositiveIds()
    {
        // Arrange
        var c = Chart("WR", 1);

        // Act & Assert
        Assert.Throws<DomainValidationException>(() => c.AddPlayer(null!, P("WR"), null, T0));
        Assert.Throws<DomainValidationException>(() => c.AddPlayer(Pl(2), null!, null, T0));
        Assert.Throws<DomainValidationException>(() => c.RemovePlayer(0, P("WR"), T0));
        Assert.Throws<DomainValidationException>(() => c.RemovePlayer(1, null!, T0));
        Assert.Throws<DomainValidationException>(() => c.GetPlayersUnder(-1, P("WR")));
        Assert.Throws<DomainValidationException>(() => c.GetPlayersUnder(1, null!));
    }

    [Fact]
    public void StreamIdFor_IsLowerCase() =>
        Assert.Equal("nfl:tb", DepthChart.StreamIdFor(new Sport("NFL"), new Team("TB")));
}
