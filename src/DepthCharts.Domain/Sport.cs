using System.Text.RegularExpressions;

namespace DepthCharts.Domain;

public sealed partial record Sport
{
    public Sport(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainValidationException("Sport must not be empty.");
        var normalised = value.Trim().ToUpperInvariant();
        if (!ValidPattern().IsMatch(normalised))
            throw new DomainValidationException("Sport may only contain letters and digits.");
        Value = normalised;
    }

    public string Value { get; }

    public override string ToString() => Value;

    [GeneratedRegex("^[A-Z0-9]+$")]
    private static partial Regex ValidPattern();
}
