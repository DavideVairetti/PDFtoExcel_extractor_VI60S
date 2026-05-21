namespace EstrattoreContatori.Services;

public static class FileDiscoveryService
{
    public static bool IsPdfFile(string path) =>
        string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);

    public static string GetProcessingRoot(string inputPath)
    {
        if (File.Exists(inputPath))
        {
            return Path.GetDirectoryName(inputPath) ?? AppContext.BaseDirectory;
        }

        return inputPath;
    }

    public static string GetDefaultExcelPath(string pdfPath)
    {
        var directory = Path.GetDirectoryName(pdfPath) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(pdfPath);
        var excelDirectory = Path.Combine(directory, "excel");
        return Path.Combine(excelDirectory, fileName + ".xlsx");
    }

    public static IEnumerable<string> FindPdfFiles(string inputPath, bool recursive, List<string> warnings)
    {
        if (File.Exists(inputPath))
        {
            if (IsPdfFile(inputPath))
            {
                yield return inputPath;
            }

            yield break;
        }

        if (!Directory.Exists(inputPath))
        {
            yield break;
        }

        if (!recursive)
        {
            foreach (var file in EnumeratePdfFilesInDirectory(inputPath, warnings))
            {
                yield return file;
            }

            yield break;
        }

        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(inputPath);

        while (pendingDirectories.Count > 0)
        {
            var directory = pendingDirectories.Pop();

            foreach (var file in EnumeratePdfFilesInDirectory(directory, warnings))
            {
                yield return file;
            }

            foreach (var subDirectory in EnumerateSubDirectories(directory, warnings).Reverse())
            {
                pendingDirectories.Push(subDirectory);
            }
        }
    }

    private static IEnumerable<string> EnumeratePdfFilesInDirectory(string directory, List<string> warnings)
    {
        try
        {
            return Directory
                .EnumerateFiles(directory, "*.pdf", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            warnings.Add($"Impossibile leggere i file nella cartella '{directory}': {ex.Message}");
            return Enumerable.Empty<string>();
        }
    }

    private static IEnumerable<string> EnumerateSubDirectories(string directory, List<string> warnings)
    {
        try
        {
            return Directory
                .EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            warnings.Add($"Impossibile leggere le sottocartelle di '{directory}': {ex.Message}");
            return Enumerable.Empty<string>();
        }
    }
}
