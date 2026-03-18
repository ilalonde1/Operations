#nullable enable
using System;
using System.IO;
using Microsoft.Extensions.Logging;
using MsgReader.Mime;
using MsgReader.Outlook;
using OutlookAttachment = MsgReader.Outlook.Storage.Attachment;

namespace Kor.Operations.App.Email;

internal sealed class EmailSubjectExtractor
{
    private readonly ILogger<EmailSubjectExtractor> _logger;

    public EmailSubjectExtractor(ILogger<EmailSubjectExtractor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private string GetSubjectFromMsg(string path)
    {
        try
        {
            _logger.LogDebug("Opening MSG email file {Path}.", path);
            using var msg = new Storage.Message(path);
            var subject = msg.Subject ?? string.Empty;
            _logger.LogDebug("Extracted MSG subject {Subject} from {Path}.", subject, path);
            return subject;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract subject from MSG file {Path}.", path);
            return string.Empty;
        }
    }

    private string GetSubjectFromEml(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            var eml = Message.Load(fileInfo);
            var subject = eml?.Headers?.Subject ?? string.Empty;
            _logger.LogDebug("Extracted EML subject {Subject} from {Path}.", subject, path);
            return subject;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract subject from EML file {Path}.", path);
            return string.Empty;
        }
    }

    public string ExtractSubject(string path)
    {
        if (path.EndsWith(".msg", StringComparison.OrdinalIgnoreCase))
            return GetSubjectFromMsg(path);

        if (path.EndsWith(".eml", StringComparison.OrdinalIgnoreCase))
            return GetSubjectFromEml(path);

        return string.Empty;
    }

    public bool EmailHasAttachments(string emailPath)
    {
        try
        {
            string ext = Path.GetExtension(emailPath);

            if (ext.Equals(".msg", StringComparison.OrdinalIgnoreCase))
            {
                using var msg = new Storage.Message(emailPath);
                return msg.Attachments != null && msg.Attachments.Count > 0;
            }
            else if (ext.Equals(".eml", StringComparison.OrdinalIgnoreCase))
            {
                var fi = new FileInfo(emailPath);
                var eml = Message.Load(fi);
                return eml.Attachments != null && eml.Attachments.Count > 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to inspect attachments for email file {Path}.", emailPath);
        }

        return false;
    }

    public string GetFirstAttachmentFileName(string emailPath)
    {
        try
        {
            string ext = Path.GetExtension(emailPath);

            if (ext.Equals(".msg", StringComparison.OrdinalIgnoreCase))
            {
                using var msg = new Storage.Message(emailPath);

                if (msg.Attachments == null || msg.Attachments.Count == 0)
                    return string.Empty;

                foreach (var obj in msg.Attachments)
                {
                    if (obj is OutlookAttachment att && !string.IsNullOrWhiteSpace(att.FileName))
                        return att.FileName;
                }
            }
            else if (ext.Equals(".eml", StringComparison.OrdinalIgnoreCase))
            {
                var fi = new FileInfo(emailPath);
                var eml = Message.Load(fi);

                if (eml.Attachments == null || eml.Attachments.Count == 0)
                    return string.Empty;

                foreach (var part in eml.Attachments)
                {
                    if (part != null && part.IsAttachment && !string.IsNullOrWhiteSpace(part.FileName))
                        return part.FileName;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get first attachment file name for email file {Path}.", emailPath);
        }

        return string.Empty;
    }
}
