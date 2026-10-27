using DepthCharts.Application.Contracts;

namespace DepthCharts.Application.Abstractions;

public interface ICommandPublisher
{
    Task PublishAsync(DepthChartCommand command, CancellationToken ct);
}
