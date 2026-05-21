namespace EstrattoreContatori.Models;

public sealed class ReportMetadata
{
    public Dictionary<string, string> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);
}
