#nullable enable
#pragma warning disable SA1649
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kor.EmailCommon;
using Kor.Operations.App.Options;
using Kor.Operations.Data;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.App.Email;

internal sealed class EmailFilingResult
{
    public int FiledCount { get; init; }
    public int SkippedCount { get; init; }
    public string? DestinationFolder { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FiledPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FiledSourcePaths { get; init; } = Array.Empty<string>();
}

internal sealed class EmailFilingService
{
    private static readonly string DebugLogPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KorTransmittals",
            "Logs",
            "EmailFilePicker_MsgReaderDebug.txt");

    private static bool _encodingsRegistered;

    private readonly SqlEmailIndexStore? _emailIndexStore;
    private readonly StorageOptions _storageOptions;
    private readonly ILogger<EmailFilingService> _logger;

    public EmailFilingService(SqlEmailIndexStore? emailIndexStore, StorageOptions storageOptions, ILogger<EmailFilingService> logger)
    {
        _emailIndexStore = emailIndexStore;
        _storageOptions = storageOptions ?? throw new ArgumentNullException(nameof(storageOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<EmailFilingResult> FileEmailsAsync(
        IEnumerable<string> emailPaths,
        string destinationFolder,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(destinationFolder);

        var copied = 0;
        var skipped = 0;
        var errors = new List<string>();
        var filedPaths = new List<string>();
        var filedSourcePaths = new List<string>();
        var indexingTasks = new List<Task>();
        var projectNumber = GetProjectNumber(destinationFolder);
        foreach (var src in emailPaths)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (!File.Exists(src))
                {
                    skipped++;
                    continue;
                }

                if (!(src.EndsWith(".msg", StringComparison.OrdinalIgnoreCase) ||
                      src.EndsWith(".eml", StringComparison.OrdinalIgnoreCase)))
                {
                    skipped++;
                    continue;
                }

                string fileName = Path.GetFileName(src);
                string destPath = Path.Combine(destinationFolder, fileName);
                destPath = EnsureUniquePath(destPath);

                File.Copy(src, destPath);
                filedPaths.Add(destPath);
                filedSourcePaths.Add(src);
                copied++;

                if (_emailIndexStore != null)
                    indexingTasks.Add(IndexEmailAsync(projectNumber, destPath, ct));
                else
                    DebugLog("Email filed but index store is null; not indexed.");
            }
            catch (Exception ex)
            {
                errors.Add(src + " -> " + ex.Message);
            }
        }

        // Block until every row is in dbo.Emails so a search immediately after
        // filing actually finds the email. Each task swallows its own exceptions
        // so WhenAll never propagates indexing failures.
        if (indexingTasks.Count > 0)
            await Task.WhenAll(indexingTasks).ConfigureAwait(false);

        return new EmailFilingResult
        {
            FiledCount = copied,
            SkippedCount = skipped,
            DestinationFolder = destinationFolder,
            Errors = errors,
            FiledPaths = filedPaths,
            FiledSourcePaths = filedSourcePaths
        };
    }

    private async Task IndexEmailAsync(string projectNumber, string destPath, CancellationToken ct)
    {
        if (_emailIndexStore == null)
            return;

        EnsureCodePagesEncodingRegistered();

        ParsedEmail? parsed = null;
        bool isCorrupt = false;

        try
        {
            parsed = EmailParser.Parse(destPath);
        }
        catch (Exception ex)
        {
            // Record the file even when parsing fails — search results will surface
            // it with empty metadata and IsCorrupt=true so the user can investigate.
            DebugLog($"Parse failed for {destPath}: {ex.GetType().Name}: {ex.Message}");
            _logger.LogWarning(ex, "Email parse failed for {Path}; recording as corrupt.", destPath);
            isCorrupt = true;
        }

        try
        {
            await _emailIndexStore.InsertEmailAsync(
                projectNumber: projectNumber,
                filePath: destPath,
                subject: parsed?.Subject ?? string.Empty,
                fromEmail: parsed?.FromEmail ?? string.Empty,
                sentOnUtc: parsed?.SentOnUtc,
                attachmentCount: parsed?.AttachmentCount ?? 0,
                hasAttachments: parsed?.HasAttachments ?? false,
                fromDisplay: parsed?.FromDisplay,
                toList: parsed?.ToList,
                ccList: parsed?.CcList,
                bccList: parsed?.BccList,
                bodyText: parsed?.BodyText,
                receivedOnUtc: parsed?.ReceivedOnUtc,
                messageId: parsed?.MessageId,
                isCorrupt: isCorrupt,
                ct: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DebugLog($"Indexing failed for {destPath}: {ex.GetType().Name}: {ex.Message}");
            _logger.LogWarning(ex, "Email indexing failed for {Path}.", destPath);
        }
    }

    public string EnsureUniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        string dir = Path.GetDirectoryName(path) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);

        int i = 1;
        string candidate;

        do
        {
            candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            i++;
        } while (File.Exists(candidate));

        return candidate;
    }

    private static void DebugLog(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DebugLogPath)!);

            File.AppendAllText(
                DebugLogPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch
        {
            // ignore logging failures
        }
    }

    private static void EnsureCodePagesEncodingRegistered()
    {
        if (_encodingsRegistered)
            return;

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _encodingsRegistered = true;
            DebugLog("CodePagesEncodingProvider registered.");
        }
        catch (Exception ex)
        {
            DebugLog($"Encoding.RegisterProvider failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string GetProjectNumber(string destinationFolder)
    {
        try
        {
            var projectRoot = Directory.GetParent(destinationFolder)?.Parent?.Parent?.Name;
            if (!string.IsNullOrWhiteSpace(projectRoot) && projectRoot.Length >= 8)
                return projectRoot.Substring(0, 8);
        }
        catch
        {
        }

        return string.Empty;
    }

}
