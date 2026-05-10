using System.Text;
using System.Diagnostics;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace MyBrainAI.Api.Services;

public sealed class DocumentProcessor
{
    private static readonly Regex WordRegex = new("[a-zA-ZÀ-ÿ0-9]{3,}", RegexOptions.Compiled);
    private readonly string contentRootPath;

    public DocumentProcessor(IWebHostEnvironment environment)
    {
        contentRootPath = environment.ContentRootPath;
    }

    public async Task<ProcessedDocument> ProcessAsync(IFormFile file)
    {
        var tempFile = Path.GetTempFileName();

        await using (var stream = File.Create(tempFile))
        {
            await file.CopyToAsync(stream);
        }

        try
        {
            var pages = await ExtractTextByPageAsync(tempFile, file.FileName);
            var chunks = CreateChunks(pages, 900);

            return new ProcessedDocument
            {
                FileName = file.FileName,
                SizeBytes = file.Length,
                TotalPages = pages.Count,
                TotalTokens = chunks.Sum(c => c.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length),
                Chunks = chunks
            };
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private async Task<List<(int Page, string Text)>> ExtractTextByPageAsync(string path, string fileName)
    {
        if (fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            using var document = PdfDocument.Open(path);
            return document.GetPages()
                .Select(p => (p.Number, Clean(p.Text)))
                .Where(p => !string.IsNullOrWhiteSpace(p.Item2))
                .ToList();
        }

        if (IsImage(fileName))
        {
            var text = await ExtractImageTextAsync(path);
            return string.IsNullOrWhiteSpace(text)
                ? []
                : [(1, Clean(text))];
        }

        if (IsText(fileName))
        {
            var text = await File.ReadAllTextAsync(path);
            return [(1, Clean(text))];
        }

        throw new InvalidOperationException("Unsupported file type. Upload PDF, TXT, Markdown, CSV, or an image supported by local OCR.");
    }

    private static List<DocumentChunk> CreateChunks(List<(int Page, string Text)> pages, int maxChars)
    {
        var result = new List<DocumentChunk>();
        var index = 0;

        foreach (var page in pages)
        {
            var text = page.Text;
            for (var i = 0; i < text.Length; i += maxChars)
            {
                var length = Math.Min(maxChars, text.Length - i);
                var chunkText = text.Substring(i, length);

                result.Add(new DocumentChunk
                {
                    Index = index++,
                    Page = page.Page,
                    Text = chunkText,
                    Keywords = ExtractKeywords(chunkText)
                });
            }
        }

        return result;
    }

    private static HashSet<string> ExtractKeywords(string text)
    {
        return WordRegex.Matches(text.ToLowerInvariant())
            .Select(x => x.Value)
            .Where(x => x.Length > 3)
            .ToHashSet();
    }

    private static string Clean(string value)
    {
        return Regex.Replace(value, "\\s+", " ").Trim();
    }

    private static bool IsText(string fileName)
    {
        return EndsWithAny(fileName, ".txt", ".md", ".csv", ".json", ".log");
    }

    private static bool IsImage(string fileName)
    {
        return EndsWithAny(fileName, ".png", ".jpg", ".jpeg", ".webp", ".tif", ".tiff", ".bmp");
    }

    private static bool EndsWithAny(string value, params string[] extensions)
    {
        return extensions.Any(extension => value.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string> ExtractImageTextAsync(string path)
    {
        var macVisionText = await ExtractImageTextWithMacVisionAsync(path);
        if (!string.IsNullOrWhiteSpace(macVisionText))
            return macVisionText;

        var tesseractPath = FindExecutable("tesseract");
        if (tesseractPath is null)
        {
            throw new InvalidOperationException("Local OCR is not available. On macOS, Swift/Vision is required; otherwise install Tesseract.");
        }

        var language = GetTesseractLanguageArgument();
        var startInfo = new ProcessStartInfo(tesseractPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        var tessdataDirectory = FindTessdataDirectory();
        if (tessdataDirectory is not null)
            startInfo.Environment["TESSDATA_PREFIX"] = tessdataDirectory;

        startInfo.ArgumentList.Add(path);
        startInfo.ArgumentList.Add("stdout");

        if (!string.IsNullOrWhiteSpace(language))
        {
            startInfo.ArgumentList.Add("-l");
            startInfo.ArgumentList.Add(language);
        }

        var result = await RunProcessAsync(startInfo, TimeSpan.FromSeconds(10));
        if (!result.Completed)
            throw new InvalidOperationException("Local OCR timed out while reading the image.");

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Local OCR failed: {result.Error.Trim()}");

        return result.Output;
    }

    private async Task<string?> ExtractImageTextWithMacVisionAsync(string path)
    {
        if (!OperatingSystem.IsMacOS())
            return null;

        var swiftPath = FindExecutable("swift");
        var scriptPath = Path.Combine(contentRootPath, "Tools", "macos-vision-ocr.swift");
        if (swiftPath is null || !File.Exists(scriptPath))
            return null;

        var startInfo = new ProcessStartInfo(swiftPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add(path);

        var result = await RunProcessAsync(startInfo, TimeSpan.FromSeconds(30));
        return result.Completed && result.ExitCode == 0 ? result.Output : null;
    }

    private static string? GetTesseractLanguageArgument()
    {
        var tessdataDirectory = FindTessdataDirectory();
        if (tessdataDirectory is null)
            return "eng";

        var languages = Directory.GetFiles(tessdataDirectory, "*.traineddata")
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (languages.Contains("por") && languages.Contains("eng"))
            return "por+eng";

        if (languages.Contains("por"))
            return "por";

        return languages.Contains("eng") ? "eng" : null;
    }

    private static string? FindTessdataDirectory()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("TESSDATA_PREFIX"),
            "/opt/homebrew/share/tessdata",
            "/usr/local/share/tessdata",
            "/usr/share/tessdata"
        };

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .FirstOrDefault(path => Directory.Exists(path!));
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        var waitTask = process.WaitForExitAsync();
        var timeoutTask = Task.Delay(timeout);
        return await Task.WhenAny(waitTask, timeoutTask) == waitTask;
    }

    private static async Task<ProcessResult> RunProcessAsync(ProcessStartInfo startInfo, TimeSpan timeout)
    {
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start local process.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        var completed = await WaitForExitAsync(process, timeout);

        if (!completed)
        {
            TryKill(process);
            return new ProcessResult(false, -1, "", "");
        }

        return new ProcessResult(
            true,
            process.ExitCode,
            await outputTask,
            await errorTask);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // The OCR process is already gone or cannot be killed; the caller will return an error.
        }
    }

    private static string? FindExecutable(string command)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => Path.Combine(path, command))
            .Concat([
                $"/opt/homebrew/bin/{command}",
                $"/usr/local/bin/{command}",
                $"/usr/bin/{command}"
            ]);

        return paths.FirstOrDefault(File.Exists);
    }

    private sealed record ProcessResult(bool Completed, int ExitCode, string Output, string Error);
}
