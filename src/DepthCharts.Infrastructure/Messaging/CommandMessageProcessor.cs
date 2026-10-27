using System.Text.Json;
using DepthCharts.Application;
using DepthCharts.Application.Contracts;
using DepthCharts.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DepthCharts.Infrastructure.Messaging;

public sealed class CommandMessageProcessor(IServiceScopeFactory scopes, ILogger<CommandMessageProcessor> logger)
{
    // No retry, no DLQ: a bad message is logged and skipped so later ones still apply.
    public async Task ProcessAsync(string? json, DateTimeOffset timestamp, CancellationToken ct)
    {
        if (json is null) // tombstone
        {
            logger.LogWarning("Skipping empty command message");
            return;
        }
        try
        {
            var command = CommandSerializer.Deserialize(json);
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<DepthChartCommandHandler>()
                .HandleAsync(command, timestamp, ct);
        }
        catch (Exception e) when (e is JsonException or DomainValidationException)
        {
            logger.LogWarning(e, "Skipping invalid command message");
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(e, "Skipping command message after unexpected error");
        }
    }
}
