using System.Text.RegularExpressions;

namespace DepthCharts.Domain;

public sealed partial record Team
{
    public Team(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainValidationException("Team must not be empty.");
        var normalised = value.Trim().ToLowerInvariant();
        if (!ValidPattern().IsMatch(normalised))
            throw new DomainValidationException("Team may only contain letters, digits and '-'.");
        Value = normalised;
    }

    public string Value { get; }

    public override string ToString() => Value;

    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex ValidPattern();
}
