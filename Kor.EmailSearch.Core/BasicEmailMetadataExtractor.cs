using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.EmailSearch.Core
{
    /// <summary>
    /// Minimal, safe implementation of IEmailMetadataExtractor.
    /// 
    /// For now:
    /// - Infers Subject from the file name (without extension).
    /// - Leaves address fields and body empty/null.
    /// 
    /// This is a placeholder so EmailIndexWriter can be used immediately
    /// without pulling in MsgReader or other heavy dependencies.
    /// Later we can replace this with a MsgReader-based implementation.
    /// </summary>
    public sealed class BasicEmailMetadataExtractor : IEmailMetadataExtractor
    {
        public Task<EmailMetadata> ExtractAsync(
            string projectNumber,
            string filePath,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("filePath is required.", nameof(filePath));

            var fi = new FileInfo(filePath);
            if (!fi.Exists)
                throw new FileNotFoundException("Email file not found.", filePath);

            var nameWithoutExt = Path.GetFileNameWithoutExtension(fi.Name) ?? string.Empty;
            var ext = Path.GetExtension(fi.Name) ?? string.Empty;

            string format;
            if (ext.Equals(".msg", StringComparison.OrdinalIgnoreCase))
                format = "MSG";
            else if (ext.Equals(".eml", StringComparison.OrdinalIgnoreCase))
                format = "EML";
            else
                format = "UNK";

            var meta = new EmailMetadata
            {
                ProjectNumber = projectNumber ?? string.Empty,
                FileName = fi.Name,
                Format = format,

                // For now we only set a basic subject
                Subject = nameWithoutExt,

                // Everything else left null / default; the writer will map these to NULL
                MessageId = null,
                FromDisplay = null,
                FromEmail = null,
                ToList = null,
                CcList = null,
                BccList = null,
                SentOnUtc = null,
                ReceivedOnUtc = null,
                BodyText = null,
                HasAttachments = false,
                AttachmentCount = 0
            };

            return Task.FromResult(meta);
        }
    }
}
