using DepthCharts.Application;
using DepthCharts.Application.Abstractions;
using DepthCharts.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DepthCharts.Tests.Infrastructure;

public class DependencyInjectionTests
{
    private static ServiceProvider Build() =>
        new ServiceCollection().AddApplication().AddInfrastructure(new ConfigurationBuilder().Build()).BuildServiceProvider(validateScopes: true);

    [Fact]
    public async Task Handlers_ResolveFromScope()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<DepthChartCommandHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<DepthChartQueryHandler>());
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IEventStore>().LoadAsync("nfl:tb", null, default));
    }

    [Fact]
    public async Task Scopes_ShareData_ButProvidersAreIsolated()
    {
        using var a = Build();
        using var b = Build();
        var id = Guid.NewGuid();
        using (var scope = a.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IEventStore>().AppendAsync("nfl:tb", 0, id, [], default);

        using var sameProvider = a.CreateScope();
        using var otherProvider = b.CreateScope();
        Assert.True(await sameProvider.ServiceProvider.GetRequiredService<IEventStore>().HasProcessedAsync(id, default));
        Assert.False(await otherProvider.ServiceProvider.GetRequiredService<IEventStore>().HasProcessedAsync(id, default));
    }
}
