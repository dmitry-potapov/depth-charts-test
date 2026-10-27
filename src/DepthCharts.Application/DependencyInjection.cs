using Microsoft.Extensions.DependencyInjection;

namespace DepthCharts.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services) =>
        services.AddScoped<DepthChartCommandHandler>().AddScoped<DepthChartQueryHandler>();
}
