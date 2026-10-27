using DepthCharts.Application.Abstractions;
using DepthCharts.Infrastructure;
using DepthCharts.Producer;
using DepthCharts.Producer.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// No args passed to the builder: CLI tokens must not leak into configuration.
var builder = Host.CreateApplicationBuilder();
builder.Services.AddCommandPublisher(builder.Configuration);
using var host = builder.Build();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

return await ProducerApp.RunAsync(
    args,
    () => host.Services.GetRequiredService<ICommandPublisher>(),
    () => new DepthChartApiClient(new HttpClient
    {
        BaseAddress = ProducerApp.ParseBaseUrl(builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5050"),
    }),
    Console.Out, Console.Error, cts.Token);
