using DepthCharts.Domain.Events;

namespace DepthCharts.Application.Abstractions;

public interface IEventStore
{
    Task<IReadOnlyList<IDomainEvent>> LoadAsync(string streamId, DateTimeOffset? asOf, CancellationToken ct);
    Task<bool> HasProcessedAsync(Guid commandId, CancellationToken ct);
    Task AppendAsync(string streamId, int expectedVersion, Guid commandId,
                     IReadOnlyList<IDomainEvent> events, CancellationToken ct);
}
