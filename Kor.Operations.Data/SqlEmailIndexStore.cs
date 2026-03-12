#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
namespace Kor.Operations.Data
{
    public sealed class SqlEmailIndexStore
    {
        private readonly string _connString;

        public SqlEmailIndexStore(string connString)
        {
            _connString = connString ?? throw new ArgumentNullException(nameof(connString));
        }

        /// <summary>
        /// Inserts a single email file (MSG or EML) into KorEmailIndex.dbo.Emails.
        /// Assumes the file has already been written to disk at filePath.
        /// </summary>
        public async Task InsertEmailAsync(
            string projectNumber,
            string filePath,
            string subject,
            string fromEmail,
            DateTime? sentOnUtc,
            int attachmentCount,
            bool hasAttachments,
            string? fromDisplay = null,
            string? toList = null,
            string? ccList = null,
            string? bccList = null,
            string? bodyText = null,
            DateTime? receivedOnUtc = null,
            string? messageId = null,
            bool isCorrupt = false,
            CancellationToken ct = default)
        {
            await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                if (string.IsNullOrWhiteSpace(filePath))
                    throw new ArgumentNullException(nameof(filePath));

                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists)
                    throw new FileNotFoundException("Filed email not found at expected path.", filePath);

                string fileName = fileInfo.Name;
                long fileSize = fileInfo.Length;
                DateTime fileLastWriteUtc = fileInfo.LastWriteTimeUtc;

                string extension = fileInfo.Extension;
                string format;
                if (extension.Equals(".eml", StringComparison.OrdinalIgnoreCase))
                    format = "EML";
                else if (extension.Equals(".msg", StringComparison.OrdinalIgnoreCase))
                    format = "MSG";
                else
                    format = "MSG";

                string sha1 = ComputeSha1(filePath);

                const string findExistingSql = @"
SELECT TOP 1 EmailId
FROM dbo.Emails
WHERE FileHashSha1 = @FileHashSha1
  AND FilePath = @FilePath;";

                const string sql = @"
INSERT INTO dbo.Emails
(
    ProjectNumber,
    FilePath,
    FileName,
    FileSizeBytes,
    FileLastWriteUtc,
    FileHashSha1,
    Format,
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
    Source,
    IsCorrupt,
    MessageId
)
VALUES
(
    @ProjectNumber,
    @FilePath,
    @FileName,
    @FileSizeBytes,
    @FileLastWriteUtc,
    @FileHashSha1,
    @Format,
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
    @Source,
    @IsCorrupt,
    @MessageId
);";

                await using var cn = new SqlConnection(_connString);
                await cn.OpenAsync(innerCt);

                await using (var existingCmd = new SqlCommand(findExistingSql, cn))
                {
                    existingCmd.Parameters.AddWithValue("@FileHashSha1", sha1);
                    existingCmd.Parameters.AddWithValue("@FilePath", filePath);
                    var existingId = await existingCmd.ExecuteScalarAsync(innerCt);
                    if (existingId != null && existingId != DBNull.Value)
                        return;
                }

                await using var cmd = new SqlCommand(sql, cn);

                cmd.Parameters.AddWithValue("@ProjectNumber", (object?)projectNumber ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@FilePath", filePath);
                cmd.Parameters.AddWithValue("@FileName", fileName);
                cmd.Parameters.AddWithValue("@FileSizeBytes", fileSize);
                cmd.Parameters.AddWithValue("@FileLastWriteUtc", fileLastWriteUtc);
                cmd.Parameters.AddWithValue("@FileHashSha1", sha1);
                cmd.Parameters.AddWithValue("@Format", format);

                cmd.Parameters.AddWithValue("@Subject", (object?)subject ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@FromDisplay", (object?)fromDisplay ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@FromEmail", (object?)fromEmail ?? DBNull.Value);

                cmd.Parameters.AddWithValue("@ToList", (object?)toList ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CcList", (object?)ccList ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@BccList", (object?)bccList ?? DBNull.Value);

                cmd.Parameters.AddWithValue("@SentOnUtc", (object?)sentOnUtc ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ReceivedOnUtc", (object?)receivedOnUtc ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@BodyText", (object?)bodyText ?? DBNull.Value);

                cmd.Parameters.AddWithValue("@HasAttachments", hasAttachments);
                cmd.Parameters.AddWithValue("@AttachmentCount", attachmentCount);

                cmd.Parameters.AddWithValue("@Source", "VSTO");
                cmd.Parameters.AddWithValue("@IsCorrupt", isCorrupt);
                cmd.Parameters.AddWithValue("@MessageId", (object?)messageId ?? DBNull.Value);

                await cmd.ExecuteNonQueryAsync(innerCt);
            }, ct);
        }

        private static string ComputeSha1(string path)
        {
            using var sha1 = SHA1.Create();
            using var stream = File.OpenRead(path);
            byte[] hash = sha1.ComputeHash(stream);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
