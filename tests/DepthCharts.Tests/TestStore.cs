using DepthCharts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DepthCharts.Tests;

public static class TestStore
{
    public static EfEventStore Create() =>
        new(new EventStoreDbContext(new DbContextOptionsBuilder<EventStoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options));
}
