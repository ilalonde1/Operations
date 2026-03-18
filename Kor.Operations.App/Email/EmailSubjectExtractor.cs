#nullable enable
using System;
using System.IO;
using Kor.Operations.Core;
using Microsoft.Extensions.Logging;
using MsgReader.Mime;
using MsgReader.Outlook;
using OutlookAttachment = MsgReader.Outlook.Storage.Attachment;

namespace Kor.Operations.App.Email;

internal sealed class EmailSubjectExtractor
{
    private static readonly string DebugLogPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KorTransmittals",
            "Logs",
            "EmailFilePicker_MsgReaderDebug.txt");

    public EmailSubjectExtractor(ILogger<EmailSubjectExtractor> logger)
    {
        _ = logger ?? throw new ArgumentNullException(nameof(logger));
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

    private static string GetSubjectFromMsg(string path)
    {
        try
        {
            DebugLog($"MSG: Opening {path}");
            using var msg = new Storage.Message(path);
            var subject = msg.Subject ?? string.Empty;
            DebugLog($"MSG: Subject='{subject}'");
            return subject;
        }
        catch (Exception ex)
        {
            DebugLog($"MSG: EX {ex.GetType().Name}: {ex.Message}");
            return string.Empty;
        }
    }

    private static string GetSubjectFromEml(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            var eml = Message.Load(fileInfo);
            var subject = eml?.Headers?.Subject ?? string.Empty;
            DebugLog($"EML: Subject='{subject}' from {path}");
            return subject;
        }
        catch (Exception ex)
        {
            DebugLog($"EML: EX {ex.GetType().Name}: {ex.Message}");
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
            DebugLog($"EmailHasAttachments failed for {emailPath}: {ex.GetType().Name}: {ex.Message}");
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
            DebugLog($"GetFirstAttachmentFileName failed for {emailPath}: {ex.GetType().Name}: {ex.Message}");
        }

        return string.Empty;
    }
}
