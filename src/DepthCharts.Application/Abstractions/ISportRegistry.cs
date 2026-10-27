using DepthCharts.Domain;

namespace DepthCharts.Application.Abstractions;

public interface ISportRegistry
{
    /// <summary>The positions a sport allows, or null when any position is allowed.</summary>
    Task<IReadOnlySet<Position>?> GetAllowedPositionsAsync(Sport sport, CancellationToken ct);
}
