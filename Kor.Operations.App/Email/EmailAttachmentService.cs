#nullable enable
#pragma warning disable SA1649
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core;
using Microsoft.Extensions.Logging;
using MsgReader.Outlook;
using OutlookAttachment = MsgReader.Outlook.Storage.Attachment;

namespace Kor.Operations.App.Email;

internal sealed class AttachmentSaveResult
{
    public int SavedCount { get; init; }
    public int SkippedCount { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

internal sealed class AttachmentInfo
{
    public int Index { get; init; }
    public string FileName { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public bool IsLikelyInlineImage { get; init; }
    public string? SkipReason { get; init; }
}

internal sealed class EmailAttachmentService
{
    private static readonly string DebugLogPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KorTransmittals",
            "Logs",
            "EmailFilePicker_MsgReaderDebug.txt");

    private readonly EmailFilingService _filingService;
    private readonly ILogger<EmailAttachmentService> _logger;

    public EmailAttachmentService(EmailFilingService filingService, ILogger<EmailAttachmentService> logger)
    {
        _filingService = filingService ?? throw new ArgumentNullException(nameof(filingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Returns metadata for every attachment in the email — savable and not.
    /// Use <see cref="AttachmentInfo.SkipReason"/> to filter the savable subset.
    /// </summary>
    public IReadOnlyList<AttachmentInfo> ListAttachments(string emailPath)
    {
        var list = new List<AttachmentInfo>();
        var options = EmailIndexOptions.FromAppConfig();

        foreach (var raw in EnumerateAttachments(emailPath))
        {
            list.Add(BuildAttachmentInfo(raw.Index, raw.FileName, raw.SizeBytes, options));
        }

        return list;
    }

    /// <summary>
    /// Saves attachments whose index appears in <paramref name="selectedIndices"/>.
    /// Index numbering matches what <see cref="ListAttachments"/> returned for the
    /// same email path. Filtered attachments (blocked extension, oversized, empty)
    /// count toward SkippedCount; exceptions count toward Errors.
    /// </summary>
    public Task<AttachmentSaveResult> SaveSelectedAttachmentsAsync(
        string emailPath,
        string destinationFolder,
        IReadOnlySet<int> selectedIndices,
        CancellationToken ct = default)
    {
        if (selectedIndices == null)
            throw new ArgumentNullException(nameof(selectedIndices));

        if (!File.Exists(emailPath) || selectedIndices.Count == 0)
            return Task.FromResult(new AttachmentSaveResult());

        var savedCount = 0;
        var skippedCount = 0;
        var errors = new List<string>();

        try
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(destinationFolder);
            var options = EmailIndexOptions.FromAppConfig();

            foreach (var raw in EnumerateAttachments(emailPath))
            {
                ct.ThrowIfCancellationRequested();

                if (!selectedIndices.Contains(raw.Index))
                    continue;

                switch (TrySaveOne(raw, destinationFolder, options, emailPath, errors))
                {
                    case SaveOutcome.Saved:           savedCount++;   break;
                    case SaveOutcome.SkippedFiltered: skippedCount++; break;
                    // SaveOutcome.Error: TrySaveOne already appended to errors.
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            errors.Add($"{emailPath} -> {ex.Message}");
            DebugLog($"SaveSelectedAttachmentsAsync general error for {emailPath}: {ex.GetType().Name}: {ex.Message}");
        }

        return Task.FromResult(new AttachmentSaveResult
        {
            SavedCount = savedCount,
            SkippedCount = skippedCount,
            Errors = errors
        });
    }

    // ---- Single iteration source for both reading and saving ----

    private readonly struct RawAttachment
    {
        public int Index { get; init; }
        public string FileName { get; init; }
        public byte[]? Data { get; init; }
        public long SizeBytes { get; init; }
    }

    private enum SaveOutcome { Saved, SkippedFiltered, Error }

    /// <summary>
    /// Walks the .msg / .eml attachment collection exactly once. Yields a uniform
    /// view regardless of file format. Disposal of the underlying MsgReader handle
    /// is tied to the iterator's lifetime.
    /// </summary>
    private IEnumerable<RawAttachment> EnumerateAttachments(string emailPath)
    {
        if (!File.Exists(emailPath))
            yield break;

        string ext = Path.GetExtension(emailPath);

        if (ext.Equals(".msg", StringComparison.OrdinalIgnoreCase))
        {
            var msg = TryOpenMsg(emailPath);
            if (msg == null)
                yield break;

            using (msg)
            {
                if (msg.Attachments == null)
                    yield break;

                int idx = 0;
                foreach (var obj in msg.Attachments)
                {
                    int currentIdx = idx++;
                    if (obj is not OutlookAttachment att)
                        continue;

                    yield return new RawAttachment
                    {
                        Index = currentIdx,
                        FileName = string.IsNullOrWhiteSpace(att.FileName) ? "Attachment.bin" : att.FileName,
                        Data = att.Data,
                        SizeBytes = att.Data?.LongLength ?? 0L
                    };
                }
            }
        }
        else if (ext.Equals(".eml", StringComparison.OrdinalIgnoreCase))
        {
            var eml = TryLoadEml(emailPath);
            if (eml?.Attachments == null)
                yield break;

            int idx = 0;
            foreach (var part in eml.Attachments)
            {
                int currentIdx = idx++;
                if (part == null || !part.IsAttachment)
                    continue;

                yield return new RawAttachment
                {
                    Index = currentIdx,
                    FileName = string.IsNullOrWhiteSpace(part.FileName) ? "Attachment.bin" : part.FileName,
                    Data = part.Body,
                    SizeBytes = part.Body?.LongLength ?? 0L
                };
            }
        }
        else
        {
            DebugLog($"EnumerateAttachments: unsupported extension for {emailPath}");
        }
    }

    private Storage.Message? TryOpenMsg(string emailPath)
    {
        try
        {
            return new Storage.Message(emailPath);
        }
        catch (Exception ex)
        {
            DebugLog($"Failed to open MSG {emailPath}: {ex.GetType().Name}: {ex.Message}");
            _logger.LogWarning(ex, "Failed to open MSG {Path}.", emailPath);
            return null;
        }
    }

    private MsgReader.Mime.Message? TryLoadEml(string emailPath)
    {
        try
        {
            return MsgReader.Mime.Message.Load(new FileInfo(emailPath));
        }
        catch (Exception ex)
        {
            DebugLog($"Failed to load EML {emailPath}: {ex.GetType().Name}: {ex.Message}");
            _logger.LogWarning(ex, "Failed to load EML {Path}.", emailPath);
            return null;
        }
    }

    // ---- Single filter + save policy ----

    private SaveOutcome TrySaveOne(
        RawAttachment raw,
        string destinationFolder,
        EmailIndexOptions options,
        string emailPath,
        List<string> errors)
    {
        try
        {
            string? skip = GetSkipReason(raw.FileName, raw.SizeBytes, options);
            if (skip != null)
            {
                _logger.LogWarning("Skipping attachment {FileName} ({Reason}).", raw.FileName, skip);
                return SaveOutcome.SkippedFiltered;
            }

            var safeFileName = Path.GetFileName(raw.FileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(safeFileName) ||
                Path.IsPathRooted(safeFileName) ||
                safeFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                _logger.LogWarning("Rejected unsafe attachment filename {FileName}.", SanitizeForLog(safeFileName));
                return SaveOutcome.SkippedFiltered;
            }

            string targetPath = Path.Combine(destinationFolder, safeFileName);
            targetPath = _filingService.EnsureUniquePath(targetPath);
            var destinationRoot = EnsureTrailingSeparator(Path.GetFullPath(destinationFolder));
            var targetFullPath = Path.GetFullPath(targetPath);
            if (!targetFullPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Rejected attachment filename outside destination folder {FileName}.", SanitizeForLog(safeFileName));
                return SaveOutcome.SkippedFiltered;
            }

            File.WriteAllBytes(targetPath, raw.Data!);
            DebugLog($"Saved attachment to {targetPath}");
            return SaveOutcome.Saved;
        }
        catch (Exception ex)
        {
            errors.Add($"{emailPath} -> {ex.Message}");
            DebugLog($"Failed to save attachment {raw.FileName} for {emailPath}: {ex.GetType().Name}: {ex.Message}");
            return SaveOutcome.Error;
        }
    }

    private static string EnsureTrailingSeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
           path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;

    private static string SanitizeForLog(string? value)
        => (value ?? string.Empty)
            .Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", "", StringComparison.Ordinal);

    private static string? GetSkipReason(string fileName, long sizeBytes, EmailIndexOptions options)
    {
        var ext = Path.GetExtension(fileName);
        if (options.BlockedExtensions.Contains(ext))
            return $"Blocked extension ({ext})";
        if (sizeBytes <= 0)
            return "Empty";
        if (sizeBytes > options.MaxAttachmentBytes)
            return $"Too large (> {options.MaxAttachmentBytes:N0} bytes)";
        return null;
    }

    private static AttachmentInfo BuildAttachmentInfo(int index, string fileName, long sizeBytes, EmailIndexOptions options)
    {
        bool isInline = sizeBytes > 0
            && sizeBytes < 50_000
            && System.Text.RegularExpressions.Regex.IsMatch(
                fileName,
                @"^image\d+\.(png|jpe?g|gif|bmp)$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return new AttachmentInfo
        {
            Index = index,
            FileName = fileName,
            SizeBytes = sizeBytes,
            IsLikelyInlineImage = isInline,
            SkipReason = GetSkipReason(fileName, sizeBytes, options)
        };
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
}
