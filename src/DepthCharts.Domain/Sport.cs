namespace DepthCharts.Domain;

public sealed record Sport
{
    public Sport(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainValidationException("Sport must not be empty.");
        Value = value.Trim().ToUpperInvariant();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
