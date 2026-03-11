using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
namespace Kor.Operations.Data
{
    public interface ITransmittalsStore
    {
        Task LogTransmittalAsync(Guid id, string projectNo, string subject,
                                 string driveId, string itemId, string sharePointUrl,
                                 DateTime createdUtc, string createdBy, string? appVersion,
                                 string type = "Transmittal",
                                 CancellationToken ct = default);

        Task AddRecipientsAsync(Guid transmittalId,
                                IEnumerable<(string Email, string Kind, Guid LinkId, string? PersonalShareLink)> recips,
                                CancellationToken ct = default);

        Task MarkSentAsync(Guid transmittalId, DateTime sentUtc, string sentBy, string? appVersion, CancellationToken ct = default);

        // Dashboard queries
        Task<IReadOnlyList<TransmittalSummary>> SearchSummaryAsync(
            string? text, DateTime? startUtc, DateTime? endUtc, int take = 200, CancellationToken ct = default);

        Task<IReadOnlyList<ActivityRow>> LoadActivityAsync(
            Guid transmittalId, int take = 200, CancellationToken ct = default);
    }

    public sealed class SqlTransmittalsStore : ITransmittalsStore
    {
        private readonly string _cs;

        public SqlTransmittalsStore(string connectionString)
        {
            _cs = connectionString;
        }

        public async Task LogTransmittalAsync(
            Guid id,
            string projectNo,
            string subject,
            string driveId,
            string itemId,
            string sharePointUrl,
            DateTime createdUtc,
            string createdBy,
            string? appVersion,
            string type = "Transmittal",
            CancellationToken ct = default)
        {
            const string sql = @"
INSERT INTO dbo.Transmittals
    (Id, ProjectNo, Subject, DriveId, ItemId, SharePointUrl, CreatedAt, CreatedBy, AppVersion, [Type])
VALUES
    (@Id, @ProjectNo, @Subject, @DriveId, @ItemId, @SharePointUrl, @CreatedUtc, @CreatedBy, @AppVersion, @Type);";

            await using var cn = new SqlConnection(_cs);
            await cn.OpenAsync(ct);

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@ProjectNo", (object?)projectNo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Subject", (object?)subject ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DriveId", (object?)driveId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ItemId", (object?)itemId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SharePointUrl", (object?)sharePointUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedUtc", createdUtc);
            cmd.Parameters.AddWithValue("@CreatedBy", (object?)createdBy ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AppVersion", (object?)appVersion ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Type", (object?)type ?? "Transmittal");

            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task AddRecipientsAsync(
            Guid transmittalId,
            IEnumerable<(string Email, string Kind, Guid LinkId, string? PersonalShareLink)> recips,
            CancellationToken ct = default)
        {
            const string sql = @"
INSERT INTO dbo.TransmittalRecipients
    (Id, TransmittalId, Email, Kind, LinkId, PersonalShareLink, LastActivityAt)
VALUES
    (@Id, @TransmittalId, @Email, @Kind, @LinkId, @PersonalShareLink, NULL);";

            await using var cn = new SqlConnection(_cs);
            await cn.OpenAsync(ct);

            foreach (var r in recips)
            {
                await using var cmd = new SqlCommand(sql, cn);
                cmd.Parameters.AddWithValue("@Id", Guid.NewGuid());
                cmd.Parameters.AddWithValue("@TransmittalId", transmittalId);
                cmd.Parameters.AddWithValue("@Email", r.Email ?? string.Empty);
                cmd.Parameters.AddWithValue("@Kind", r.Kind ?? string.Empty);
                cmd.Parameters.AddWithValue("@LinkId", r.LinkId);
                cmd.Parameters.AddWithValue("@PersonalShareLink", (object?)r.PersonalShareLink ?? DBNull.Value);

                await cmd.ExecuteNonQueryAsync(ct);
            }
        }

        public async Task MarkSentAsync(
            Guid transmittalId,
            DateTime sentUtc,
            string sentBy,
            string? appVersion,
            CancellationToken ct = default)
        {
            const string sql = @"
UPDATE dbo.Transmittals
   SET SentAt    = @SentAt,
       SentBy    = @SentBy,
       AppVersion = COALESCE(@AppVersion, AppVersion)
 WHERE Id = @Id;";

            await using var cn = new SqlConnection(_cs);
            await cn.OpenAsync(ct);

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Id", transmittalId);
            cmd.Parameters.AddWithValue("@SentAt", sentUtc);
            cmd.Parameters.AddWithValue("@SentBy", sentBy ?? string.Empty);
            cmd.Parameters.AddWithValue("@AppVersion", (object?)appVersion ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(ct);
        }

        // ---------------------------
        // Dashboard query methods
        // ---------------------------

        public async Task<IReadOnlyList<TransmittalSummary>> SearchSummaryAsync(
            string? text,
            DateTime? startUtc,
            DateTime? endUtc,
            int take = 200,
            CancellationToken ct = default)
        {
            const string sql = @"
WITH O AS (
    SELECT oe.TransmittalId, COUNT_BIG(*) AS OpenCount
    FROM dbo.OpenEvents oe
    GROUP BY oe.TransmittalId
),
C AS (
    SELECT ce.TransmittalId, COUNT_BIG(*) AS ClickCount
    FROM dbo.ClickEvents ce
    GROUP BY ce.TransmittalId
)
SELECT TOP (@Take)
       t.Id,
       t.ProjectNo,
       t.Subject,
       t.SharePointUrl,
       t.CreatedAt,
       t.SentAt,
       ISNULL(t.[Type], 'Transmittal') AS [Type],
       COALESCE(o.OpenCount, 0)  AS OpenCount,
       COALESCE(c.ClickCount, 0) AS ClickCount
FROM dbo.Transmittals t
LEFT JOIN O o ON o.TransmittalId = t.Id
LEFT JOIN C c ON c.TransmittalId = t.Id
WHERE (@Text IS NULL
       OR t.ProjectNo LIKE @TextLike
       OR t.Subject   LIKE @TextLike)
  AND (@Start IS NULL OR t.CreatedAt >= @Start)
  AND (@End   IS NULL OR t.CreatedAt <  @End)
ORDER BY t.CreatedAt DESC;";

            var list = new List<TransmittalSummary>(Math.Max(32, take));

            await using var cn = new SqlConnection(_cs);
            await cn.OpenAsync(ct);

            await using var cmd = new SqlCommand(sql, cn);

            string? textLike = null;
            if (!string.IsNullOrWhiteSpace(text))
                textLike = $"%{text}%";

            cmd.Parameters.AddWithValue("@Take", take);
            cmd.Parameters.AddWithValue("@Text", (object?)text ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@TextLike", (object?)textLike ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Start", (object?)startUtc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@End", (object?)endUtc ?? DBNull.Value);

            using var rd = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
            while (await rd.ReadAsync(ct))
            {
                var row = new TransmittalSummary
                {
                    Id = rd.GetGuid(0),
                    ProjectNo = rd.IsDBNull(1) ? string.Empty : rd.GetString(1),
                    Subject = rd.IsDBNull(2) ? string.Empty : rd.GetString(2),
                    SharePointUrl = rd.IsDBNull(3) ? string.Empty : rd.GetString(3),
                    CreatedAt = rd.IsDBNull(4) ? (DateTime?)null : rd.GetDateTime(4),
                    SentAt = rd.IsDBNull(5) ? (DateTime?)null : rd.GetDateTime(5),
                    Type = rd.IsDBNull(6) ? "Transmittal" : rd.GetString(6),
                    OpenCount = rd.IsDBNull(7) ? 0 : (long)rd.GetInt64(7),
                    ClickCount = rd.IsDBNull(8) ? 0 : (long)rd.GetInt64(8)
                };
                list.Add(row);
            }

            return list;
        }

        public async Task<IReadOnlyList<ActivityRow>> LoadActivityAsync(
            Guid transmittalId,
            int take = 200,
            CancellationToken ct = default)
        {
            const string sql = @"
SELECT TOP (@Take)
       'Open'  AS Kind,
       oe.OpenedAt AS OccurredAt,
       oe.RecipientEmail,
       oe.ClientIp,
       oe.UserAgent,
       CAST(NULL AS nvarchar(1024)) AS Referer
FROM dbo.OpenEvents oe
WHERE oe.TransmittalId = @Tid

UNION ALL

SELECT TOP (@Take)
       'Click' AS Kind,
       ce.ClickedAt AS OccurredAt,
       ce.RecipientEmail,
       ce.ClientIp,
       ce.UserAgent,
       ce.Referer
FROM dbo.ClickEvents ce
WHERE ce.TransmittalId = @Tid

ORDER BY OccurredAt DESC;";

            var list = new List<ActivityRow>(Math.Max(32, take));

            await using var cn = new SqlConnection(_cs);
            await cn.OpenAsync(ct);

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@Take", take);
            cmd.Parameters.AddWithValue("@Tid", transmittalId);

            using var rd = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
            while (await rd.ReadAsync(ct))
            {
                var row = new ActivityRow
                {
                    Kind = rd.IsDBNull(0) ? string.Empty : rd.GetString(0),
                    OccurredAt = rd.IsDBNull(1) ? DateTime.MinValue : rd.GetDateTime(1),
                    RecipientEmail = rd.IsDBNull(2) ? string.Empty : rd.GetString(2),
                    ClientIp = rd.IsDBNull(3) ? string.Empty : rd.GetString(3),
                    UserAgent = rd.IsDBNull(4) ? string.Empty : rd.GetString(4),
                    Referer = rd.IsDBNull(5) ? null : rd.GetString(5)
                };
                list.Add(row);
            }

            return list;
        }
    }

    // ---------- Small DTOs for dashboard ----------

    public sealed class TransmittalSummary
    {
        public Guid Id { get; set; }
        public string ProjectNo { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string SharePointUrl { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; }
        public DateTime? SentAt { get; set; }
        public long OpenCount { get; set; }
        public long ClickCount { get; set; }

        // Used by the dashboard Type column and row colouring
        public string Type { get; set; } = "Transmittal";
    }

    public sealed class ActivityRow
    {
        public string Kind { get; set; } = string.Empty;   // "Open" or "Click"
        public DateTime OccurredAt { get; set; }           // UTC
        public string RecipientEmail { get; set; } = string.Empty;
        public string ClientIp { get; set; } = string.Empty;
        public string UserAgent { get; set; } = string.Empty;
        public string? Referer { get; set; }
    }
}
