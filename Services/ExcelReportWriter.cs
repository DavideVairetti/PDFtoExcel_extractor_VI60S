using ClosedXML.Excel;
using EstrattoreContatori.Models;

namespace EstrattoreContatori.Services;

public sealed class ExcelReportWriter
{
    private const int MaxOutputAttempts = 1000;

    private static readonly string[] MetadataOutputOrder =
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
        "Campioni per ciclo",
        "Nome file PDF",
        "Percorso file PDF",
        "Data/ora elaborazione"
    ];

    public string Write(ExtractionResult result, string outputPath, bool overwrite)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Il percorso di output Excel non è valido.", nameof(outputPath));
        }

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var workbook = BuildWorkbook(result);
        Exception? lastException = null;

        foreach (var candidatePath in EnumerateCandidatePaths(outputPath, overwrite).Take(MaxOutputAttempts))
        {
            try
            {
                if (overwrite && File.Exists(candidatePath))
                {
                    File.Delete(candidatePath);
                }

                workbook.SaveAs(candidatePath);
                return candidatePath;
            }
            catch (Exception ex) when (IsFileAccessException(ex))
            {
                lastException = ex;
            }
        }

        throw new IOException(
            $"Impossibile creare il file Excel per '{result.PdfPath}'. Ultimo errore: {lastException?.Message ?? "nessun dettaglio"}",
            lastException);
    }

    private static XLWorkbook BuildWorkbook(ExtractionResult result)
    {
        var workbook = new XLWorkbook();
        workbook.Properties.Author = "EstrattoreContatori";
        workbook.Properties.Title = "Estrazione contatori report KIT";
        workbook.Properties.Subject = Path.GetFileName(result.PdfPath);
        workbook.Properties.Created = DateTime.Now;

        CreateCountersSheet(workbook, result.Counters);
        CreateMetadataSheet(workbook, result.Metadata);

        return workbook;
    }

    private static void CreateCountersSheet(XLWorkbook workbook, IReadOnlyList<CounterRecord> counters)
    {
        var worksheet = workbook.Worksheets.Add("Contatori");

        var headers = new[]
        {
            "Nome contatore",
            "Valore",
            "Percentuale"
        };

        for (var column = 0; column < headers.Length; column++)
        {
            worksheet.Cell(1, column + 1).Value = headers[column];
        }

        var row = 2;
        foreach (var counter in counters)
        {
            if (row > 69)
            {
                break;
            }
            
            worksheet.Cell(row, 1).Value = counter.Name;
            worksheet.Cell(row, 2).Value = counter.Value;

            if (counter.Percentage.HasValue)
            {
                worksheet.Cell(row, 3).Value = (double)counter.Percentage.Value;
            }

            row++;
        }

        var lastRow = Math.Max(row - 1, 1);
        var usedRange = worksheet.Range(1, 1, lastRow, headers.Length);
        worksheet.Range(1, 1, 1, headers.Length).Style.Font.Bold = true;
        usedRange.SetAutoFilter();

        worksheet.SheetView.FreezeRows(1);
        worksheet.Column(2).Style.NumberFormat.Format = "0";
        worksheet.Column(3).Style.NumberFormat.Format = "0.00";
        worksheet.Columns().AdjustToContents();
    }

    private static void CreateMetadataSheet(XLWorkbook workbook, ReportMetadata metadata)
    {
        var worksheet = workbook.Worksheets.Add("Metadati");
        worksheet.Cell(1, 1).Value = "Campo";
        worksheet.Cell(1, 2).Value = "Valore";
        worksheet.Range(1, 1, 1, 2).Style.Font.Bold = true;

        var row = 2;
        foreach (var key in MetadataOutputOrder)
        {
            if (metadata.Fields.TryGetValue(key, out var value))
            {
                worksheet.Cell(row, 1).Value = key;
                worksheet.Cell(row, 2).Value = value;
                row++;
            }
        }

        foreach (var pair in metadata.Fields.Where(pair => !MetadataOutputOrder.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)))
        {
            worksheet.Cell(row, 1).Value = pair.Key;
            worksheet.Cell(row, 2).Value = pair.Value;
            row++;
        }

        worksheet.SheetView.FreezeRows(1);
        worksheet.Columns().AdjustToContents();
    }

    private static IEnumerable<string> EnumerateCandidatePaths(string outputPath, bool overwrite)
    {
        if (overwrite || !File.Exists(outputPath))
        {
            yield return outputPath;
        }

        for (var index = 1; index <= MaxOutputAttempts; index++)
        {
            var candidate = BuildAlternativePath(outputPath, index);

            if (overwrite || !File.Exists(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static string BuildAlternativePath(string outputPath, int index)
    {
        var directory = Path.GetDirectoryName(outputPath) ?? string.Empty;
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(outputPath);
        var extension = Path.GetExtension(outputPath);
        return Path.Combine(directory, $"{fileNameWithoutExtension}_estratto_{index}{extension}");
    }

    private static bool IsFileAccessException(Exception ex) => ex is IOException or UnauthorizedAccessException;
}
