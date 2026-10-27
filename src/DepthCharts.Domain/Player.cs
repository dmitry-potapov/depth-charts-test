namespace DepthCharts.Domain;

public sealed record Player
{
    public Player(int id, string name, Position? naturalPosition)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainValidationException("Player name must not be empty.");
        if (id <= 0) throw new DomainValidationException("Player id must be positive.");
        Id = id;
        Name = name.Trim();
        NaturalPosition = naturalPosition;
    }

    public int Id { get; }
    public string Name { get; }
    public Position? NaturalPosition { get; }
}
