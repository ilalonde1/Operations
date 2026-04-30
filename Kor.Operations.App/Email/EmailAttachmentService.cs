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

    public Task<AttachmentSaveResult> SaveAttachmentsAsync(
        string emailPath,
        string destinationFolder,
        CancellationToken ct = default)
    {
        if (!File.Exists(emailPath))
        {
            return Task.FromResult(new AttachmentSaveResult
            {
                SavedCount = 0,
                SkippedCount = 1
            });
        }

        string extension = Path.GetExtension(emailPath);
        var savedCount = 0;
        var skippedCount = 0;
        var errors = new List<string>();

        try
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(destinationFolder);
            var options = EmailIndexOptions.FromAppConfig();

            if (extension.Equals(".msg", StringComparison.OrdinalIgnoreCase))
            {
                using var msg = new Storage.Message(emailPath);

                if (msg.Attachments == null || msg.Attachments.Count == 0)
                {
                    DebugLog($"No MSG attachments found for {emailPath}");
                    return Task.FromResult(new AttachmentSaveResult
                    {
                        SavedCount = savedCount,
                        SkippedCount = skippedCount,
                        Errors = errors
                    });
                }

                foreach (var obj in msg.Attachments)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        if (obj is not OutlookAttachment attach)
                        {
                            skippedCount++;
                            continue;
                        }

                        string attName = attach.FileName;
                        if (string.IsNullOrWhiteSpace(attName))
                            attName = "Attachment.bin";

                        var ext = Path.GetExtension(attName);
                        if (options.BlockedExtensions.Contains(ext))
                        {
                            _logger.LogWarning(
                                "Skipping blocked attachment {FileName} (extension {Ext}).",
                                attName, ext);
                            skippedCount++;
                            continue;
                        }

                        var data = attach.Data;
                        if (data == null || data.Length == 0)
                        {
                            skippedCount++;
                            continue;
                        }

                        var fileSize = data.Length;
                        if (fileSize > options.MaxAttachmentBytes)
                        {
                            _logger.LogWarning(
                                "Skipping oversized attachment {FileName} ({Bytes} bytes, limit {Limit}).",
                                attName, fileSize, options.MaxAttachmentBytes);
                            skippedCount++;
                            continue;
                        }

                        string targetPath = Path.Combine(destinationFolder, attName);
                        targetPath = _filingService.EnsureUniquePath(targetPath);

                        File.WriteAllBytes(targetPath, data);
                        savedCount++;
                        DebugLog($"Saved MSG attachment to {targetPath}");
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{emailPath} -> {ex.Message}");
                        DebugLog($"Failed to save MSG attachment for {emailPath}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            else if (extension.Equals(".eml", StringComparison.OrdinalIgnoreCase))
            {
                var fileInfo = new FileInfo(emailPath);
                var eml = MsgReader.Mime.Message.Load(fileInfo);

                if (eml.Attachments == null || eml.Attachments.Count == 0)
                {
                    DebugLog($"No EML attachments found for {emailPath}");
                    return Task.FromResult(new AttachmentSaveResult
                    {
                        SavedCount = savedCount,
                        SkippedCount = skippedCount,
                        Errors = errors
                    });
                }

                foreach (var part in eml.Attachments)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        if (part == null || !part.IsAttachment)
                        {
                            skippedCount++;
                            continue;
                        }

                        string attName = part.FileName;
                        if (string.IsNullOrWhiteSpace(attName))
                            attName = "Attachment.bin";

                        var ext = Path.GetExtension(attName);
                        if (options.BlockedExtensions.Contains(ext))
                        {
                            _logger.LogWarning(
                                "Skipping blocked attachment {FileName} (extension {Ext}).",
                                attName, ext);
                            skippedCount++;
                            continue;
                        }

                        var data = part.Body;
                        if (data == null || data.Length == 0)
                        {
                            skippedCount++;
                            continue;
                        }

                        var fileSize = data.Length;
                        if (fileSize > options.MaxAttachmentBytes)
                        {
                            _logger.LogWarning(
                                "Skipping oversized attachment {FileName} ({Bytes} bytes, limit {Limit}).",
                                attName, fileSize, options.MaxAttachmentBytes);
                            skippedCount++;
                            continue;
                        }

                        string targetPath = Path.Combine(destinationFolder, attName);
                        targetPath = _filingService.EnsureUniquePath(targetPath);

                        File.WriteAllBytes(targetPath, data);
                        savedCount++;
                        DebugLog($"Saved EML attachment to {targetPath}");
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{emailPath} -> {ex.Message}");
                        DebugLog($"Failed to save EML attachment for {emailPath}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            else
            {
                DebugLog($"SaveAttachmentsForEmail: unsupported extension for {emailPath}");
                skippedCount++;
            }
        }
        catch (Exception ex)
        {
            errors.Add($"{emailPath} -> {ex.Message}");
            DebugLog($"SaveAttachmentsForEmail general error for {emailPath}: {ex.GetType().Name}: {ex.Message}");
        }

        return Task.FromResult(new AttachmentSaveResult
        {
            SavedCount = savedCount,
            SkippedCount = skippedCount,
            Errors = errors
        });
    }

    public IReadOnlyList<AttachmentInfo> ListAttachments(string emailPath)
    {
        var list = new List<AttachmentInfo>();

        if (!File.Exists(emailPath))
            return list;

        string extension = Path.GetExtension(emailPath);
        var options = EmailIndexOptions.FromAppConfig();

        try
        {
            if (extension.Equals(".msg", StringComparison.OrdinalIgnoreCase))
            {
                using var msg = new Storage.Message(emailPath);
                if (msg.Attachments == null)
                    return list;

                int idx = 0;
                foreach (var obj in msg.Attachments)
                {
                    int currentIdx = idx++;
                    if (obj is not OutlookAttachment att)
                        continue;

                    long size = att.Data?.LongLength ?? 0L;
                    list.Add(BuildAttachmentInfo(currentIdx, att.FileName, size, options));
                }
            }
            else if (extension.Equals(".eml", StringComparison.OrdinalIgnoreCase))
            {
                var fileInfo = new FileInfo(emailPath);
                var eml = MsgReader.Mime.Message.Load(fileInfo);
                if (eml.Attachments == null)
                    return list;

                int idx = 0;
                foreach (var part in eml.Attachments)
                {
                    int currentIdx = idx++;
                    if (part == null || !part.IsAttachment)
                        continue;

                    long size = part.Body?.LongLength ?? 0L;
                    list.Add(BuildAttachmentInfo(currentIdx, part.FileName, size, options));
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog($"ListAttachments failed for {emailPath}: {ex.GetType().Name}: {ex.Message}");
            _logger.LogWarning(ex, "Failed to list attachments for {Path}.", emailPath);
        }

        return list;
    }

    public Task<AttachmentSaveResult> SaveSelectedAttachmentsAsync(
        string emailPath,
        string destinationFolder,
        IReadOnlySet<int> selectedIndices,
        CancellationToken ct = default)
    {
        if (selectedIndices == null)
            throw new ArgumentNullException(nameof(selectedIndices));

        if (!File.Exists(emailPath) || selectedIndices.Count == 0)
        {
            return Task.FromResult(new AttachmentSaveResult());
        }

        string extension = Path.GetExtension(emailPath);
        var savedCount = 0;
        var skippedCount = 0;
        var errors = new List<string>();

        try
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(destinationFolder);
            var options = EmailIndexOptions.FromAppConfig();

            if (extension.Equals(".msg", StringComparison.OrdinalIgnoreCase))
            {
                using var msg = new Storage.Message(emailPath);
                if (msg.Attachments != null)
                {
                    int idx = 0;
                    foreach (var obj in msg.Attachments)
                    {
                        int currentIdx = idx++;
                        ct.ThrowIfCancellationRequested();

                        if (!selectedIndices.Contains(currentIdx))
                            continue;

                        try
                        {
                            if (obj is not OutlookAttachment attach)
                            {
                                skippedCount++;
                                continue;
                            }

                            string attName = string.IsNullOrWhiteSpace(attach.FileName) ? "Attachment.bin" : attach.FileName;
                            var ext = Path.GetExtension(attName);
                            if (options.BlockedExtensions.Contains(ext))
                            {
                                skippedCount++;
                                continue;
                            }

                            var data = attach.Data;
                            if (data == null || data.Length == 0)
                            {
                                skippedCount++;
                                continue;
                            }

                            if (data.Length > options.MaxAttachmentBytes)
                            {
                                skippedCount++;
                                continue;
                            }

                            string targetPath = Path.Combine(destinationFolder, attName);
                            targetPath = _filingService.EnsureUniquePath(targetPath);

                            File.WriteAllBytes(targetPath, data);
                            savedCount++;
                            DebugLog($"Saved selected MSG attachment to {targetPath}");
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"{emailPath} -> {ex.Message}");
                            DebugLog($"Failed to save selected MSG attachment idx={currentIdx} for {emailPath}: {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                }
            }
            else if (extension.Equals(".eml", StringComparison.OrdinalIgnoreCase))
            {
                var fileInfo = new FileInfo(emailPath);
                var eml = MsgReader.Mime.Message.Load(fileInfo);
                if (eml.Attachments != null)
                {
                    int idx = 0;
                    foreach (var part in eml.Attachments)
                    {
                        int currentIdx = idx++;
                        ct.ThrowIfCancellationRequested();

                        if (!selectedIndices.Contains(currentIdx))
                            continue;

                        try
                        {
                            if (part == null || !part.IsAttachment)
                            {
                                skippedCount++;
                                continue;
                            }

                            string attName = string.IsNullOrWhiteSpace(part.FileName) ? "Attachment.bin" : part.FileName;
                            var ext = Path.GetExtension(attName);
                            if (options.BlockedExtensions.Contains(ext))
                            {
                                skippedCount++;
                                continue;
                            }

                            var data = part.Body;
                            if (data == null || data.Length == 0)
                            {
                                skippedCount++;
                                continue;
                            }

                            if (data.Length > options.MaxAttachmentBytes)
                            {
                                skippedCount++;
                                continue;
                            }

                            string targetPath = Path.Combine(destinationFolder, attName);
                            targetPath = _filingService.EnsureUniquePath(targetPath);

                            File.WriteAllBytes(targetPath, data);
                            savedCount++;
                            DebugLog($"Saved selected EML attachment to {targetPath}");
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"{emailPath} -> {ex.Message}");
                            DebugLog($"Failed to save selected EML attachment idx={currentIdx} for {emailPath}: {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                }
            }
            else
            {
                DebugLog($"SaveSelectedAttachmentsAsync: unsupported extension for {emailPath}");
                skippedCount = selectedIndices.Count;
            }
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

    private static AttachmentInfo BuildAttachmentInfo(int index, string? fileName, long sizeBytes, EmailIndexOptions options)
    {
        string name = string.IsNullOrWhiteSpace(fileName) ? "Attachment.bin" : fileName;
        string ext = Path.GetExtension(name);
        string? skipReason = null;

        if (options.BlockedExtensions.Contains(ext))
            skipReason = $"Blocked extension ({ext})";
        else if (sizeBytes <= 0)
            skipReason = "Empty";
        else if (sizeBytes > options.MaxAttachmentBytes)
            skipReason = $"Too large (> {options.MaxAttachmentBytes:N0} bytes)";

        bool isInline = sizeBytes > 0
            && sizeBytes < 50_000
            && System.Text.RegularExpressions.Regex.IsMatch(
                name,
                @"^image\d+\.(png|jpe?g|gif|bmp)$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return new AttachmentInfo
        {
            Index = index,
            FileName = name,
            SizeBytes = sizeBytes,
            IsLikelyInlineImage = isInline,
            SkipReason = skipReason
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
