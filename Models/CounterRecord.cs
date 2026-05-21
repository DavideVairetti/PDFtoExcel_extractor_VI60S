namespace EstrattoreContatori.Models;

public sealed class CounterRecord
{
    public string Section { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Value { get; init; }
    public decimal? Percentage { get; init; }
    public int PageNumber { get; init; }
}
