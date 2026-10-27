using System.Text.Json;
using Confluent.Kafka;
using DepthCharts.Application.Abstractions;
using DepthCharts.Producer.Api;
using DepthCharts.Producer.Cli;

namespace DepthCharts.Producer;

public static class ProducerApp
{
    // publisher/api are factories so that building them (bad Kafka or Api config) fails inside the handled region.
    public static async Task<int> RunAsync(
        string[] args, Func<ICommandPublisher> publisher, Func<DepthChartApiClient> api,
        TextWriter output, TextWriter error, CancellationToken ct)
    {
        try
        {
            switch (CliParser.Parse(args))
            {
                case PublishRequest p:
                    await publisher().PublishAsync(p.Command, ct);
                    output.WriteLine($"Published {p.Command.GetType().Name} {p.Command.CommandId}");
                    return 0;
                case ChartRequest c:
                    output.WriteLine(ChartFormatter.FormatChart(await api().GetChartAsync(c.Sport, c.Team, c.AsOf, ct)));
                    return 0;
                case BelowRequest b:
                    output.WriteLine(ChartFormatter.FormatIds(
                        await api().GetBelowAsync(b.Sport, b.Team, b.Position, b.PlayerId, b.AsOf, ct)));
                    return 0;
                default:
                    return await new DemoRunner(publisher(), api(), output, error).RunAsync(ct);
            }
        }
        catch (CliUsageException e)
        {
            error.WriteLine(e.Message);
            error.WriteLine(CliParser.Usage);
            return 1;
        }
        catch (OperationCanceledException) // Ctrl+C, or an HttpClient timeout (TaskCanceledException)
        {
            error.WriteLine(ct.IsCancellationRequested ? "Cancelled." : "Request timed out.");
            return 1;
        }
        catch (Exception e) when (e is HttpRequestException or KafkaException or UriFormatException or JsonException)
        {
            error.WriteLine(e.Message);
            return 1;
        }
    }

    // "localhost:5050" parses as scheme "localhost" and only fails later, so require http(s) up front.
    public static Uri ParseBaseUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri
            : throw new UriFormatException($"Api:BaseUrl must be an absolute http(s) URL: '{url}'.");
}
