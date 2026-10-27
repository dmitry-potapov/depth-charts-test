using DepthCharts.Application;
using DepthCharts.Application.Abstractions;
using DepthCharts.Application.Contracts;
using DepthCharts.Domain;
using DepthCharts.Domain.Events;

namespace DepthCharts.Tests.Application;

public class DepthChartCommandHandlerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static AddPlayerCommand Add(Guid? id = null, string sport = "nfl", string team = "TB", int playerId = 1, string name = "Bob") =>
        new(id ?? Guid.NewGuid(), sport, team, "WR", playerId, name, null, null);

    [Fact]
    public async Task Handle_AddPlayer_AppendsEvent()
    {
        var store = TestStore.Create();

        await new DepthChartCommandHandler(store).HandleAsync(Add(), T0, default);

        var added = Assert.IsType<PlayerAddedToDepthChart>(Assert.Single(await store.LoadAsync("nfl:tb", null, default)));
        Assert.Equal(T0, added.OccurredAt);
    }

    [Fact]
    public async Task Handle_DuplicateCommandId_Ignored()
    {
        var store = TestStore.Create();
        var handler = new DepthChartCommandHandler(store);
        var addId = Guid.NewGuid();
        await handler.HandleAsync(Add(addId), T0, default);
        await handler.HandleAsync(new RemovePlayerCommand(Guid.NewGuid(), "nfl", "TB", "WR", 1), T0, default);

        await handler.HandleAsync(Add(addId), T0, default);

        var events = await store.LoadAsync("nfl:tb", null, default);
        Assert.Equal(2, events.Count);
        var chart = DepthChart.Replay(new Sport("nfl"), new Team("tb"), events);
        Assert.Empty(chart.GetFullChart());
    }

    [Fact]
    public async Task Handle_NoOpRemove_AppendsNothing()
    {
        var store = TestStore.Create();
        var id = Guid.NewGuid();

        await new DepthChartCommandHandler(store).HandleAsync(new RemovePlayerCommand(id, "nfl", "tb", "WR", 1), T0, default);

        Assert.Empty(await store.LoadAsync("nfl:tb", null, default));
        Assert.True(await store.HasProcessedAsync(id, default));
    }

    [Fact]
    public async Task Handle_RedeliveredNoOpRemove_DoesNotRemoveLaterAddedPlayer()
    {
        var store = TestStore.Create();
        var handler = new DepthChartCommandHandler(store);
        var remove = new RemovePlayerCommand(Guid.NewGuid(), "nfl", "tb", "WR", 1);
        var add = Add();
        await handler.HandleAsync(remove, T0, default);
        await handler.HandleAsync(add, T0, default);

        await handler.HandleAsync(remove, T0, default);
        await handler.HandleAsync(add, T0, default);

        var chart = DepthChart.Replay(new Sport("nfl"), new Team("tb"), await store.LoadAsync("nfl:tb", null, default));
        Assert.Single(chart.GetFullChart());
    }

    [Fact]
    public async Task Handle_EmptyCommandId_Rejected()
    {
        var store = TestStore.Create();
        var handler = new DepthChartCommandHandler(store);

        await Assert.ThrowsAsync<DomainValidationException>(() => handler.HandleAsync(Add(Guid.Empty), T0, default));

        Assert.Empty(await store.LoadAsync("nfl:tb", null, default));
        Assert.False(await store.HasProcessedAsync(Guid.Empty, default));
    }

    [Fact]
    public async Task Handle_InvalidRemoveOrDepth_ThrowsAndRecordsNothing()
    {
        var store = TestStore.Create();
        var handler = new DepthChartCommandHandler(store);
        var removeId = Guid.NewGuid();
        var addId = Guid.NewGuid();

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            handler.HandleAsync(new RemovePlayerCommand(removeId, "nfl", "tb", "WR", 0), T0, default));
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            handler.HandleAsync(new AddPlayerCommand(addId, "nfl", "tb", "WR", 1, "Bob", null, -1), T0, default));

        Assert.False(await store.HasProcessedAsync(removeId, default));
        Assert.False(await store.HasProcessedAsync(addId, default));
        Assert.Empty(await store.LoadAsync("nfl:tb", null, default));
    }

    [Fact]
    public async Task Handle_InvalidInput_ThrowsDomainValidation()
    {
        var store = TestStore.Create();
        var handler = new DepthChartCommandHandler(store);

        await Assert.ThrowsAsync<DomainValidationException>(() => handler.HandleAsync(Add(team: "t:b"), T0, default));
        await Assert.ThrowsAsync<DomainValidationException>(() => handler.HandleAsync(Add(playerId: 0), T0, default));

        Assert.Empty(await store.LoadAsync("nfl:tb", null, default));
    }

    [Fact]
    public async Task Handle_DifferentCasing_SameStream()
    {
        var store = TestStore.Create();
        var handler = new DepthChartCommandHandler(store);

        await handler.HandleAsync(Add(sport: "nfl", team: "TB"), T0, default);
        await handler.HandleAsync(Add(sport: "NFL", team: "tb", playerId: 2, name: "Alice"), T0, default);

        Assert.Equal(2, (await store.LoadAsync("nfl:tb", null, default)).Count);
    }

    [Fact]
    public async Task Handle_StaleVersion_PropagatesConcurrencyException()
    {
        var real = TestStore.Create();
        await new DepthChartCommandHandler(real).HandleAsync(Add(), T0, default);

        var handler = new DepthChartCommandHandler(new StaleLoadStore(real));

        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            handler.HandleAsync(Add(playerId: 2, name: "Alice"), T0, default));
    }

    // Loads an empty stream (a stale read) but appends to the real store.
    private sealed class StaleLoadStore(IEventStore inner) : IEventStore
    {
        public Task<IReadOnlyList<IDomainEvent>> LoadAsync(string streamId, DateTimeOffset? asOf, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<IDomainEvent>>([]);

        public Task<bool> HasProcessedAsync(Guid commandId, CancellationToken ct) => inner.HasProcessedAsync(commandId, ct);

        public Task AppendAsync(string streamId, int expectedVersion, Guid commandId,
            IReadOnlyList<IDomainEvent> events, CancellationToken ct) =>
            inner.AppendAsync(streamId, expectedVersion, commandId, events, ct);
    }
}
