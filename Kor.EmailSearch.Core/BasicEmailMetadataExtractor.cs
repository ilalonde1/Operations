#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MsgReader.Outlook;

namespace Kor.EmailSearch.Core
{
    public sealed class BasicEmailMetadataExtractor : IEmailMetadataExtractor
    {
        private const int MaxBodyLength = 4000;

        public Task<EmailMetadata> ExtractAsync(
            string projectNumber,
            string filePath,
            CancellationToken ct = default)
        {
            string format = GetFormat(filePath);
            var meta = new EmailMetadata
            {
                ProjectNumber = projectNumber ?? string.Empty,
                FileName = Path.GetFileName(filePath ?? string.Empty) ?? string.Empty,
                Format = format,
                HasAttachments = false,
                AttachmentCount = 0
            };

            try
            {
                ct.ThrowIfCancellationRequested();

                var validatedFilePath = GetValidatedFilePath(filePath);

                if (format == "MSG")
                {
                    PopulateMsg(meta, validatedFilePath);
                }
                else if (format == "EML")
                {
                    PopulateEml(meta, validatedFilePath);
                }
            }
            catch
            {
                meta.Subject = null;
                meta.MessageId = null;
                meta.FromDisplay = null;
                meta.FromEmail = null;
                meta.ToList = null;
                meta.CcList = null;
                meta.BccList = null;
                meta.SentOnUtc = null;
                meta.ReceivedOnUtc = null;
                meta.BodyText = null;
                meta.HasAttachments = false;
                meta.AttachmentCount = 0;
            }

            return Task.FromResult(meta);
        }

        private static void PopulateMsg(EmailMetadata meta, string filePath)
        {
            using var msg = new MsgReader.Outlook.Storage.Message(filePath);

            meta.Subject = NullIfWhiteSpace(msg.Subject);
            meta.FromDisplay = NullIfWhiteSpace(msg.Sender?.DisplayName);
            meta.FromEmail = NullIfWhiteSpace(msg.Sender?.Email);
            meta.ToList = JoinRecipients(msg.Recipients, RecipientType.To);
            meta.CcList = JoinRecipients(msg.Recipients, RecipientType.Cc);
            meta.BccList = JoinRecipients(msg.Recipients, RecipientType.Bcc);
            meta.SentOnUtc = msg.SentOn?.UtcDateTime;
            meta.ReceivedOnUtc = msg.ReceivedOn?.UtcDateTime;
            meta.BodyText = Truncate(msg.BodyText);
            meta.HasAttachments = msg.Attachments.Count > 0;
            meta.AttachmentCount = msg.Attachments.Count;
            meta.MessageId = NullIfWhiteSpace(msg.Headers?.MessageId) ?? ExtractMessageId(msg.TransportMessageHeaders);
        }

        private static void PopulateEml(EmailMetadata meta, string filePath)
        {
            var message = MsgReader.Mime.Message.Load(new FileInfo(filePath));
            var headers = message.Headers;

            meta.Subject = NullIfWhiteSpace(headers?.Subject);
            meta.FromDisplay = NullIfWhiteSpace(headers?.From?.DisplayName);
            meta.FromEmail = NullIfWhiteSpace(headers?.From?.Address);
            meta.ToList = JoinAddresses(headers?.To);
            meta.CcList = JoinAddresses(headers?.Cc);
            meta.BccList = JoinAddresses(headers?.Bcc);
            meta.SentOnUtc = headers?.DateSent.UtcDateTime;
            meta.ReceivedOnUtc = headers?.DateSent.UtcDateTime;
            meta.MessageId = NullIfWhiteSpace(headers?.MessageId);
            meta.BodyText = Truncate(message.TextBody?.GetBodyAsText());
            meta.HasAttachments = message.Attachments?.Count > 0;
            meta.AttachmentCount = message.Attachments?.Count ?? 0;
        }

        private static string GetFormat(string? filePath)
        {
            var ext = Path.GetExtension(filePath ?? string.Empty) ?? string.Empty;
            if (ext.Equals(".msg", StringComparison.OrdinalIgnoreCase)) return "MSG";
            if (ext.Equals(".eml", StringComparison.OrdinalIgnoreCase)) return "EML";
            return "UNK";
        }

        private static string? JoinRecipients(
            IEnumerable<MsgReader.Outlook.Storage.Recipient>? recipients,
            RecipientType recipientType)
        {
            if (recipients == null) return null;

            return JoinValues(
                recipients
                    .Where(r => r.Type == recipientType)
                    .Select(r => string.IsNullOrWhiteSpace(r.Email) ? r.DisplayName : r.Email));
        }

        private static string? JoinAddresses(
            IEnumerable<MsgReader.Mime.Header.RfcMailAddress>? addresses)
        {
            if (addresses == null) return null;
            return JoinValues(addresses.Select(a => string.IsNullOrWhiteSpace(a.Address) ? a.DisplayName : a.Address));
        }

        private static string? JoinValues(IEnumerable<string?> values)
        {
            var joined = string.Join("; ", values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()));
            return string.IsNullOrWhiteSpace(joined) ? null : joined;
        }

        private static string? Truncate(string? value)
        {
            if (value is null) return null;
            var trimmed = value.Trim();
            if (trimmed.Length == 0) return null;
            return trimmed.Length <= MaxBodyLength ? trimmed : trimmed.Substring(0, MaxBodyLength);
        }

        private static string? NullIfWhiteSpace(string? value)
        {
            if (value is null) return null;
            var trimmed = value.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }

        private static string? ExtractMessageId(string? transportHeaders)
        {
            if (string.IsNullOrWhiteSpace(transportHeaders)) return null;

            var match = Regex.Match(
                transportHeaders,
                @"^Message-ID:\s*(.+)$",
                RegexOptions.IgnoreCase | RegexOptions.Multiline);

            return match.Success ? NullIfWhiteSpace(match.Groups[1].Value) : null;
        }

        private static string GetValidatedFilePath(string? filePath)
        {
            if (filePath is null)
                throw new ArgumentNullException(nameof(filePath));

            if (filePath.Trim().Length == 0)
                throw new ArgumentException("filePath is required.", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("Email file not found.", filePath);

            return filePath;
        }
    }
}
