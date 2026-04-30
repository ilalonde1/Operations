#nullable enable
using System;
using System.Data;
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
        /// Inserts a single email file (MSG or EML) into KorEmailIndex.dbo.Emails
        /// via dbo.UpsertEmail. Idempotent on (FileHashSha1, FilePath) — duplicate
        /// calls are silently deduped server-side. Returns true if a new row was
        /// inserted, false if the email was already present.
        /// </summary>
        public async Task<bool> InsertEmailAsync(
            string projectNumber,
            string filePath,
            string subject,
            string fromEmail,
            DateTime? sentOnUtc,
            int attachmentCount,
            bool hasAttachments,
            string source,
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
            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("source is required and must match a known writer label (e.g. WPF-PICKER, VSTO, BACKFILL).", nameof(source));

            return await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
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
                string format = extension.Equals(".eml", StringComparison.OrdinalIgnoreCase) ? "EML" : "MSG";

                string sha1 = ComputeSha1(filePath);

                await using var cn = new SqlConnection(_connString);
                await cn.OpenAsync(innerCt);

                await using var cmd = new SqlCommand("dbo.UpsertEmail", cn)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = SqlTimeouts.Batch
                };

                cmd.Parameters.Add("@ProjectNumber", SqlDbType.VarChar, 32).Value = (object?)projectNumber ?? DBNull.Value;
                cmd.Parameters.Add("@FilePath", SqlDbType.NVarChar, 1024).Value = filePath;
                cmd.Parameters.Add("@FileName", SqlDbType.NVarChar, 260).Value = fileName;
                cmd.Parameters.Add("@FileSizeBytes", SqlDbType.BigInt).Value = fileSize;
                cmd.Parameters.Add("@FileLastWriteUtc", SqlDbType.DateTime2).Value = fileLastWriteUtc;
                cmd.Parameters.Add("@FileHashSha1", SqlDbType.Char, 40).Value = sha1;
                cmd.Parameters.Add("@Format", SqlDbType.VarChar, 8).Value = format;
                cmd.Parameters.Add("@MessageId", SqlDbType.NVarChar, 512).Value = (object?)messageId ?? DBNull.Value;
                cmd.Parameters.Add("@Subject", SqlDbType.NVarChar, 512).Value = (object?)subject ?? DBNull.Value;
                cmd.Parameters.Add("@FromDisplay", SqlDbType.NVarChar, 256).Value = (object?)fromDisplay ?? DBNull.Value;
                cmd.Parameters.Add("@FromEmail", SqlDbType.NVarChar, 320).Value = (object?)fromEmail ?? DBNull.Value;
                cmd.Parameters.Add("@ToList", SqlDbType.NVarChar, -1).Value = (object?)toList ?? DBNull.Value;
                cmd.Parameters.Add("@CcList", SqlDbType.NVarChar, -1).Value = (object?)ccList ?? DBNull.Value;
                cmd.Parameters.Add("@BccList", SqlDbType.NVarChar, -1).Value = (object?)bccList ?? DBNull.Value;
                cmd.Parameters.Add("@SentOnUtc", SqlDbType.DateTime2).Value = (object?)sentOnUtc ?? DBNull.Value;
                cmd.Parameters.Add("@ReceivedOnUtc", SqlDbType.DateTime2).Value = (object?)receivedOnUtc ?? DBNull.Value;
                cmd.Parameters.Add("@BodyText", SqlDbType.NVarChar, -1).Value = (object?)bodyText ?? DBNull.Value;
                cmd.Parameters.Add("@HasAttachments", SqlDbType.Bit).Value = hasAttachments;
                cmd.Parameters.Add("@AttachmentCount", SqlDbType.Int).Value = attachmentCount;
                cmd.Parameters.Add("@Source", SqlDbType.VarChar, 32).Value = source;
                cmd.Parameters.Add("@IsCorrupt", SqlDbType.Bit).Value = isCorrupt;

                var rv = new SqlParameter("@__Return", SqlDbType.Int) { Direction = ParameterDirection.ReturnValue };
                cmd.Parameters.Add(rv);

                await cmd.ExecuteNonQueryAsync(innerCt);

                int rowsAffected = rv.Value is int n ? n : 0;
                return rowsAffected == 1;
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
