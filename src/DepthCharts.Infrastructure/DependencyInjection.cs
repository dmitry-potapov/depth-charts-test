using DepthCharts.Application.Abstractions;
using DepthCharts.Infrastructure.Messaging;
using DepthCharts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DepthCharts.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // A per-provider root keeps each host (and each WebApplicationFactory) isolated.
        services.AddSingleton(new InMemoryDatabaseRoot());
        services.AddDbContext<EventStoreDbContext>((sp, o) =>
            o.UseInMemoryDatabase("depth-charts", sp.GetRequiredService<InMemoryDatabaseRoot>()));
        services.AddScoped<IEventStore, EfEventStore>();
        services.Configure<KafkaOptions>(configuration.GetSection("Kafka"));
        services.AddSingleton<CommandMessageProcessor>();
        services.AddHostedService<KafkaCommandConsumer>();
        return services;
    }

    // For the Producer CLI: publishing only, no store or consumer.
    public static IServiceCollection AddCommandPublisher(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<KafkaOptions>(configuration.GetSection("Kafka"));
        services.AddSingleton<ICommandPublisher, KafkaCommandPublisher>();
        return services;
    }
}
