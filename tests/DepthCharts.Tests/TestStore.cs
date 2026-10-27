using DepthCharts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DepthCharts.Tests;

public static class TestStore
{
    public static EfEventStore Create() => new(NewContext());

    // Seeded with the brief's NFL and MLB positions.
    public static EfSportRegistry Sports() => new(NewContext());

    public static EventStoreDbContext NewContext()
    {
        var db = new EventStoreDbContext(new DbContextOptionsBuilder<EventStoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        db.Database.EnsureCreated();
        return db;
    }
}
