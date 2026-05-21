using System.Text;
using EstrattoreContatori.Models;
using EstrattoreContatori.Services;

namespace EstrattoreContatori;

internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitInvalidInput = 1;
    private const int ExitNoPdfFound = 2;
    private const int ExitSomePdfFailed = 3;
    private const int ExitUnexpectedError = 4;

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            AppOptions options;

            try
            {
                options = AppOptions.Parse(args);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"Errore input: {ex.Message}");
                PrintUsage();
                return ExitInvalidInput;
            }

            if (options.ShowHelp)
            {
                PrintUsage();
                return ExitOk;
            }

            if (options.Ask)
            {
                Console.Write("Inserisci il percorso del file PDF o della cartella: ");
                options.InputPath = Console.ReadLine();
            }

            if (string.IsNullOrWhiteSpace(options.InputPath))
            {
                options.InputPath = FindProjectRoot();
            }

            var inputPath = NormalizeInputPath(options.InputPath);

            if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
            {
                Console.Error.WriteLine($"Errore: il percorso non esiste: {inputPath}");
                return ExitInvalidInput;
            }

            if (File.Exists(inputPath) && !FileDiscoveryService.IsPdfFile(inputPath))
            {
                Console.Error.WriteLine($"Errore: il file indicato non è un PDF: {inputPath}");
                return ExitInvalidInput;
            }

            var processingRoot = FileDiscoveryService.GetProcessingRoot(inputPath);
            Directory.CreateDirectory(processingRoot);

            using var log = new LogService(Path.Combine(processingRoot, "estrazione_contatori.log"));
            log.Info("============================================================");
            log.Info($"Data e ora di avvio: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            log.Info($"Path elaborato: {inputPath}");
            log.Info($"Ricerca ricorsiva: {FormatBool(options.Recursive)}");
            log.Info($"Overwrite: {FormatBool(options.Overwrite)}");

            Console.WriteLine("Estrattore Contatori Report");
            Console.WriteLine($"Percorso: {inputPath}");
            Console.WriteLine($"Ricerca ricorsiva: {FormatBool(options.Recursive)}");
            Console.WriteLine();

            var discoveryWarnings = new List<string>();
            var pdfFiles = FileDiscoveryService.FindPdfFiles(inputPath, options.Recursive, discoveryWarnings).ToList();

            foreach (var warning in discoveryWarnings)
            {
                log.Warning(warning);
                Console.WriteLine($"Warning: {warning}");
            }

            log.Info($"Numero PDF trovati: {pdfFiles.Count}");
            Console.WriteLine($"PDF trovati: {pdfFiles.Count}");
            Console.WriteLine();

            if (pdfFiles.Count == 0)
            {
                log.Warning("Nessun PDF trovato.");
                Console.WriteLine("Nessun PDF trovato.");
                Console.WriteLine($"Log: {log.LogPath}");
                return ExitNoPdfFound;
            }

            var extractor = new PdfCounterExtractor();
            var writer = new ExcelReportWriter();

            var processedOk = 0;
            var failed = 0;

            for (var index = 0; index < pdfFiles.Count; index++)
            {
                var pdfPath = pdfFiles[index];
                Console.WriteLine($"[{index + 1}/{pdfFiles.Count}] Elaborazione: {Path.GetFileName(pdfPath)}");
                log.Info($"[{index + 1}/{pdfFiles.Count}] Elaborazione PDF: {pdfPath}");

                try
                {
                    var result = extractor.Extract(pdfPath);
                    LogWarnings(log, result);

                    var desiredOutputPath = FileDiscoveryService.GetDefaultExcelPath(pdfPath);
                    var actualOutputPath = writer.Write(result, desiredOutputPath, options.Overwrite);

                    processedOk++;

                    Console.WriteLine($"      Contatori estratti: {result.Counters.Count}");
                    Console.WriteLine($"      Excel creato: {Path.GetFileName(actualOutputPath)}");
                    Console.WriteLine();

                    log.Info($"Excel creato: {actualOutputPath}");
                    log.Info($"Numero contatori estratti: {result.Counters.Count}");
                }
                catch (Exception ex)
                {
                    failed++;

                    Console.WriteLine($"      Errore: {ex.Message}");
                    Console.WriteLine();

                    log.Error($"Errore durante l'elaborazione di {pdfPath}", ex);
                }
            }

            Console.WriteLine("Elaborazione completata.");
            Console.WriteLine($"PDF trovati: {pdfFiles.Count}");
            Console.WriteLine($"Elaborati correttamente: {processedOk}");
            Console.WriteLine($"Falliti: {failed}");
            Console.WriteLine($"Log: {log.LogPath}");

            log.Info("Riepilogo finale:");
            log.Info($"PDF trovati: {pdfFiles.Count}");
            log.Info($"Elaborati correttamente: {processedOk}");
            log.Info($"Falliti: {failed}");
            log.Info($"Data e ora di fine: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            return failed > 0 ? ExitSomePdfFailed : ExitOk;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Errore imprevisto: {ex.Message}");
            Console.Error.WriteLine(ex);
            return ExitUnexpectedError;
        }
    }

    private static void LogWarnings(LogService log, ExtractionResult result)
    {
        foreach (var warning in result.Warnings)
        {
            log.Warning($"{Path.GetFileName(result.PdfPath)}: {warning}");
        }
    }

    private static string FindProjectRoot()
    {
        var current = AppContext.BaseDirectory;
        
        // Risali la gerarchia delle cartelle finché non trovi un .csproj
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.GetFiles(current, "*.csproj").Any())
            {
                return current;
            }

            var parent = Path.GetDirectoryName(current);
            if (parent == current) // Raggiunto il root del filesystem
            {
                break;
            }

            current = parent;
        }

        // Fallback se non trova .csproj
        return AppContext.BaseDirectory;
    }

    private static string NormalizeInputPath(string input)
    {
        var trimmed = input.Trim().Trim('"');
        var expanded = Environment.ExpandEnvironmentVariables(trimmed);
        return Path.GetFullPath(expanded);
    }

    private static string FormatBool(bool value) => value ? "sì" : "no";

    private static void PrintUsage()
    {
        Console.WriteLine("Uso:");
        Console.WriteLine("  EstrattoreContatori.exe");
        Console.WriteLine("  EstrattoreContatori.exe \"C:\\Report\"");
        Console.WriteLine("  EstrattoreContatori.exe \"C:\\Report\\Buoni KIT_Report.pdf\"");
        Console.WriteLine("  EstrattoreContatori.exe --ask");
        Console.WriteLine("  EstrattoreContatori.exe \"C:\\Report\" --no-recursive");
        Console.WriteLine("  EstrattoreContatori.exe \"C:\\Report\" --overwrite");
        Console.WriteLine();
        Console.WriteLine("Opzioni:");
        Console.WriteLine("  --ask             Chiede il percorso da terminale.");
        Console.WriteLine("  --no-recursive    Cerca PDF solo nella cartella indicata.");
        Console.WriteLine("  --overwrite       Sovrascrive il file Excel se possibile.");
        Console.WriteLine("  --help            Mostra questa guida.");
    }

    private sealed class AppOptions
    {
        public string? InputPath { get; set; }
        public bool Ask { get; private set; }
        public bool Recursive { get; private set; } = true;
        public bool Overwrite { get; private set; }
        public bool ShowHelp { get; private set; }

        public static AppOptions Parse(string[] args)
        {
            var options = new AppOptions();

            foreach (var rawArg in args)
            {
                if (string.IsNullOrWhiteSpace(rawArg))
                {
                    continue;
                }

                var arg = rawArg.Trim();
                var normalized = arg.ToLowerInvariant();

                switch (normalized)
                {
                    case "--ask":
                        options.Ask = true;
                        continue;
                    case "--no-recursive":
                        options.Recursive = false;
                        continue;
                    case "--overwrite":
                        options.Overwrite = true;
                        continue;
                    case "--help":
                    case "-h":
                    case "/?":
                        options.ShowHelp = true;
                        continue;
                }

                if (arg.StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"opzione non riconosciuta: {arg}");
                }

                if (options.InputPath is not null)
                {
                    throw new ArgumentException("è possibile indicare un solo percorso di input.");
                }

                options.InputPath = rawArg;
            }

            return options;
        }
    }
}
