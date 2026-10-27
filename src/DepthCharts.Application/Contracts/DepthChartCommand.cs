using System.Text.Json.Serialization;

namespace DepthCharts.Application.Contracts;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(AddPlayerCommand), "add-player")]
[JsonDerivedType(typeof(RemovePlayerCommand), "remove-player")]
public abstract record DepthChartCommand(Guid CommandId, string Sport, string Team, string Position);

public sealed record AddPlayerCommand(Guid CommandId, string Sport, string Team, string Position,
    int PlayerId, string PlayerName, string? PlayerPosition, int? Depth) : DepthChartCommand(CommandId, Sport, Team, Position);

public sealed record RemovePlayerCommand(Guid CommandId, string Sport, string Team, string Position,
    int PlayerId) : DepthChartCommand(CommandId, Sport, Team, Position);
