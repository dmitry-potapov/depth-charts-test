using DepthCharts.Application.Abstractions;
using DepthCharts.Domain;
using Microsoft.EntityFrameworkCore;

namespace DepthCharts.Infrastructure.Persistence;

public sealed class EfSportRegistry(EventStoreDbContext db) : ISportRegistry
{
    public async Task<IReadOnlySet<Position>?> GetAllowedPositionsAsync(Sport sport, CancellationToken ct)
    {
        var definition = await db.Sports.AsNoTracking().FirstOrDefaultAsync(s => s.Sport == sport.Value, ct);
        // A sport with no row accepts any position. Adding or rejecting unknown sports would be handled
        // separately (sport management) in a complete feature.
        if (definition?.AllowedPositions is not { Count: > 0 } positions) return null;
        return positions.Select(p => new Position(p)).ToHashSet();
    }
}
