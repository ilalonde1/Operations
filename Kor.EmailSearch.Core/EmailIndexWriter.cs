#nullable enable
using System;
using System.Data;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Data;
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
    public sealed class EmailIndexWriter : IEmailIndexWriter
    {
        private sealed class ExistingEmailRow
        {
            public long EmailId { get; set; }
            public long FileSizeBytes { get; set; }
            public DateTime FileLastWriteUtc { get; set; }
            public string? FileHashSha1 { get; set; }
        }

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

            using (var cn = new SqlConnection(_connString))
            {
                await cn.OpenAsync(ct).ConfigureAwait(false);

                ExistingEmailRow? existing = null;
                using (var findCmd = new SqlCommand(
                           @"SELECT EmailId, FileSizeBytes, FileLastWriteUtc, FileHashSha1
FROM dbo.Emails
WHERE FilePath = @FilePath", cn))
                {
                    findCmd.CommandTimeout = SqlTimeouts.Batch;
                    var filePathParam = findCmd.Parameters.Add("@FilePath", SqlDbType.NVarChar, 4000);
                    filePathParam.Value = filePath ?? (object)DBNull.Value;

                    using (var reader = await findCmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(ct).ConfigureAwait(false))
                        {
                            existing = new ExistingEmailRow
                            {
                                EmailId = reader.GetInt64(0),
                                FileSizeBytes = reader.GetInt64(1),
                                FileLastWriteUtc = reader.GetDateTime(2),
                                FileHashSha1 = reader.IsDBNull(3) ? null : reader.GetString(3)
                            };
                        }
                    }
                }

                var sha1Hex =
                    existing != null &&
                    existing.FileSizeBytes == fileSize &&
                    existing.FileLastWriteUtc == lastWriteUtc &&
                    !string.IsNullOrWhiteSpace(existing.FileHashSha1)
                        ? existing.FileHashSha1!
                        : ComputeSha1Hex(filePath);

                // Let the caller (worker / add-in) handle MsgReader/MIME specifics
                var meta = await _extractor.ExtractAsync(projectNumber, filePath, ct).ConfigureAwait(false);

                if (existing != null)
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
                        updateCmd.CommandTimeout = SqlTimeouts.Batch;
                        PopulateCommonParameters(updateCmd, projectNumber, filePath!,
                            fileName, fileSize, lastWriteUtc, sha1Hex, meta, source);

                        var emailId = updateCmd.Parameters.Add("@EmailId", SqlDbType.BigInt);
                        emailId.Value = existing.EmailId;

                        await updateCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                        return existing.EmailId;
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
                        insertCmd.CommandTimeout = SqlTimeouts.Batch;
                        PopulateCommonParameters(insertCmd, projectNumber, filePath!,
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

            var projectNumberParam = cmd.Parameters.Add("@ProjectNumber", SqlDbType.NVarChar, 256);
            projectNumberParam.Value = string.IsNullOrWhiteSpace(projectNumber) ? (object)DBNull.Value : projectNumber;

            var filePathParam = cmd.Parameters.Add("@FilePath", SqlDbType.NVarChar, 4000);
            filePathParam.Value = filePath ?? (object)DBNull.Value;
            var fileNameParam = cmd.Parameters.Add("@FileName", SqlDbType.NVarChar, 500);
            fileNameParam.Value = fileName ?? (object)DBNull.Value;
            var fileSizeParam = cmd.Parameters.Add("@FileSizeBytes", SqlDbType.BigInt);
            fileSizeParam.Value = fileSize;
            var fileLastWriteUtcParam = cmd.Parameters.Add("@FileLastWriteUtc", SqlDbType.DateTime2);
            fileLastWriteUtcParam.Value = lastWriteUtc;
            var fileHashSha1Param = cmd.Parameters.Add("@FileHashSha1", SqlDbType.NVarChar, 40);
            fileHashSha1Param.Value = sha1Hex ?? (object)DBNull.Value;

            var formatParam = cmd.Parameters.Add("@Format", SqlDbType.NVarChar, 32);
            formatParam.Value = fmt ?? (object)DBNull.Value;

            var messageIdParam = cmd.Parameters.Add("@MessageId", SqlDbType.NVarChar, 500);
            messageIdParam.Value = meta.MessageId ?? (object)DBNull.Value;
            var subjectParam = cmd.Parameters.Add("@Subject", SqlDbType.NVarChar, 4000);
            subjectParam.Value = meta.Subject ?? (object)DBNull.Value;
            var fromDisplayParam = cmd.Parameters.Add("@FromDisplay", SqlDbType.NVarChar, 500);
            fromDisplayParam.Value = meta.FromDisplay ?? (object)DBNull.Value;
            var fromEmailParam = cmd.Parameters.Add("@FromEmail", SqlDbType.NVarChar, 256);
            fromEmailParam.Value = meta.FromEmail ?? (object)DBNull.Value;
            var toListParam = cmd.Parameters.Add("@ToList", SqlDbType.NVarChar, -1);
            toListParam.Value = meta.ToList ?? (object)DBNull.Value;
            var ccListParam = cmd.Parameters.Add("@CcList", SqlDbType.NVarChar, -1);
            ccListParam.Value = meta.CcList ?? (object)DBNull.Value;
            var bccListParam = cmd.Parameters.Add("@BccList", SqlDbType.NVarChar, -1);
            bccListParam.Value = meta.BccList ?? (object)DBNull.Value;

            // Prefer SentOnUtc; if it's missing, fall back to ReceivedOnUtc so
            // new items sort correctly and survive date filters.
            var effectiveSent =
                meta.SentOnUtc ?? meta.ReceivedOnUtc;

            var sentOnUtcParam = cmd.Parameters.Add("@SentOnUtc", SqlDbType.DateTime2);
            sentOnUtcParam.Value = effectiveSent ?? (object)DBNull.Value;

            // For ReceivedOnUtc, prefer the real ReceivedOnUtc if present,
            // otherwise fall back to whatever we used as "sent".
            var effectiveReceived =
                meta.ReceivedOnUtc ?? effectiveSent;

            var receivedOnUtcParam = cmd.Parameters.Add("@ReceivedOnUtc", SqlDbType.DateTime2);
            receivedOnUtcParam.Value = effectiveReceived ?? (object)DBNull.Value;


            var bodyTextParam = cmd.Parameters.Add("@BodyText", SqlDbType.NVarChar, -1);
            bodyTextParam.Value = meta.BodyText ?? (object)DBNull.Value;

            var hasAttachmentsParam = cmd.Parameters.Add("@HasAttachments", SqlDbType.Bit);
            hasAttachmentsParam.Value = meta.HasAttachments;
            var attachmentCountParam = cmd.Parameters.Add("@AttachmentCount", SqlDbType.Int);
            attachmentCountParam.Value = meta.AttachmentCount;

            var sourceParam = cmd.Parameters.Add("@Source", SqlDbType.NVarChar, 256);
            sourceParam.Value = string.IsNullOrWhiteSpace(source) ? "OUTLOOK" : source;
        }
    }
}
