#nullable enable
#pragma warning disable SA1649
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
namespace Kor.Operations.Data
{
    public interface ITransmittalsStore
    {
        Task LogTransmittalWithRecipientsAsync(Guid id, string projectNo, string subject,
                                               string driveId, string itemId, string sharePointUrl,
                                               DateTime createdUtc, string createdBy, string? appVersion,
                                               IEnumerable<(string Email, string Kind, Guid LinkId, string? PersonalShareLink)> recips,
                                               string type = "Transmittal",
                                               CancellationToken ct = default);

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
            string? text,
            DateTime? startUtc,
            DateTime? endUtc,
            string? typeFilter = null,
            bool includeSharePointUrlInSearch = false,
            int take = 200,
            CancellationToken ct = default);

        Task<IReadOnlyList<ActivityRow>> LoadActivityAsync(
            Guid transmittalId, int take = 200, CancellationToken ct = default);

        Task<IReadOnlyList<string>> SearchHintsAsync(
            string text,
            int projectTake = 30,
            int subjectTake = 20,
            CancellationToken ct = default);
    }

    public sealed class SqlTransmittalsStore : ITransmittalsStore
    {
        private readonly string _cs;
        private readonly Func<CancellationToken, Task<DbConnection>> _openConnectionAsync;

        public SqlTransmittalsStore(string connectionString)
        {
            _cs = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _openConnectionAsync = async ct =>
            {
                var cn = new SqlConnection(_cs);
                await cn.OpenAsync(ct);
                return cn;
            };
        }

        internal SqlTransmittalsStore(Func<CancellationToken, Task<DbConnection>> openConnectionAsync)
        {
            _cs = string.Empty;
            _openConnectionAsync = openConnectionAsync ?? throw new ArgumentNullException(nameof(openConnectionAsync));
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
            await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                await using var cn = await _openConnectionAsync(innerCt);
                await InsertTransmittalAsync(
                    cn,
                    transaction: null,
                    id,
                    projectNo,
                    subject,
                    driveId,
                    itemId,
                    sharePointUrl,
                    createdUtc,
                    createdBy,
                    appVersion,
                    type,
                    innerCt);
            }, ct);
        }

        public async Task LogTransmittalWithRecipientsAsync(
            Guid id,
            string projectNo,
            string subject,
            string driveId,
            string itemId,
            string sharePointUrl,
            DateTime createdUtc,
            string createdBy,
            string? appVersion,
            IEnumerable<(string Email, string Kind, Guid LinkId, string? PersonalShareLink)> recips,
            string type = "Transmittal",
            CancellationToken ct = default)
        {
            await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                await using var cn = await _openConnectionAsync(innerCt);
                await using var tx = await cn.BeginTransactionAsync(innerCt);

                try
                {
                    await InsertTransmittalAsync(
                        cn,
                        tx,
                        id,
                        projectNo,
                        subject,
                        driveId,
                        itemId,
                        sharePointUrl,
                        createdUtc,
                        createdBy,
                        appVersion,
                        type,
                        innerCt);
                    await InsertRecipientsAsync(cn, tx, transmittalId: id, recips, innerCt);
                    await tx.CommitAsync(innerCt);
                }
                catch
                {
                    await tx.RollbackAsync(CancellationToken.None);
                    throw;
                }
            }, ct);
        }

        public async Task AddRecipientsAsync(
            Guid transmittalId,
            IEnumerable<(string Email, string Kind, Guid LinkId, string? PersonalShareLink)> recips,
            CancellationToken ct = default)
        {
            await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                await using var cn = await _openConnectionAsync(innerCt);
                await InsertRecipientsAsync(cn, transaction: null, transmittalId, recips, innerCt);
            }, ct);
        }

        public async Task MarkSentAsync(
            Guid transmittalId,
            DateTime sentUtc,
            string sentBy,
            string? appVersion,
            CancellationToken ct = default)
        {
            await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = @"
UPDATE dbo.Transmittals
   SET SentAt    = @SentAt,
       SentBy    = @SentBy,
       AppVersion = COALESCE(@AppVersion, AppVersion)
 WHERE Id = @Id;";

                await using var cn = await _openConnectionAsync(innerCt);

                await using var cmd = cn.CreateCommand();
                cmd.CommandText = sql;
                cmd.CommandTimeout = SqlTimeouts.Batch;
                AddParameter(cmd, "@Id", transmittalId);
                AddParameter(cmd, "@SentAt", sentUtc);
                AddParameter(cmd, "@SentBy", sentBy ?? string.Empty);
                AddParameter(cmd, "@AppVersion", appVersion);

                await cmd.ExecuteNonQueryAsync(innerCt);
            }, ct);
        }

        // ---------------------------
        // Dashboard query methods
        // ---------------------------

        public async Task<IReadOnlyList<TransmittalSummary>> SearchSummaryAsync(
            string? text,
            DateTime? startUtc,
            DateTime? endUtc,
            string? typeFilter = null,
            bool includeSharePointUrlInSearch = false,
            int take = 200,
            CancellationToken ct = default)
        {
            return await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
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
       OR t.Subject   LIKE @TextLike
       OR (@IncludeSharePointUrlInSearch = 1 AND t.SharePointUrl LIKE @TextLike))
  AND (@Start IS NULL OR t.CreatedAt >= @Start)
  AND (@End   IS NULL OR t.CreatedAt <  @End)
  AND (@TypeFilter IS NULL OR ISNULL(t.[Type], 'Transmittal') = @TypeFilter)
ORDER BY t.CreatedAt DESC;";

                var list = new List<TransmittalSummary>(Math.Max(32, take));

                await using var cn = await _openConnectionAsync(innerCt);

                await using var cmd = cn.CreateCommand();
                cmd.CommandText = sql;
                cmd.CommandTimeout = SqlTimeouts.UiFacing;

                string? textLike = null;
                if (!string.IsNullOrWhiteSpace(text))
                    textLike = $"%{text}%";

                AddParameter(cmd, "@Take", take);
                AddParameter(cmd, "@Text", text);
                AddParameter(cmd, "@TextLike", textLike);
                AddParameter(cmd, "@Start", startUtc);
                AddParameter(cmd, "@End", endUtc);
                AddParameter(cmd, "@TypeFilter", typeFilter);
                AddParameter(cmd, "@IncludeSharePointUrlInSearch", includeSharePointUrlInSearch ? 1 : 0);

                using var rd = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, innerCt);
                while (await rd.ReadAsync(innerCt))
                {
                    var row = new TransmittalSummary
                    {
                        Id = rd.GetGuid(0),
                        ProjectNo = rd.GetStringOrEmpty(1),
                        Subject = rd.GetStringOrEmpty(2),
                        SharePointUrl = rd.GetStringOrEmpty(3),
                        CreatedAt = rd.GetDateTimeOrNull(4),
                        SentAt = rd.GetDateTimeOrNull(5),
                        Type = rd.IsDBNull(6) ? "Transmittal" : rd.GetString(6),
                        OpenCount = rd.GetInt64OrDefault(7),
                        ClickCount = rd.GetInt64OrDefault(8)
                    };
                    list.Add(row);
                }

                return (IReadOnlyList<TransmittalSummary>)list;
            }, ct);
        }

        public async Task<IReadOnlyList<string>> SearchHintsAsync(
            string text,
            int projectTake = 30,
            int subjectTake = 20,
            CancellationToken ct = default)
        {
            return await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = @"
;WITH P AS (
    SELECT DISTINCT TOP (@ProjectTake) ProjectNo
    FROM dbo.Transmittals
    WHERE ProjectNo LIKE @TextLike OR SharePointUrl LIKE @TextLike
    ORDER BY ProjectNo
),
S AS (
    SELECT DISTINCT TOP (@SubjectTake) Subject
    FROM dbo.Transmittals
    WHERE Subject LIKE @TextLike
    ORDER BY Subject
)
SELECT ProjectNo AS Val, 1 AS Ord FROM P
UNION ALL
SELECT Subject   AS Val, 2 AS Ord FROM S
ORDER BY Ord, Val;";

                var list = new List<string>(projectTake + subjectTake);
                await using var cn = await _openConnectionAsync(innerCt);
                await using var cmd = cn.CreateCommand();
                cmd.CommandText = sql;
                cmd.CommandTimeout = SqlTimeouts.UiFacing;
                AddParameter(cmd, "@ProjectTake", projectTake);
                AddParameter(cmd, "@SubjectTake", subjectTake);
                AddParameter(cmd, "@TextLike", $"%{text}%");

                using var rd = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, innerCt);
                while (await rd.ReadAsync(innerCt))
                    list.Add(rd.GetStringOrEmpty(0));

                return (IReadOnlyList<string>)list;
            }, ct);
        }

        public async Task<IReadOnlyList<ActivityRow>> LoadActivityAsync(
            Guid transmittalId,
            int take = 200,
            CancellationToken ct = default)
        {
            return await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
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

                await using var cn = await _openConnectionAsync(innerCt);

                await using var cmd = cn.CreateCommand();
                cmd.CommandText = sql;
                cmd.CommandTimeout = SqlTimeouts.UiFacing;
                AddParameter(cmd, "@Take", take);
                AddParameter(cmd, "@Tid", transmittalId);

                using var rd = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, innerCt);
                while (await rd.ReadAsync(innerCt))
                {
                    var row = new ActivityRow
                    {
                        Kind = rd.GetStringOrEmpty(0),
                        OccurredAt = rd.IsDBNull(1) ? DateTime.MinValue : rd.GetDateTime(1),
                        RecipientEmail = rd.GetStringOrEmpty(2),
                        ClientIp = rd.GetStringOrEmpty(3),
                        UserAgent = rd.GetStringOrEmpty(4),
                        Referer = rd.GetStringOrNull(5)
                    };
                    list.Add(row);
                }

                return (IReadOnlyList<ActivityRow>)list;
            }, ct);
        }

        private static void AddParameter(DbCommand cmd, string name, object? value)
        {
            var parameter = cmd.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(parameter);
        }

        private static async Task InsertTransmittalAsync(
            DbConnection cn,
            DbTransaction? transaction,
            Guid id,
            string projectNo,
            string subject,
            string driveId,
            string itemId,
            string sharePointUrl,
            DateTime createdUtc,
            string createdBy,
            string? appVersion,
            string type,
            CancellationToken ct)
        {
            const string sql = @"
INSERT INTO dbo.Transmittals
    (Id, ProjectNo, Subject, DriveId, ItemId, SharePointUrl, CreatedAt, CreatedBy, AppVersion, [Type])
VALUES
    (@Id, @ProjectNo, @Subject, @DriveId, @ItemId, @SharePointUrl, @CreatedUtc, @CreatedBy, @AppVersion, @Type);";

            await using var cmd = cn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = sql;
            cmd.CommandTimeout = SqlTimeouts.Batch;
            AddParameter(cmd, "@Id", id);
            AddParameter(cmd, "@ProjectNo", projectNo);
            AddParameter(cmd, "@Subject", subject);
            AddParameter(cmd, "@DriveId", driveId);
            AddParameter(cmd, "@ItemId", itemId);
            AddParameter(cmd, "@SharePointUrl", sharePointUrl);
            AddParameter(cmd, "@CreatedUtc", createdUtc);
            AddParameter(cmd, "@CreatedBy", createdBy);
            AddParameter(cmd, "@AppVersion", appVersion);
            AddParameter(cmd, "@Type", type ?? "Transmittal");

            await cmd.ExecuteNonQueryAsync(ct);
        }

        private static async Task InsertRecipientsAsync(
            DbConnection cn,
            DbTransaction? transaction,
            Guid transmittalId,
            IEnumerable<(string Email, string Kind, Guid LinkId, string? PersonalShareLink)> recips,
            CancellationToken ct)
        {
            const string sql = @"
INSERT INTO dbo.TransmittalRecipients
    (Id, TransmittalId, Email, Kind, LinkId, PersonalShareLink, LastActivityAt)
VALUES
    (@Id, @TransmittalId, @Email, @Kind, @LinkId, @PersonalShareLink, NULL);";

            foreach (var r in recips)
            {
                await using var cmd = cn.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = sql;
                cmd.CommandTimeout = SqlTimeouts.Batch;
                AddParameter(cmd, "@Id", Guid.NewGuid());
                AddParameter(cmd, "@TransmittalId", transmittalId);
                AddParameter(cmd, "@Email", r.Email ?? string.Empty);
                AddParameter(cmd, "@Kind", r.Kind ?? string.Empty);
                AddParameter(cmd, "@LinkId", r.LinkId);
                AddParameter(cmd, "@PersonalShareLink", r.PersonalShareLink);

                await cmd.ExecuteNonQueryAsync(ct);
            }
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
