using System.ComponentModel.DataAnnotations;

namespace DepthCharts.Infrastructure.Persistence;

// Null or empty AllowedPositions means the sport accepts any position.
public sealed class SportDefinition
{
    [Key]
    public string Sport { get; set; } = "";

    public List<string>? AllowedPositions { get; set; }
}
