using System.Text.Json;

namespace DepthCharts.Application.Contracts;

public static class CommandSerializer
{
    // Out-of-order metadata lets hand-typed JSON put "type" anywhere.
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { AllowOutOfOrderMetadataProperties = true };

    public static string Serialize(DepthChartCommand command) =>
        JsonSerializer.Serialize(command, Options);

    public static DepthChartCommand Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<DepthChartCommand>(json, Options)
                ?? throw new JsonException("Command must not be null.");
        }
        catch (NotSupportedException e)
        {
            // A missing or unknown "type" surfaces as NotSupported on the abstract base.
            throw new JsonException("Missing or unknown command type.", e);
        }
    }
}
