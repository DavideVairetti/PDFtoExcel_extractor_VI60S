namespace EstrattoreContatori.Models;

public sealed class ExtractionResult
{
    public string PdfPath { get; init; } = string.Empty;
    public ReportMetadata Metadata { get; init; } = new();
    public List<CounterRecord> Counters { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
}
