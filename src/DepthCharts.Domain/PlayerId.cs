namespace DepthCharts.Domain;

public sealed record PlayerId
{
    public PlayerId(int value)
    {
        if (value <= 0) throw new DomainValidationException("PlayerId must be positive.");
        Value = value;
    }

    public int Value { get; }

    public override string ToString() => Value.ToString();
}
