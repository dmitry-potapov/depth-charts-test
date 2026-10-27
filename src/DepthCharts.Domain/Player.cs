namespace DepthCharts.Domain;

public sealed record Player
{
    public Player(PlayerId id, string name, Position? naturalPosition)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainValidationException("Player name must not be empty.");
        Id = id ?? throw new DomainValidationException("Player id is required.");
        Name = name.Trim();
        NaturalPosition = naturalPosition;
    }

    public PlayerId Id { get; }
    public string Name { get; }
    public Position? NaturalPosition { get; }
}
