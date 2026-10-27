using Microsoft.EntityFrameworkCore;

namespace DepthCharts.Infrastructure.Persistence;

public sealed class EventStoreDbContext(DbContextOptions<EventStoreDbContext> options) : DbContext(options)
{
    public DbSet<StoredEvent> Events => Set<StoredEvent>();
    public DbSet<ProcessedCommand> ProcessedCommands => Set<ProcessedCommand>();
    public DbSet<SportDefinition> Sports => Set<SportDefinition>();

    // The InMemory provider has no migrations, so the brief's sports are seeded with HasData + EnsureCreated.
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<SportDefinition>().HasData(
            new SportDefinition { Sport = "NFL", AllowedPositions = ["QB", "WR", "RB", "TE", "K", "P", "KR", "PR"] },
            new SportDefinition { Sport = "MLB", AllowedPositions = ["SP", "RP", "C", "1B", "2B", "3B", "SS", "LF", "RF", "CF", "DH"] });
}
