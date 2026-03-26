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
