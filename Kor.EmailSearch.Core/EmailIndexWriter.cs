using System;
using System.Data;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.EmailSearch.Core
{
    /// <summary>
    /// Minimal metadata needed to populate KorEmailIndex.dbo.Emails
    /// for either .msg or .eml files.
    /// </summary>
    public sealed class EmailMetadata
    {
        public string ProjectNumber { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// "MSG" or "EML" (if empty, will be inferred from file extension).
        /// </summary>
        public string Format { get; set; } = string.Empty;

        public string? MessageId { get; set; }
        public string? Subject { get; set; }
        public string? FromDisplay { get; set; }
        public string? FromEmail { get; set; }
        public string? ToList { get; set; }
        public string? CcList { get; set; }
        public string? BccList { get; set; }

        public DateTime? SentOnUtc { get; set; }
        public DateTime? ReceivedOnUtc { get; set; }

        public string? BodyText { get; set; }

        public bool HasAttachments { get; set; }
        public int AttachmentCount { get; set; }
    }


    /// <summary>
    /// Abstraction over MsgReader/MIME parsing so EmailIndexWriter
    /// does not depend on any specific email library.
    /// </summary>
    public interface IEmailMetadataExtractor
    {
        /// <summary>
        /// Parse the given file and return metadata.
        /// Implementations should handle both .msg and .eml based on the file path.
        /// </summary>
        Task<EmailMetadata> ExtractAsync(
            string projectNumber,
            string filePath,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Central writer that upserts rows into KorEmailIndex.dbo.Emails
    /// whenever an email is filed or changed on disk.
    /// </summary>
    public sealed class EmailIndexWriter
    {
        private readonly string _connString;
        private readonly IEmailMetadataExtractor _extractor;

        public EmailIndexWriter(string connString, IEmailMetadataExtractor extractor)
        {
            if (string.IsNullOrWhiteSpace(connString))
                throw new ArgumentNullException(nameof(connString));
            if (extractor == null)
                throw new ArgumentNullException(nameof(extractor));

            _connString = connString;
            _extractor = extractor;
        }

        /// <summary>
        /// Upsert an email into dbo.Emails based on its file path.
        /// - Computes SHA1 for FileHashSha1 (content-based).
        /// - Uses FilePath to find existing row.
        /// - Updates if exists, inserts if not.
        /// Returns EmailId (bigint).
        /// </summary>
        public async Task<long> UpsertEmailAsync(
            string projectNumber,
            string filePath,
            string source = "OUTLOOK",
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(projectNumber))
                throw new ArgumentException("Project number is required.", nameof(projectNumber));
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path is required.", nameof(filePath));

            var fi = new FileInfo(filePath);
            if (!fi.Exists)
                throw new FileNotFoundException("Email file not found.", filePath);

            var fileSize = fi.Length;
            var lastWriteUtc = fi.LastWriteTimeUtc;
            var fileName = fi.Name;

            // Hash the file contents so FileHashSha1 stays in sync with your one-time index
            var sha1Hex = ComputeSha1Hex(filePath);

            // Let the caller (worker / add-in) handle MsgReader/MIME specifics
            var meta = await _extractor.ExtractAsync(projectNumber, filePath, ct).ConfigureAwait(false);

            using (var cn = new SqlConnection(_connString))
            {
                await cn.OpenAsync(ct).ConfigureAwait(false);

                // Try to locate an existing row by exact FilePath
                long? existingId = null;
                using (var findCmd = new SqlCommand(
                           "SELECT EmailId FROM dbo.Emails WHERE FilePath = @FilePath", cn))
                {
                    findCmd.Parameters.AddWithValue("@FilePath", filePath);
                    var obj = await findCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                    if (obj != null && obj != DBNull.Value)
                        existingId = Convert.ToInt64(obj);
                }

                if (existingId.HasValue)
                {
                    // UPDATE existing row
                    using (var updateCmd = new SqlCommand(@"
UPDATE dbo.Emails
SET ProjectNumber     = @ProjectNumber,
    FileName          = @FileName,
    FileSizeBytes     = @FileSizeBytes,
    FileLastWriteUtc  = @FileLastWriteUtc,
    FileHashSha1      = @FileHashSha1,
    Format            = @Format,
    MessageId         = @MessageId,
    Subject           = @Subject,
    FromDisplay       = @FromDisplay,
    FromEmail         = @FromEmail,
    ToList            = @ToList,
    CcList            = @CcList,
    BccList           = @BccList,
    SentOnUtc         = @SentOnUtc,
    ReceivedOnUtc     = @ReceivedOnUtc,
    BodyText          = @BodyText,
    HasAttachments    = @HasAttachments,
    AttachmentCount   = @AttachmentCount,
    IndexedAtUtc      = SYSUTCDATETIME(),
    Source            = @Source
WHERE EmailId = @EmailId;", cn))
                    {
                        PopulateCommonParameters(updateCmd, projectNumber, filePath,
                            fileName, fileSize, lastWriteUtc, sha1Hex, meta, source);

                        updateCmd.Parameters.AddWithValue("@EmailId", existingId.Value);

                        await updateCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                        return existingId.Value;
                    }
                }
                else
                {
                    // INSERT new row
                    using (var insertCmd = new SqlCommand(@"
INSERT INTO dbo.Emails
    (ProjectId,
     ProjectNumber,
     FilePath,
     FileName,
     FileSizeBytes,
     FileLastWriteUtc,
     FileHashSha1,
     Format,
     MessageId,
     Subject,
     FromDisplay,
     FromEmail,
     ToList,
     CcList,
     BccList,
     SentOnUtc,
     ReceivedOnUtc,
     BodyText,
     HasAttachments,
     AttachmentCount,
     IndexedAtUtc,
     Source,
     IsCorrupt)
VALUES
    (NULL,
     @ProjectNumber,
     @FilePath,
     @FileName,
     @FileSizeBytes,
     @FileLastWriteUtc,
     @FileHashSha1,
     @Format,
     @MessageId,
     @Subject,
     @FromDisplay,
     @FromEmail,
     @ToList,
     @CcList,
     @BccList,
     @SentOnUtc,
     @ReceivedOnUtc,
     @BodyText,
     @HasAttachments,
     @AttachmentCount,
     SYSUTCDATETIME(),
     @Source,
     0);
SELECT CAST(SCOPE_IDENTITY() AS bigint);", cn))
                    {
                        PopulateCommonParameters(insertCmd, projectNumber, filePath,
                            fileName, fileSize, lastWriteUtc, sha1Hex, meta, source);

                        var newIdObj = await insertCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                        return Convert.ToInt64(newIdObj);
                    }
                }
            }
        }

        // -----------------------------
        // Helpers
        // -----------------------------
        private static string ComputeSha1Hex(string filePath)
        {
            using (var stream = File.OpenRead(filePath))
            using (var sha1 = SHA1.Create())
            {
                var hash = sha1.ComputeHash(stream);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static void PopulateCommonParameters(
            SqlCommand cmd,
            string projectNumber,
            string filePath,
            string fileName,
            long fileSize,
            DateTime lastWriteUtc,
            string sha1Hex,
            EmailMetadata meta,
            string source)
        {
            if (meta == null)
                throw new ArgumentNullException(nameof(meta));

            // Ensure format is never NULL (column is NOT NULL)
            var fmt = meta.Format;
            if (string.IsNullOrWhiteSpace(fmt))
            {
                var ext = Path.GetExtension(fileName) ?? string.Empty;
                if (ext.Equals(".msg", StringComparison.OrdinalIgnoreCase))
                    fmt = "MSG";
                else if (ext.Equals(".eml", StringComparison.OrdinalIgnoreCase))
                    fmt = "EML";
                else
                    fmt = "UNK";
            }

            cmd.Parameters.AddWithValue("@ProjectNumber",
                string.IsNullOrWhiteSpace(projectNumber) ? (object)DBNull.Value : projectNumber);

            cmd.Parameters.AddWithValue("@FilePath", filePath);
            cmd.Parameters.AddWithValue("@FileName", fileName);
            cmd.Parameters.AddWithValue("@FileSizeBytes", fileSize);
            cmd.Parameters.AddWithValue("@FileLastWriteUtc", lastWriteUtc);
            cmd.Parameters.AddWithValue("@FileHashSha1", sha1Hex);

            cmd.Parameters.AddWithValue("@Format", fmt);

            cmd.Parameters.AddWithValue("@MessageId",
                (object?)meta.MessageId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Subject",
                (object?)meta.Subject ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@FromDisplay",
                (object?)meta.FromDisplay ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@FromEmail",
                (object?)meta.FromEmail ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToList",
                (object?)meta.ToList ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CcList",
                (object?)meta.CcList ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@BccList",
                (object?)meta.BccList ?? DBNull.Value);

            // Prefer SentOnUtc; if it's missing, fall back to ReceivedOnUtc so
            // new items sort correctly and survive date filters.
            var effectiveSent =
                meta.SentOnUtc ?? meta.ReceivedOnUtc;

            cmd.Parameters.AddWithValue("@SentOnUtc",
                (object?)effectiveSent ?? DBNull.Value);

            // For ReceivedOnUtc, prefer the real ReceivedOnUtc if present,
            // otherwise fall back to whatever we used as "sent".
            var effectiveReceived =
                meta.ReceivedOnUtc ?? effectiveSent;

            cmd.Parameters.AddWithValue("@ReceivedOnUtc",
                (object?)effectiveReceived ?? DBNull.Value);


            cmd.Parameters.AddWithValue("@BodyText",
                (object?)meta.BodyText ?? DBNull.Value);

            cmd.Parameters.AddWithValue("@HasAttachments", meta.HasAttachments);
            cmd.Parameters.AddWithValue("@AttachmentCount", meta.AttachmentCount);

            cmd.Parameters.AddWithValue("@Source",
                string.IsNullOrWhiteSpace(source) ? "OUTLOOK" : source);
        }
    }
}
