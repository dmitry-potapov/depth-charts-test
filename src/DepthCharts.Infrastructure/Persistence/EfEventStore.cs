using System.Text.Json;
using DepthCharts.Application.Abstractions;
using DepthCharts.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace DepthCharts.Infrastructure.Persistence;

// shortcut: InMemory enforces no unique index or transaction, so this is correct only with the single
// sequential consumer; upgrade = relational provider with unique (StreamId, Version) and unique CommandId.
public sealed class EfEventStore(EventStoreDbContext db) : IEventStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Dictionary<string, Type> EventTypes = new[]
    {
        typeof(PlayerAddedToDepthChart),
        typeof(PlayerRemovedFromDepthChart),
    }.ToDictionary(t => t.Name);

    public async Task<IReadOnlyList<IDomainEvent>> LoadAsync(string streamId, DateTimeOffset? asOf, CancellationToken ct)
    {
        var rows = await db.Events.Where(e => e.StreamId == streamId).OrderBy(e => e.Version).ToListAsync(ct);
        // Stop at the first late event so the result is always a prefix, even if timestamps go backwards.
        return rows.TakeWhile(e => asOf is not { } cutoff || e.OccurredAt <= cutoff).Select(Deserialize).ToList();
    }

    public Task<bool> HasProcessedAsync(Guid commandId, CancellationToken ct) =>
        db.ProcessedCommands.AnyAsync(c => c.CommandId == commandId, ct);

    public async Task AppendAsync(string streamId, int expectedVersion, Guid commandId,
        IReadOnlyList<IDomainEvent> events, CancellationToken ct)
    {
        var actual = await db.Events.Where(e => e.StreamId == streamId).MaxAsync(e => (int?)e.Version, ct) ?? 0;
        if (actual != expectedVersion)
            throw new ConcurrencyException($"Stream {streamId} is at version {actual}, expected {expectedVersion}.");

        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            db.Events.Add(new StoredEvent
            {
                StreamId = streamId,
                Version = expectedVersion + i + 1,
                EventType = e.GetType().Name,
                Data = JsonSerializer.Serialize(e, e.GetType(), Json),
                OccurredAt = e.OccurredAt,
                CommandId = commandId,
            });
        }
        db.ProcessedCommands.Add(new ProcessedCommand { CommandId = commandId });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Don't let the failed rows ride along with the next append in this scope.
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private static IDomainEvent Deserialize(StoredEvent row) =>
        EventTypes.TryGetValue(row.EventType, out var type)
            ? (IDomainEvent)JsonSerializer.Deserialize(row.Data, type, Json)!
            : throw new InvalidOperationException($"Unknown event type {row.EventType}.");
}
