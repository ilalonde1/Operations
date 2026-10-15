using System.Diagnostics;

namespace Kor.Opportunities.Knowledge.Readers;

/// <summary>The process boundary, replaceable in tests without invoking Calibre.</summary>
public interface IEpubConverter
{
    Task ConvertAsync(string originalPath, string epubPath, CancellationToken ct);
}

/// <summary>Read a temporary ePub; the caller retains the original filename and hash.</summary>
public sealed class CalibreConverter(string? executable = null, IEpubConverter? converter = null) : IReader
{
    public const string DefaultExecutable = @"C:\Program Files\Calibre2\ebook-convert.exe";

    public async Task<IReadOnlyList<Chunk>> ReadAsync(string path, CancellationToken ct = default)
    {
        var temporary = Path.Combine(Path.GetTempPath(), $"kor-knowledge-{Guid.NewGuid():N}.epub");
        try
        {
            await (converter ?? new CalibreProcess(executable ?? DefaultExecutable)).ConvertAsync(path, temporary, ct);
            return await new EpubReader().ReadAsync(temporary, ct);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private sealed class CalibreProcess(string executablePath) : IEpubConverter
    {
        public async Task ConvertAsync(string originalPath, string epubPath, CancellationToken ct)
        {
            if (!File.Exists(executablePath))
                throw new FileNotFoundException("Calibre was not found. Pass --calibre with the path to ebook-convert.exe.");
            var start = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            start.ArgumentList.Add(Path.GetFullPath(originalPath));
            start.ArgumentList.Add(epubPath);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Calibre could not start.");
            // Drain both pipes while waiting, so a verbose conversion cannot deadlock.
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                await Task.WhenAll(output, error);
                throw;
            }
            await Task.WhenAll(output, error);
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Calibre conversion failed with exit code {process.ExitCode}.");
        }
    }
}
