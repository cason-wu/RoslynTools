namespace RoslynLib.Infrastructure;

public sealed record RoslynCliQueryOptions
{
    public string? Symbol { get; init; }
    public string? Filter { get; init; }
    public string Visibility { get; init; } = "all";
    public IReadOnlyList<string> Excludes { get; init; } = [];
    public string? File { get; init; }
    public int? Line { get; init; }
    public int? Column { get; init; }
    public string? Project { get; init; }
    public int? MaxResults { get; init; }
}
