using DepthCharts.Application.Abstractions;
using DepthCharts.Domain;
using DepthCharts.Domain.Events;
using DepthCharts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DepthCharts.Tests.Infrastructure;

public class EfEventStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly Sport Nfl = new("NFL");
    private static readonly Team Tb = new("tb");
    private static readonly Position Wr = new("WR");

    private static PlayerAddedToDepthChart Added(int id, Position? natural, DateTimeOffset at, Team? team = null) =>
        new(Nfl, team ?? Tb, new Player(id, "P" + id, natural), Wr, 0, at);

    [Fact]
    public async Task AppendThenLoad_RoundTripsAllFields()
    {
        // Arrange
        var store = TestStore.Create();
        IDomainEvent[] events =
        [
            Added(1, Wr, T0),
            Added(2, null, T0.AddMinutes(1)),
            new PlayerRemovedFromDepthChart(Nfl, Tb, 1, Wr, T0.AddMinutes(2)),
        ];

        // Act
        await store.AppendAsync("nfl:tb", 0, Guid.NewGuid(), events, default);

        // Assert
        Assert.Equal(events, await store.LoadAsync("nfl:tb", null, default));
    }

    [Fact]
    public async Task Load_UnknownStream_Empty() =>
        Assert.Empty(await TestStore.Create().LoadAsync("nfl:zz", null, default));

    [Fact]
    public async Task Load_OtherStreamsExcluded()
    {
        // Arrange
        var store = TestStore.Create();
        var tbEvent = Added(1, null, T0);
        await store.AppendAsync("nfl:tb", 0, Guid.NewGuid(), [tbEvent], default);
        await store.AppendAsync("nfl:ne", 0, Guid.NewGuid(), [Added(2, null, T0, new Team("ne"))], default);

        // Act & Assert
        Assert.Equal([tbEvent], await store.LoadAsync("nfl:tb", null, default));
    }

    [Fact]
    public async Task Load_AsOfBetweenEvents_ReturnsEarlier()
    {
        // Arrange
        var store = await ThreeEvents();

        // Act
        var loaded = await store.LoadAsync("nfl:tb", T0.AddMinutes(1).AddSeconds(30), default);

        // Assert
        Assert.Equal([1, 2], loaded.Cast<PlayerAddedToDepthChart>().Select(e => e.Player.Id));
    }

    [Fact]
    public async Task Load_AsOfEqualToEvent_IncludesIt()
    {
        // Arrange
        var store = await ThreeEvents();

        // Act
        var loaded = await store.LoadAsync("nfl:tb", T0.AddMinutes(1), default);

        // Assert
        Assert.Equal([1, 2], loaded.Cast<PlayerAddedToDepthChart>().Select(e => e.Player.Id));
    }

    [Fact]
    public async Task Append_StaleExpectedVersion_Throws()
    {
        // Arrange
        var store = TestStore.Create();
        await store.AppendAsync("nfl:tb", 0, Guid.NewGuid(), [Added(1, null, T0)], default);

        // Act & Assert
        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            store.AppendAsync("nfl:tb", 0, Guid.NewGuid(), [Added(2, null, T0)], default));

        // Assert
        Assert.Single(await store.LoadAsync("nfl:tb", null, default));
    }

    [Fact]
    public async Task Append_SecondBatch_ContinuesVersions()
    {
        // Arrange
        var store = TestStore.Create();
        await store.AppendAsync("nfl:tb", 0, Guid.NewGuid(), [Added(1, null, T0)], default);

        // Act
        await store.AppendAsync("nfl:tb", 1, Guid.NewGuid(), [Added(2, null, T0), Added(3, null, T0)], default);

        // Assert
        Assert.Equal(3, (await store.LoadAsync("nfl:tb", null, default)).Count);
    }

    [Fact]
    public async Task HasProcessed_TrueOnlyForAppendedCommandId()
    {
        // Arrange
        var store = TestStore.Create();
        var id = Guid.NewGuid();
        await store.AppendAsync("nfl:tb", 0, id, [Added(1, null, T0)], default);

        // Act & Assert
        Assert.True(await store.HasProcessedAsync(id, default));
        Assert.False(await store.HasProcessedAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Load_AsOfWithOutOfOrderTimestamps_ReturnsPrefix()
    {
        // Arrange
        var store = TestStore.Create();
        await store.AppendAsync("nfl:tb", 0, Guid.NewGuid(), [Added(1, null, T0.AddMinutes(5)), Added(2, null, T0)], default);

        // Act & Assert
        Assert.Empty(await store.LoadAsync("nfl:tb", T0.AddMinutes(2), default));
    }

    [Fact]
    public async Task Append_NoEvents_StillMarksProcessed()
    {
        // Arrange
        var store = TestStore.Create();
        var id = Guid.NewGuid();

        // Act
        await store.AppendAsync("nfl:tb", 0, id, [], default);

        // Assert
        Assert.True(await store.HasProcessedAsync(id, default));
        Assert.Empty(await store.LoadAsync("nfl:tb", null, default));
    }

    [Fact]
    public async Task Append_AfterFailedSave_DoesNotWriteStaleRows()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<EventStoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new FailOnceInterceptor())
            .Options;
        var store = new EfEventStore(new EventStoreDbContext(options));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.AppendAsync("nfl:tb", 0, Guid.NewGuid(), [Added(1, null, T0)], default));

        // Act
        await store.AppendAsync("nfl:tb", 0, Guid.NewGuid(), [Added(2, null, T0)], default);

        // Assert
        var only = Assert.IsType<PlayerAddedToDepthChart>(Assert.Single(await store.LoadAsync("nfl:tb", null, default)));
        Assert.Equal(2, only.Player.Id);
    }

    [Fact]
    public async Task Load_UnknownStoredEventType_Throws()
    {
        // Arrange
        var db = TestStore.NewContext();
        db.Events.Add(new StoredEvent { StreamId = "nfl:tb", Version = 1, EventType = "PlayerRenamed", Data = "{}", OccurredAt = T0 });
        await db.SaveChangesAsync();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => new EfEventStore(db).LoadAsync("nfl:tb", null, default));
    }

    private sealed class FailOnceInterceptor : SaveChangesInterceptor
    {
        private bool _failed;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (_failed) return base.SavingChangesAsync(eventData, result, ct);
            _failed = true;
            throw new InvalidOperationException("boom");
        }
    }

    private static async Task<IEventStore> ThreeEvents()
    {
        var store = TestStore.Create();
        await store.AppendAsync("nfl:tb", 0, Guid.NewGuid(),
            [Added(1, null, T0), Added(2, null, T0.AddMinutes(1)), Added(3, null, T0.AddMinutes(2))], default);
        return store;
    }
}
