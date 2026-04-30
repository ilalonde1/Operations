#nullable enable
#pragma warning disable SA1649
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
    private const int MaxConcurrentCopies = 4;
    private const int CopyBufferSize = 81920;

    private static readonly string DebugLogPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KorTransmittals",
            "Logs",
            "EmailFilePicker_MsgReaderDebug.txt");

    // Shared filing log on the fileserver — the addin writes here too, so all
    // filing events from any writer end up in one place for cross-machine
    // diagnostics. Rolled monthly (EmailFilingLog_YYYY-MM.txt) so the file
    // never grows unbounded and pruning is trivial. Falls back to DebugLog
    // if the share is unreachable.
    private const string SharedFilingLogDir =
        @"\\kor-fs01\Projects\Reporting\Scripts\Logs";

    private static string GetSharedFilingLogPath() =>
        Path.Combine(SharedFilingLogDir, $"EmailFilingLog_{DateTime.Now:yyyy-MM}.txt");

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
        string projectNumber,
        string destinationFolder,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectNumber))
            throw new ArgumentException("projectNumber is required so audit log entries are accurate.", nameof(projectNumber));

        Directory.CreateDirectory(destinationFolder);

        var emailList = emailPaths.ToList();

        // Run up to MaxConcurrentCopies copies in parallel. Outcomes are
        // indexed by input position so the result lists preserve the user's
        // original selection order — important because the picker shows the
        // attachment dialog per email in this order.
        var outcomes = new EmailCopyOutcome[emailList.Count];
        using (var sem = new SemaphoreSlim(MaxConcurrentCopies))
        {
            var copyTasks = emailList
                .Select((src, idx) => CopyWithSemaphoreAsync(src, idx, destinationFolder, projectNumber, sem, outcomes, ct))
                .ToList();

            await Task.WhenAll(copyTasks).ConfigureAwait(false);
        }

        // Aggregate outcomes in input order, kick off indexing for the copies that succeeded.
        var skipped = 0;
        var errors = new List<string>();
        var filedPaths = new List<string>();
        var filedSourcePaths = new List<string>();
        var notIndexedPaths = new List<string>();
        var indexingTasks = new List<Task<(string Path, bool Indexed)>>();

        foreach (var outcome in outcomes)
        {
            switch (outcome.Kind)
            {
                case CopyOutcomeKind.Copied:
                    filedPaths.Add(outcome.DestPath!);
                    filedSourcePaths.Add(outcome.SrcPath);

                    if (_emailIndexStore != null)
                    {
                        indexingTasks.Add(IndexEmailAsync(projectNumber, outcome.DestPath!, outcome.Sha1!, ct));
                    }
                    else
                    {
                        DebugLog("Email filed but index store is null; not indexed.");
                        FilingLog("INDEX SKIPPED (no store)", projectNumber, outcome.DestPath!);
                        notIndexedPaths.Add(outcome.DestPath!);
                    }
                    break;

                case CopyOutcomeKind.Skipped:
                    skipped++;
                    break;

                case CopyOutcomeKind.Error:
                    errors.Add(outcome.SrcPath + " -> " + (outcome.ErrorMessage ?? string.Empty));
                    break;
            }
        }

        // Block until every row is in dbo.Emails so a search immediately after
        // filing actually finds the email. Each task swallows its own exceptions
        // and reports its own indexed bool, so WhenAll never throws.
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
            FiledCount = filedPaths.Count,
            SkippedCount = skipped,
            IndexedCount = indexedCount,
            DestinationFolder = destinationFolder,
            Errors = errors,
            FiledPaths = filedPaths,
            FiledSourcePaths = filedSourcePaths,
            NotIndexedPaths = notIndexedPaths
        };
    }

    private async Task CopyWithSemaphoreAsync(
        string src,
        int idx,
        string destinationFolder,
        string projectNumber,
        SemaphoreSlim sem,
        EmailCopyOutcome[] outcomes,
        CancellationToken ct)
    {
        await sem.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            outcomes[idx] = await CopyOneAsync(src, destinationFolder, projectNumber, ct).ConfigureAwait(false);
        }
        finally
        {
            sem.Release();
        }
    }

    private async Task<EmailCopyOutcome> CopyOneAsync(
        string src,
        string destinationFolder,
        string projectNumber,
        CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            if (!File.Exists(src))
                return EmailCopyOutcome.Skipped(src);

            if (!IsSupportedExtension(src))
                return EmailCopyOutcome.Skipped(src);

            string preferred = Path.Combine(destinationFolder, Path.GetFileName(src));
            var (destPath, sha1Hex) = await CopyAndHashAsync(src, preferred, ct).ConfigureAwait(false);
            FilingLog("COPIED", projectNumber, destPath);
            return EmailCopyOutcome.Copied(src, destPath, sha1Hex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            FilingLog("COPY FAILED", projectNumber, src, ex.GetType().Name + ": " + ex.Message);
            return EmailCopyOutcome.Error(src, ex.Message);
        }
    }

    /// <summary>
    /// Copies <paramref name="srcPath"/> to a unique destination based on
    /// <paramref name="preferredDestPath"/> AND computes the SHA1 of the bytes
    /// in a single read pass via a CryptoStream. Returns the actual destination
    /// path used (may have a "(N)" suffix on collision) and the lowercase hex
    /// SHA1 digest.
    /// </summary>
    /// <remarks>
    /// Race-safety: <see cref="FileMode.CreateNew"/> is atomic at the OS level,
    /// so two concurrent copies competing for the same filename will not both
    /// win — the loser retries with a "(N)" suffix.
    /// </remarks>
    private static async Task<(string DestPath, string Sha1Hex)> CopyAndHashAsync(
        string srcPath,
        string preferredDestPath,
        CancellationToken ct)
    {
        string dir = Path.GetDirectoryName(preferredDestPath) ?? string.Empty;
        string nameNoExt = Path.GetFileNameWithoutExtension(preferredDestPath);
        string ext = Path.GetExtension(preferredDestPath);

        FileStream? dst = null;
        string destPath = preferredDestPath;
        int suffix = 0;

        while (dst == null)
        {
            ct.ThrowIfCancellationRequested();
            destPath = suffix == 0
                ? preferredDestPath
                : Path.Combine(dir, $"{nameNoExt} ({suffix}){ext}");

            try
            {
                dst = new FileStream(
                    destPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    CopyBufferSize,
                    useAsync: true);
            }
            catch (IOException) when (File.Exists(destPath))
            {
                suffix++;
                if (suffix > 10000)
                    throw;
            }
        }

        try
        {
            await using var src = new FileStream(
                srcPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                CopyBufferSize,
                useAsync: true);

            using var sha1 = SHA1.Create();

            // CryptoStream wraps dst; bytes flowing through it are both hashed
            // by sha1 AND written to dst. CryptoStream.Dispose() calls
            // FlushFinalBlock(), which is when sha1.Hash becomes valid.
            // leaveOpen so the dst FileStream is disposed by its own scope below.
            using (var hashStream = new CryptoStream(dst, sha1, CryptoStreamMode.Write, leaveOpen: true))
            {
                await src.CopyToAsync(hashStream, CopyBufferSize, ct).ConfigureAwait(false);
            }

            return (destPath, ToLowerHex(sha1.Hash!));
        }
        finally
        {
            await dst.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<(string Path, bool Indexed)> IndexEmailAsync(
        string projectNumber,
        string destPath,
        string fileHashSha1,
        CancellationToken ct)
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
                fileHashSha1: fileHashSha1,
                subject: parsed?.Subject ?? string.Empty,
                fromEmail: parsed?.FromEmail ?? string.Empty,
                sentOnUtc: parsed?.SentOnUtc,
                attachmentCount: parsed?.AttachmentCount ?? 0,
                hasAttachments: parsed?.HasAttachments ?? false,
                source: EmailSources.WpfPicker,
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

    private static bool IsSupportedExtension(string path) =>
        path.EndsWith(".msg", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".eml", StringComparison.OrdinalIgnoreCase);

    private static string ToLowerHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
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
            File.AppendAllLines(GetSharedFilingLogPath(), new[] { line });
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

    private readonly struct EmailCopyOutcome
    {
        public CopyOutcomeKind Kind { get; init; }
        public string SrcPath { get; init; }
        public string? DestPath { get; init; }
        public string? Sha1 { get; init; }
        public string? ErrorMessage { get; init; }

        public static EmailCopyOutcome Copied(string src, string dest, string sha1) =>
            new() { Kind = CopyOutcomeKind.Copied, SrcPath = src, DestPath = dest, Sha1 = sha1 };

        public static EmailCopyOutcome Skipped(string src) =>
            new() { Kind = CopyOutcomeKind.Skipped, SrcPath = src };

        public static EmailCopyOutcome Error(string src, string msg) =>
            new() { Kind = CopyOutcomeKind.Error, SrcPath = src, ErrorMessage = msg };
    }

    private enum CopyOutcomeKind { Copied, Skipped, Error }
}
