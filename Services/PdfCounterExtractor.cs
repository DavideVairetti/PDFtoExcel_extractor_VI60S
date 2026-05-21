using System.Globalization;
using System.Text.RegularExpressions;
using EstrattoreContatori.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace EstrattoreContatori.Services;

public sealed class PdfCounterExtractor
{
    private const double LineTolerance = 3.0d;

    private static readonly string[] SectionNames =
    [
        "Contatore di partite",
        "Contatore globale",
        "Sensori espulsione",
        "Contatore di categorie",
        "Contatore di difetti",
        "Difetti singoli",
        "Difetti particelle",
        "Nome delle categorie"
    ];

    private static readonly string[] MetadataKeys =
    [
        "Tipo di macchina",
        "Autore del lotto",
        "Autore del rapporto",
        "N. macchina",
        "Descrizione del lotto",
        "Fine del lotto",
        "Avvio del lotto",
        "Nome del lotto",
        "Modo della macchina",
        "Ricetta",
        "Campioni per ciclo"
    ];

    private static readonly HashSet<string> IgnoredExactLines = new(StringComparer.OrdinalIgnoreCase)
    {
        "Rapporto sul lotto",
        "Campionamento",
        "Contatori di sezioni",
        "1. Contatori di sezioni",
        "Completatore",
        "Contatore di oggetti",
        "Totale contatore",
        "Descrizione della",
        "Avvio",
        "Fine",
        "Autore"
    };

    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CounterWithPercentageRegex = new(
        @"^(.+?)\s+(\d+)\s+(\d+(?:[\.,]\d+)?)\s*%$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CounterWithoutPercentageRegex = new(
        @"^(.+?)\s+(\d+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PageFooterRegex = new(
        @"^\d{2}\.\d{2}\.\d{2}\s+\d{2}:\d{2}:\d{2}\s+Pagina\s+\d+\s+di\s+\d+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex PageOnlyRegex = new(
        @"^Pagina\s+\d+\s+di\s+\d+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex DateTimeOnlyRegex = new(
        @"^\d{2}\.\d{2}\.\d{2}\s+\d{2}:\d{2}:\d{2}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MachineLineRegex = new(
        @"^VI-S\s+\d+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex MachineNumberLineRegex = new(
        @"^\d{6,}(?:-\d{2}){2,}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NumberedSectionRegex = new(
        @"^\d+\.\s+Contatori\s+di\s+sezioni?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public ExtractionResult Extract(string pdfPath)
    {
        if (!File.Exists(pdfPath))
        {
            throw new FileNotFoundException("PDF non trovato.", pdfPath);
        }

        var metadata = new ReportMetadata();
        var counters = new List<CounterRecord>();
        var warnings = new List<string>();

        var hasAnyText = false;
        string? currentSection = null;

        using var document = PdfDocument.Open(pdfPath);

        var pageNumber = 0;
        foreach (var page in document.GetPages())
        {
            pageNumber++;
            var lines = ReadPageLines(page);

            if (lines.Count > 0)
            {
                hasAnyText = true;
            }

            CaptureMetadata(metadata, lines);

            foreach (var rawLine in lines)
            {
                var line = NormalizeLine(rawLine);
                if (line.Length == 0)
                {
                    continue;
                }

                if (NumberedSectionRegex.IsMatch(line))
                {
                    currentSection = null;
                    continue;
                }

                var section = TryGetSection(line);
                if (section is not null)
                {
                    currentSection = section;
                    continue;
                }

                if (ShouldIgnoreLine(line))
                {
                    continue;
                }

                if (currentSection is null)
                {
                    continue;
                }

                if (TryParseCounter(line, currentSection, pageNumber, out var counter))
                {
                    counters.Add(counter);
                }
            }
        }

        if (!hasAnyText)
        {
            warnings.Add("PDF senza testo estraibile.");
        }

        if (counters.Count == 0)
        {
            warnings.Add("Nessun contatore estratto.");
        }

        AddDefaultMetadata(metadata, pdfPath);

        return new ExtractionResult
        {
            PdfPath = pdfPath,
            Metadata = metadata,
            Counters = counters,
            Warnings = warnings
        };
    }

    private static IReadOnlyList<string> ReadPageLines(Page page)
    {
        var words = page.GetWords(NearestNeighbourWordExtractor.Instance).ToList();

        if (words.Count == 0)
        {
            var text = ContentOrderTextExtractor.GetText(page);
            return SplitTextLines(text);
        }

        var orderedWords = words
            .OrderByDescending(GetTop)
            .ThenBy(GetLeft)
            .ToList();

        var buckets = new List<LineBucket>();

        foreach (var word in orderedWords)
        {
            var y = GetTop(word);
            var bucket = buckets.FirstOrDefault(existing => Math.Abs(existing.Y - y) <= LineTolerance);

            if (bucket is null)
            {
                bucket = new LineBucket(y);
                buckets.Add(bucket);
            }

            bucket.Words.Add(word);
        }

        return buckets
            .OrderByDescending(bucket => bucket.Y)
            .Select(bucket => JoinLineWords(bucket.Words))
            .Select(NormalizeLine)
            .Where(line => line.Length > 0)
            .ToList();
    }

    private static IReadOnlyList<string> SplitTextLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        return text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeLine)
            .Where(line => line.Length > 0)
            .ToList();
    }

    private static string JoinLineWords(IEnumerable<Word> words) =>
        string.Join(" ", words.OrderBy(GetLeft).Select(word => word.Text));

    private static double GetTop(Word word) => Convert.ToDouble(word.BoundingBox.Top, CultureInfo.InvariantCulture);

    private static double GetLeft(Word word) => Convert.ToDouble(word.BoundingBox.Left, CultureInfo.InvariantCulture);

    private static string? TryGetSection(string line)
    {
        var candidate = NormalizeLine(line).TrimEnd(':').Trim();

        if (NumberedSectionRegex.IsMatch(candidate))
        {
            return null;
        }

        foreach (var section in SectionNames)
        {
            if (string.Equals(candidate, section, StringComparison.OrdinalIgnoreCase))
            {
                return section;
            }
        }

        return null;
    }

    private static bool TryParseCounter(string line, string currentSection, int pageNumber, out CounterRecord counter)
    {
        counter = new CounterRecord();

        var withPercentageMatch = CounterWithPercentageRegex.Match(line);
        if (withPercentageMatch.Success)
        {
            var name = NormalizeCounterName(withPercentageMatch.Groups[1].Value);
            var valueText = withPercentageMatch.Groups[2].Value;
            var percentageText = withPercentageMatch.Groups[3].Value.Replace(',', '.');

            if (name.Length == 0 || !int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return false;
            }

            if (!decimal.TryParse(percentageText, NumberStyles.Number, CultureInfo.InvariantCulture, out var percentage))
            {
                return false;
            }

            counter = new CounterRecord
            {
                Section = currentSection,
                Name = name,
                Value = value,
                Percentage = percentage,
                PageNumber = pageNumber
            };

            return true;
        }

        var withoutPercentageMatch = CounterWithoutPercentageRegex.Match(line);
        if (withoutPercentageMatch.Success)
        {
            var name = NormalizeCounterName(withoutPercentageMatch.Groups[1].Value);
            var valueText = withoutPercentageMatch.Groups[2].Value;

            if (name.Length == 0 || !int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return false;
            }

            counter = new CounterRecord
            {
                Section = currentSection,
                Name = name,
                Value = value,
                Percentage = null,
                PageNumber = pageNumber
            };

            return true;
        }

        return false;
    }

    private static string NormalizeCounterName(string value) => NormalizeLine(value);

    private static string NormalizeLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return string.Empty;
        }

        var normalized = line
            .Replace('\t', ' ')
            .Replace('\u00A0', ' ')
            .Replace('\u2007', ' ')
            .Replace('\u202F', ' ');

        normalized = WhitespaceRegex.Replace(normalized, " ").Trim();
        return normalized;
    }

    private static bool ShouldIgnoreLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return true;
        }

        var normalized = NormalizeLine(line);
        var withoutTrailingColon = normalized.TrimEnd(':').Trim();

        if (IgnoredExactLines.Contains(withoutTrailingColon))
        {
            return true;
        }

        if (PageFooterRegex.IsMatch(normalized) || PageOnlyRegex.IsMatch(normalized) || DateTimeOnlyRegex.IsMatch(normalized))
        {
            return true;
        }

        if (MachineLineRegex.IsMatch(normalized) || MachineNumberLineRegex.IsMatch(normalized))
        {
            return true;
        }

        if (MetadataKeys.Any(key => StartsWithMetadataKey(normalized, key)))
        {
            return true;
        }

        if (normalized.StartsWith("Avvio:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Fine:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Autore:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Completatore:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Le righe utili non contengono due punti; i metadati sì.
        if (normalized.Contains(':'))
        {
            return true;
        }

        return false;
    }

    private static bool StartsWithMetadataKey(string line, string key) =>
        line.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase);

    private static void CaptureMetadata(ReportMetadata metadata, IReadOnlyList<string> rawLines)
    {
        var lines = rawLines
            .Select(NormalizeLine)
            .Where(line => line.Length > 0)
            .ToList();

        foreach (var line in lines)
        {
            foreach (var key in MetadataKeys)
            {
                if (TryExtractInlineMetadata(line, key, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    SetIfMissing(metadata, key, value);
                }
            }
        }

        if (!metadata.Fields.ContainsKey("Tipo di macchina"))
        {
            var machineType = lines.FirstOrDefault(line => MachineLineRegex.IsMatch(line));
            if (!string.IsNullOrWhiteSpace(machineType))
            {
                SetIfMissing(metadata, "Tipo di macchina", machineType);
            }
        }

        if (!metadata.Fields.ContainsKey("N. macchina"))
        {
            var machineNumber = lines.FirstOrDefault(line => MachineNumberLineRegex.IsMatch(line));
            if (!string.IsNullOrWhiteSpace(machineNumber))
            {
                SetIfMissing(metadata, "N. macchina", machineNumber);
            }
        }
    }

    private static bool TryExtractInlineMetadata(string line, string key, out string value)
    {
        value = string.Empty;

        if (!line.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        value = line[(key.Length + 1)..].Trim();
        return true;
    }

    private static void AddDefaultMetadata(ReportMetadata metadata, string pdfPath)
    {
        metadata.Fields["Nome file PDF"] = Path.GetFileName(pdfPath);
        metadata.Fields["Percorso file PDF"] = pdfPath;
        metadata.Fields["Data/ora elaborazione"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static void SetIfMissing(ReportMetadata metadata, string key, string value)
    {
        if (!metadata.Fields.ContainsKey(key))
        {
            metadata.Fields[key] = value;
        }
    }

    private sealed class LineBucket(double y)
    {
        public double Y { get; } = y;
        public List<Word> Words { get; } = [];
    }
}
