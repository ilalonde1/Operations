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
    public int IndexedCount { get; init; }
    public string? DestinationFolder { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FiledPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FiledSourcePaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> NotIndexedPaths { get; init; } = Array.Empty<string>();
}

internal sealed class EmailFilingService
{
    private static readonly string DebugLogPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KorTransmittals",
            "Logs",
            "EmailFilePicker_MsgReaderDebug.txt");

    // Shared filing log on the fileserver — the addin writes here too, so all
    // filing events from any writer end up in one place for cross-machine
    // diagnostics. Falls back to DebugLog if the share is unreachable.
    private const string SharedFilingLogPath =
        @"\\kor-fs01\Projects\Reporting\Scripts\Logs\EmailFilingLog.txt";

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
        var indexingTasks = new List<Task<(string Path, bool Indexed)>>();
        var notIndexedPaths = new List<string>();
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
                FilingLog("COPIED", projectNumber, destPath);

                if (_emailIndexStore != null)
                {
                    indexingTasks.Add(IndexEmailAsync(projectNumber, destPath, ct));
                }
                else
                {
                    DebugLog("Email filed but index store is null; not indexed.");
                    FilingLog("INDEX SKIPPED (no store)", projectNumber, destPath);
                    notIndexedPaths.Add(destPath);
                }
            }
            catch (Exception ex)
            {
                errors.Add(src + " -> " + ex.Message);
                FilingLog("COPY FAILED", projectNumber, src, ex.GetType().Name + ": " + ex.Message);
            }
        }

        // Block until every row is in dbo.Emails so a search immediately after
        // filing actually finds the email. Each task swallows its own exceptions
        // and reports its own indexed bool, so WhenAll never throws — we tally
        // the results below to populate IndexedCount and NotIndexedPaths.
        var indexedCount = 0;
        if (indexingTasks.Count > 0)
        {
            var indexResults = await Task.WhenAll(indexingTasks).ConfigureAwait(false);
            foreach (var (path, indexed) in indexResults)
            {
                if (indexed)
                    indexedCount++;
                else
                    notIndexedPaths.Add(path);
            }
        }

        return new EmailFilingResult
        {
            FiledCount = copied,
            SkippedCount = skipped,
            IndexedCount = indexedCount,
            DestinationFolder = destinationFolder,
            Errors = errors,
            FiledPaths = filedPaths,
            FiledSourcePaths = filedSourcePaths,
            NotIndexedPaths = notIndexedPaths
        };
    }

    private async Task<(string Path, bool Indexed)> IndexEmailAsync(string projectNumber, string destPath, CancellationToken ct)
    {
        if (_emailIndexStore == null)
            return (destPath, false);

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
            FilingLog("PARSE FAILED", projectNumber, destPath, ex.GetType().Name + ": " + ex.Message);
            isCorrupt = true;
        }

        try
        {
            bool inserted = await _emailIndexStore.InsertEmailAsync(
                projectNumber: projectNumber,
                filePath: destPath,
                subject: parsed?.Subject ?? string.Empty,
                fromEmail: parsed?.FromEmail ?? string.Empty,
                sentOnUtc: parsed?.SentOnUtc,
                attachmentCount: parsed?.AttachmentCount ?? 0,
                hasAttachments: parsed?.HasAttachments ?? false,
                source: "WPF-PICKER",
                fromDisplay: parsed?.FromDisplay,
                toList: parsed?.ToList,
                ccList: parsed?.CcList,
                bccList: parsed?.BccList,
                bodyText: parsed?.BodyText,
                receivedOnUtc: parsed?.ReceivedOnUtc,
                messageId: parsed?.MessageId,
                isCorrupt: isCorrupt,
                ct: ct).ConfigureAwait(false);

            string action = isCorrupt
                ? "INDEXED CORRUPT"
                : (inserted ? "INDEXED OK" : "INDEXED DEDUPED");
            FilingLog(action, projectNumber, destPath);
            return (destPath, true);
        }
        catch (Exception ex)
        {
            DebugLog($"Indexing failed for {destPath}: {ex.GetType().Name}: {ex.Message}");
            _logger.LogWarning(ex, "Email indexing failed for {Path}.", destPath);
            FilingLog("INDEX FAILED", projectNumber, destPath, ex.GetType().Name + ": " + ex.Message);
            return (destPath, false);
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

    private static void FilingLog(string action, string projectNumber, string filePath, string? detail = null)
    {
        var line =
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {Environment.UserName} | WPF-PICKER | {action} | {projectNumber} | {filePath}"
            + (string.IsNullOrEmpty(detail) ? string.Empty : " | " + detail);

        try
        {
            File.AppendAllLines(SharedFilingLogPath, new[] { line });
        }
        catch
        {
            // Network share unreachable — keep the line in the local debug log
            // so we don't lose the audit trail entirely.
            DebugLog("[FilingLog fallback] " + line);
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
