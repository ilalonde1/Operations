#nullable enable
using System.Diagnostics;
using System.Text.Json;
using Kor.EmailSearch.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kor.Operations.Tests;

public sealed class EmailMetadataExtractorTests
{
    private readonly BasicEmailMetadataExtractor _extractor = new(NullLogger<BasicEmailMetadataExtractor>.Instance);

    [Fact]
    public async Task ValidMsgFile_ReturnsExpectedCoreMetadata()
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, "TestData", "sample.msg");
        var result = await ExtractMsgOutOfProcessAsync(filePath);

        Assert.Equal("MSG", result.Format);
        Assert.True(
            !string.IsNullOrWhiteSpace(result.Subject),
            $"Expected a subject but got '{result.Subject ?? "<null>"}' for '{filePath}'.");
        Assert.True(
            !string.IsNullOrWhiteSpace(result.FromEmail),
            $"Expected a sender email but got '{result.FromEmail ?? "<null>"}' for '{filePath}'.");
        Assert.NotNull(result.SentOnUtc);
    }

    [Fact]
    public async Task CorruptMsgFile_ReturnsPartialMetadataAndDoesNotThrow()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.msg");
        await File.WriteAllBytesAsync(filePath, []);

        try
        {
            var result = await _extractor.ExtractAsync("24001", filePath);

            Assert.Equal(Path.GetFileName(filePath), result.FileName);
            Assert.Equal("MSG", result.Format);
            Assert.Null(result.Subject);
            Assert.Null(result.FromEmail);
            Assert.Null(result.BodyText);
        }
        finally
        {
            try
            {
                File.Delete(filePath);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task ValidEmlFile_ReturnsExpectedSubject()
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, "TestData", "sample.eml");

        var result = await _extractor.ExtractAsync("24001", filePath);

        Assert.Equal("EML", result.Format);
        Assert.False(string.IsNullOrWhiteSpace(result.Subject));
    }

    private static async Task<EmailMetadata> ExtractMsgOutOfProcessAsync(string filePath)
    {
        static string Q(string path) => path.Replace("'", "''");

        var baseDir = AppContext.BaseDirectory;
        var scriptPath = Path.Combine(Path.GetTempPath(), $"msg_extract_{Guid.NewGuid():N}.ps1");
        try
        {
            // Write to a temp .ps1 file so we can use -File instead of -Command,
            // avoiding all backtick/quote escaping issues with generic type literals.
            await File.WriteAllTextAsync(scriptPath, $@"
Add-Type -Path '{Q(Path.Combine(baseDir, "MsgReader.dll"))}'
Add-Type -Path '{Q(Path.Combine(baseDir, "Kor.EmailSearch.Core.dll"))}'
Add-Type -Path '{Q(Path.Combine(baseDir, "Microsoft.Extensions.Logging.Abstractions.dll"))}'
$loggingAsm = [System.AppDomain]::CurrentDomain.GetAssemblies() | Where-Object {{ $_.GetName().Name -eq 'Microsoft.Extensions.Logging.Abstractions' }} | Select-Object -First 1
$openType = $loggingAsm.GetType('Microsoft.Extensions.Logging.Abstractions.NullLogger`1')
$closedType = $openType.MakeGenericType([Kor.EmailSearch.Core.BasicEmailMetadataExtractor])
$logger = $closedType.GetProperty('Instance').GetValue($null)
$extractor = [Kor.EmailSearch.Core.BasicEmailMetadataExtractor]::new($logger)
$result = $extractor.ExtractAsync('24001', '{Q(filePath)}').GetAwaiter().GetResult()
[pscustomobject]@{{
    Format = $result.Format
    Subject = $result.Subject
    FromEmail = $result.FromEmail
    SentOnUtc = $result.SentOnUtc
}} | ConvertTo-Json -Compress
");

            var startInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                Arguments = $"-NoProfile -File \"{scriptPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start pwsh for MSG extraction.");

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.True(process.ExitCode == 0, $"pwsh extraction failed (exit {process.ExitCode}):\n{stderr}");

            var json = JsonDocument.Parse(stdout.Trim());
            var root = json.RootElement;

            return new EmailMetadata
            {
                Format = root.GetProperty("Format").GetString() ?? string.Empty,
                Subject = root.GetProperty("Subject").ValueKind == JsonValueKind.Null ? null : root.GetProperty("Subject").GetString(),
                FromEmail = root.GetProperty("FromEmail").ValueKind == JsonValueKind.Null ? null : root.GetProperty("FromEmail").GetString(),
                SentOnUtc = root.GetProperty("SentOnUtc").ValueKind == JsonValueKind.Null
                    ? null
                    : root.GetProperty("SentOnUtc").GetDateTime()
            };
        }
        finally
        {
            try { File.Delete(scriptPath); } catch (IOException) { }
        }
    }
}
