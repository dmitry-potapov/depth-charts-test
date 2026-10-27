using Confluent.Kafka;
using DepthCharts.Application.Abstractions;
using DepthCharts.Infrastructure;
using DepthCharts.Producer;
using DepthCharts.Producer.Api;
using DepthCharts.Producer.Cli;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// No args passed to the builder: CLI tokens must not leak into configuration.
var builder = Host.CreateApplicationBuilder();
builder.Services.AddCommandPublisher(builder.Configuration);
using var host = builder.Build();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
var ct = cts.Token;

try
{
    var request = CliParser.Parse(args);
    var api = new DepthChartApiClient(new HttpClient
    {
        BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5000"),
    });
    var publisher = host.Services.GetRequiredService<ICommandPublisher>();

    switch (request)
    {
        case PublishRequest p:
            await publisher.PublishAsync(p.Command, ct);
            Console.WriteLine($"Published {p.Command.GetType().Name} {p.Command.CommandId}");
            return 0;
        case ChartRequest c:
            Console.WriteLine(ChartFormatter.FormatChart(await api.GetChartAsync(c.Sport, c.Team, c.AsOf, ct)));
            return 0;
        case BelowRequest b:
            Console.WriteLine(ChartFormatter.FormatIds(
                await api.GetBelowAsync(b.Sport, b.Team, b.Position, b.PlayerId, b.AsOf, ct)));
            return 0;
        default:
            return await new DemoRunner(publisher, api, Console.Out, Console.Error).RunAsync(ct);
    }
}
catch (CliUsageException e)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine(CliParser.Usage);
    return 1;
}
catch (OperationCanceledException) // Ctrl+C, or an HttpClient timeout (TaskCanceledException)
{
    Console.Error.WriteLine(ct.IsCancellationRequested ? "Cancelled." : "Request timed out.");
    return 1;
}
catch (Exception e) when (e is HttpRequestException or ProduceException<string, string>)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}
