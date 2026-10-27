using DepthCharts.Application;
using DepthCharts.Application.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DepthCharts.Tests.Api;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("Kafka:ConsumerEnabled", "false");

    public async Task SeedAsync(DepthChartCommand command, DateTimeOffset occurredAt)
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DepthChartCommandHandler>()
            .HandleAsync(command, occurredAt, default);
    }
}
