using Microsoft.EntityFrameworkCore;

namespace DepthCharts.Infrastructure.Persistence;

public sealed class EventStoreDbContext(DbContextOptions<EventStoreDbContext> options) : DbContext(options)
{
    public DbSet<StoredEvent> Events => Set<StoredEvent>();
}
