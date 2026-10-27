using System.Globalization;
using DepthCharts.Application.Contracts;
using DepthCharts.Domain;

namespace DepthCharts.Producer.Cli;

public sealed class CliUsageException(string message) : Exception(message);

public static class CliParser
{
    public const string Usage = """
        producer add    <sport> <team> <position> <playerId> <name> [depth]
        producer remove <sport> <team> <position> <playerId>
        producer chart  <sport> <team> [--as-of <iso8601>]
        producer below  <sport> <team> <position> <playerId> [--as-of <iso8601>]
        producer demo
        """;

    public static CliRequest Parse(string[] args)
    {
        if (args.Length == 0) throw new CliUsageException("Missing command.");
        try
        {
            return args[0] switch
            {
                "add" when args.Length is 6 or 7 => ParseAdd(args),
                "remove" when args.Length == 5 => ParseRemove(args),
                "chart" => ParseChart(args),
                "below" => ParseBelow(args),
                "demo" when args.Length == 1 => new DemoRequest(),
                _ => throw new CliUsageException($"Invalid arguments for '{args[0]}'."),
            };
        }
        catch (DomainValidationException e)
        {
            throw new CliUsageException(e.Message);
        }
    }

    private static CliRequest ParseAdd(string[] a)
    {
        _ = new Sport(a[1]);
        _ = new Team(a[2]);
        _ = new Position(a[3]);
        var id = ParseInt(a[4], "playerId");
        _ = new Player(id, a[5], null);
        int? depth = null;
        if (a.Length == 7)
        {
            depth = ParseInt(a[6], "depth");
            if (depth < 0) throw new CliUsageException($"depth must not be negative: '{a[6]}'.");
        }
        return new PublishRequest(new AddPlayerCommand(Guid.NewGuid(), a[1], a[2], a[3], id, a[5], null, depth));
    }

    private static CliRequest ParseRemove(string[] a)
    {
        ValidateKey(a);
        return new PublishRequest(new RemovePlayerCommand(Guid.NewGuid(), a[1], a[2], a[3], ParsePlayerId(a[4])));
    }

    private static CliRequest ParseChart(string[] a)
    {
        if (a.Length < 3) throw new CliUsageException("Invalid arguments for 'chart'.");
        _ = new Sport(a[1]);
        _ = new Team(a[2]);
        return new ChartRequest(a[1], a[2], ParseAsOf(a, 3));
    }

    private static CliRequest ParseBelow(string[] a)
    {
        if (a.Length < 5) throw new CliUsageException("Invalid arguments for 'below'.");
        ValidateKey(a);
        return new BelowRequest(a[1], a[2], a[3], ParsePlayerId(a[4]), ParseAsOf(a, 5));
    }

    private static void ValidateKey(string[] a)
    {
        _ = new Sport(a[1]);
        _ = new Team(a[2]);
        _ = new Position(a[3]);
    }

    private static int ParsePlayerId(string s)
    {
        var id = ParseInt(s, "playerId");
        if (id <= 0) throw new CliUsageException($"playerId must be positive: '{s}'.");
        return id;
    }

    private static int ParseInt(string s, string name) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : throw new CliUsageException($"{name} must be an integer: '{s}'.");

    // Optional trailing "--as-of <iso8601>" starting at index i.
    private static DateTimeOffset? ParseAsOf(string[] a, int i)
    {
        if (a.Length == i) return null;
        if (a.Length != i + 2 || a[i] != "--as-of") throw new CliUsageException($"Invalid arguments for '{a[0]}'.");
        return DateTimeOffset.TryParse(a[i + 1], CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var t)
            ? t
            : throw new CliUsageException($"--as-of must be an ISO-8601 instant: '{a[i + 1]}'.");
    }
}
