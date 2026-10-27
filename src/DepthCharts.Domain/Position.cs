namespace DepthCharts.Domain;

public sealed record Position
{
    public Position(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainValidationException("Position must not be empty.");
        Value = value.Trim().ToUpperInvariant();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
