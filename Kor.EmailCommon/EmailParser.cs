using MimeKit;
using MsgReader.Outlook;
using System.Net;
using System.Text.RegularExpressions;

namespace Kor.EmailCommon;

public static class EmailParser
{
    public static ParsedEmail Parse(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".msg" => ParseMsg(path),
            _ => ParseEml(path)
        };
    }

    private static ParsedEmail ParseMsg(string path)
    {
        using var fs = File.OpenRead(path);
        using var msg = new Storage.Message(fs);

        var body = msg.BodyText;
        if (string.IsNullOrWhiteSpace(body) && !string.IsNullOrWhiteSpace(msg.BodyHtml))
            body = HtmlToText(msg.BodyHtml);

        var allRecips = (msg.Recipients ?? new System.Collections.Generic.List<Storage.Recipient>())
            .Select(r => (r.Email ?? string.Empty).Trim().ToLowerInvariant())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();
        var joined = string.Join("; ", allRecips);

        DateTime? sentUtc = TryGetSentOnUtc(msg);
        var attachmentCount = msg.Attachments?.Count ?? 0;

        return new ParsedEmail(
            msg.Subject,
            msg.Sender?.DisplayName,
            msg.Sender?.Email?.Trim().ToLowerInvariant(),
            joined,
            string.Empty,
            string.Empty,
            sentUtc,
            null,
            body ?? string.Empty,
            attachmentCount,
            attachmentCount > 0
        );
    }

    private static ParsedEmail ParseEml(string path)
    {
        var mime = MimeMessage.Load(path);

        string JoinEmails(InternetAddressList list) =>
            string.Join("; ",
                list.Mailboxes
                    .Select(m => (m.Address ?? string.Empty).Trim().ToLowerInvariant())
                    .Where(s => !string.IsNullOrWhiteSpace(s)));

        var text = mime.TextBody ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(mime.HtmlBody))
            text = HtmlToText(mime.HtmlBody);

        return new ParsedEmail(
            mime.Subject,
            mime.From.Mailboxes.FirstOrDefault()?.Name,
            mime.From.Mailboxes.FirstOrDefault()?.Address?.Trim().ToLowerInvariant(),
            JoinEmails(mime.To),
            JoinEmails(mime.Cc),
            JoinEmails(mime.Bcc),
            mime.Date != DateTimeOffset.MinValue ? mime.Date.UtcDateTime : null,
            null,
            text,
            mime.Attachments.Count(),
            mime.Attachments.Any()
        );
    }

    private static string HtmlToText(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        var noTags = Regex.Replace(html, "<[^>]+>", " ");
        var decoded = WebUtility.HtmlDecode(noTags);
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }

    private static DateTime? TryGetSentOnUtc(Storage.Message msg)
    {
        var prop = typeof(Storage.Message).GetProperty("SentOn");
        var val = prop?.GetValue(msg);
        if (val is DateTime dt) return DateTime.SpecifyKind(dt, DateTimeKind.Local).ToUniversalTime();
        if (val is DateTimeOffset dto) return dto.UtcDateTime;
        return null;
    }
}
