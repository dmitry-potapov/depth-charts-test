namespace DepthCharts.Infrastructure.Persistence;

public sealed class StoredEvent
{
    public long Id { get; set; }
    public required string StreamId { get; set; }
    public int Version { get; set; }
    public required string EventType { get; set; }
    public required string Data { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid CommandId { get; set; }
}
