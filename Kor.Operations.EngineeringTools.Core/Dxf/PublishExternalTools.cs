using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.Dxf;

public sealed record PublishToolPaths(string RendererScript, string PdfInfoExe);

public static class PublishExternalTools
{
    public static PublishToolPaths Locate(string repoRoot, string? rendererScript = null, string? pdfInfoExe = null)
    {
        string renderer = rendererScript ?? Path.Combine(repoRoot, "tools", "Format-BdWebPdf.ps1");
        if (!File.Exists(renderer))
            throw new FileNotFoundException($"PDF renderer not found '{renderer}'.", renderer);

        string? pdfinfo = pdfInfoExe;
        if (string.IsNullOrWhiteSpace(pdfinfo))
            pdfinfo = FindOnPath("pdfinfo.exe") ?? FindUnderWinget("pdfinfo.exe");
        if (string.IsNullOrWhiteSpace(pdfinfo) || !File.Exists(pdfinfo))
            throw new FileNotFoundException("pdfinfo.exe was not found. Install Poppler or put pdfinfo.exe on PATH.");

        return new PublishToolPaths(renderer, pdfinfo);
    }

    public static void RenderPdf(PublishToolPaths tools, string htmlPath, string pdfPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(pdfPath) ?? ".");
        var result = Run("powershell", $"-NoProfile -ExecutionPolicy Bypass -File \"{tools.RendererScript}\" -Html \"{htmlPath}\" -Pdf \"{pdfPath}\"");
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"PDF render failed: {result.Error}{result.Output}");
        if (!File.Exists(pdfPath))
            throw new FileNotFoundException($"The per-job summary did not render: {pdfPath}.", pdfPath);
    }

    public static int PageCount(PublishToolPaths tools, string pdfPath)
    {
        var result = Run(tools.PdfInfoExe, $"\"{pdfPath}\"");
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"pdfinfo failed: {result.Error}{result.Output}");

        var match = Regex.Match(result.Output, @"^Pages:\s*(\d+)", RegexOptions.Multiline);
        if (!match.Success)
            throw new InvalidOperationException($"pdfinfo did not report a page count for '{pdfPath}'.");
        return int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static (int ExitCode, string Output, string Error) Run(string fileName, string arguments)
    {
        var start = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error);
    }

    private static string? FindOnPath(string name)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path)) return null;
        foreach (string dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            string candidate = Path.Combine(dir.Trim(), name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string? FindUnderWinget(string name)
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WinGet", "Packages");
        if (!Directory.Exists(root)) return null;
        return Directory.EnumerateFiles(root, name, SearchOption.AllDirectories).FirstOrDefault();
    }
}
