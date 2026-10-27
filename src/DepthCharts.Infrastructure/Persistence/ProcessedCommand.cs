using System.ComponentModel.DataAnnotations;

namespace DepthCharts.Infrastructure.Persistence;

// Idempotency record; exists even for commands that appended no events.
public sealed class ProcessedCommand
{
    [Key]
    public Guid CommandId { get; set; }
}
