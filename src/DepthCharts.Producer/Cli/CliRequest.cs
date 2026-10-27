using DepthCharts.Application.Contracts;

namespace DepthCharts.Producer.Cli;

public abstract record CliRequest;

public sealed record PublishRequest(DepthChartCommand Command) : CliRequest;

public sealed record ChartRequest(string Sport, string Team, DateTimeOffset? AsOf) : CliRequest;

public sealed record BelowRequest(string Sport, string Team, string Position, int PlayerId, DateTimeOffset? AsOf) : CliRequest;

public sealed record DemoRequest : CliRequest;
