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
    public sealed class WorkloadMeeting
    {
        public Guid Id { get; set; }
        public DateTime MeetingDate { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
    }

    public sealed class WorkloadMeetingProject
    {
        public Guid Id { get; set; }
        public Guid MeetingId { get; set; }
        public string Wbs1 { get; set; } = string.Empty;
        public int Priority { get; set; }
        public string? Notes { get; set; }
    }

    public interface IWorkloadMeetingStore
    {
        Task EnsureTablesAsync(CancellationToken ct = default);
        Task<IReadOnlyList<WorkloadMeeting>> GetAllMeetingsAsync(CancellationToken ct = default);
        Task<WorkloadMeeting> CreateMeetingAsync(DateTime meetingDate, string? createdBy, CancellationToken ct = default);
        /// <summary>Writes the meeting-level notes. Returns false, having written
        /// nothing, when <paramref name="meetingId"/> is no longer the latest meeting.</summary>
        Task<bool> SaveMeetingNotesAsync(Guid meetingId, string? notes, CancellationToken ct = default);
        Task<IReadOnlyList<WorkloadMeetingProject>> GetProjectsForMeetingAsync(Guid meetingId, CancellationToken ct = default);
        /// <summary>Sets, or when <paramref name="priority"/> is 0 clears, a project's meeting
        /// priority. Returns false, having written nothing, when <paramref name="meetingId"/>
        /// is no longer the latest meeting.</summary>
        Task<bool> UpsertProjectPriorityAsync(Guid meetingId, string wbs1, int priority, string? notes, CancellationToken ct = default);

        /// <summary>Writes a project's notes. Returns false, having written nothing, when
        /// <paramref name="meetingId"/> is no longer the latest meeting. Note that true does
        /// not imply a row was updated — a project with no priority row has nowhere to store
        /// notes, and that is a legitimate no-op rather than a rejection.</summary>
        Task<bool> SaveProjectNotesAsync(Guid meetingId, string wbs1, string? notes, CancellationToken ct = default);
        Task DeleteMeetingAsync(Guid meetingId, CancellationToken ct = default);
        Task CarryForwardProjectsAsync(Guid sourceMeetingId, Guid targetMeetingId, CancellationToken ct = default);
    }

    /// <summary>
    /// Store for the PM Tools workload meeting.
    ///
    /// <para>Edits are only ever accepted against the <em>latest</em> meeting. That rule used
    /// to live solely in <c>WorkloadMeetingPanelViewModel.IsCurrentMeeting</c>, which compares
    /// the selection against the client's in-memory list. That list is loaded once when the
    /// window opens and is never refreshed afterwards — the Refresh button reloads Deltek
    /// project data, not the meeting list. So a window left open while somebody else created
    /// the next meeting went on believing the previous meeting was current, and its writes
    /// landed in that previous meeting: silently, because the client-side guard saw nothing
    /// wrong, and unrecoverably, because the carry-forward copy into the new meeting had
    /// already happened. Observed 2026-08-24, when the day's meeting was created 49 minutes
    /// after a user had opened the window.</para>
    ///
    /// <para>The guard therefore lives here, in the same statement as the write, so no caller
    /// can bypass it and there is no window between the check and the write. "Latest" is
    /// resolved with the identical ordering the UI uses — <c>MeetingDate DESC, CreatedAt
    /// DESC</c> — so the two cannot disagree about which meeting is current. That tie-break
    /// is load-bearing, not theoretical: two meetings already share the date 2026-07-27.</para>
    ///
    /// <para><see cref="CarryForwardProjectsAsync"/> is deliberately exempt. It seeds a newly
    /// created meeting, and guarding it would mean a second person creating a meeting in the
    /// gap between <see cref="CreateMeetingAsync"/> and the copy would leave an empty meeting
    /// behind.</para>
    /// </summary>
    public sealed class SqlWorkloadMeetingStore : IWorkloadMeetingStore
    {
        /// <summary>
        /// Resolves the latest meeting and short-circuits the write when the caller's meeting
        /// is not it. Prepended to each guarded statement; the batch yields a single BIT —
        /// 1 when the write was attempted, 0 when it was refused as stale.
        /// </summary>
        private const string LatestMeetingGuard = @"
DECLARE @Latest UNIQUEIDENTIFIER =
    (SELECT TOP 1 Id FROM dbo.WorkloadMeetings ORDER BY MeetingDate DESC, CreatedAt DESC);
IF @Latest IS NULL OR @Latest <> @MeetingId
BEGIN
    SELECT CAST(0 AS BIT);
    RETURN;
END
";

        private readonly string _cs;

        public SqlWorkloadMeetingStore(string connectionString)
        {
            _cs = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        public async Task EnsureTablesAsync(CancellationToken ct = default)
        {
            await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = @"
IF OBJECT_ID('dbo.WorkloadMeetings', 'U') IS NULL
CREATE TABLE dbo.WorkloadMeetings (
    Id          UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    MeetingDate DATE             NOT NULL,
    Notes       NVARCHAR(MAX)    NULL,
    CreatedAt   DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    CreatedBy   NVARCHAR(200)    NULL
);

IF OBJECT_ID('dbo.WorkloadMeetingProjects', 'U') IS NULL
CREATE TABLE dbo.WorkloadMeetingProjects (
    Id        UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    MeetingId UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.WorkloadMeetings(Id) ON DELETE CASCADE,
    Wbs1      NVARCHAR(50)     NOT NULL,
    Priority  TINYINT          NOT NULL CHECK (Priority BETWEEN 1 AND 5),
    Notes     NVARCHAR(MAX)    NULL,
    CONSTRAINT UQ_WorkloadMeetingProjects_MeetingWbs1 UNIQUE (MeetingId, Wbs1)
);";

                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var cmd = new SqlCommand(sql, cn);
                cmd.CommandTimeout = SqlTimeouts.Batch;
                await cmd.ExecuteNonQueryAsync(innerCt);
            }, ct);
        }

        public async Task<IReadOnlyList<WorkloadMeeting>> GetAllMeetingsAsync(CancellationToken ct = default)
        {
            return await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = @"
SELECT Id, MeetingDate, Notes, CreatedAt, CreatedBy
FROM dbo.WorkloadMeetings
ORDER BY MeetingDate DESC, CreatedAt DESC;";

                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var cmd = new SqlCommand(sql, cn);
                cmd.CommandTimeout = SqlTimeouts.UiFacing;

                var list = new List<WorkloadMeeting>();
                await using var rd = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, innerCt);
                while (await rd.ReadAsync(innerCt))
                {
                    list.Add(new WorkloadMeeting
                    {
                        Id          = rd.GetGuid(0),
                        MeetingDate = rd.GetDateTime(1),
                        Notes       = rd.IsDBNull(2) ? null : rd.GetString(2),
                        CreatedAt   = rd.GetDateTime(3),
                        CreatedBy   = rd.IsDBNull(4) ? null : rd.GetString(4)
                    });
                }
                return list;
            }, ct);
        }

        public async Task<WorkloadMeeting> CreateMeetingAsync(DateTime meetingDate, string? createdBy, CancellationToken ct = default)
        {
            var id = Guid.NewGuid();
            var now = DateTime.UtcNow;

            await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = @"
INSERT INTO dbo.WorkloadMeetings (Id, MeetingDate, Notes, CreatedAt, CreatedBy)
VALUES (@Id, @MeetingDate, NULL, @CreatedAt, @CreatedBy);";

                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var cmd = new SqlCommand(sql, cn);
                cmd.CommandTimeout = SqlTimeouts.Batch;
                AddParameter(cmd, "@Id", id);
                AddParameter(cmd, "@MeetingDate", meetingDate.Date);
                AddParameter(cmd, "@CreatedAt", now);
                AddParameter(cmd, "@CreatedBy", (object?)createdBy ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(innerCt);
            }, ct);

            return new WorkloadMeeting
            {
                Id          = id,
                MeetingDate = meetingDate.Date,
                Notes       = null,
                CreatedAt   = now,
                CreatedBy   = createdBy
            };
        }

        public async Task<bool> SaveMeetingNotesAsync(Guid meetingId, string? notes, CancellationToken ct = default)
        {
            return await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = LatestMeetingGuard + @"
UPDATE dbo.WorkloadMeetings SET Notes = @Notes WHERE Id = @MeetingId;
SELECT CAST(1 AS BIT);";

                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var cmd = new SqlCommand(sql, cn);
                cmd.CommandTimeout = SqlTimeouts.Batch;
                AddParameter(cmd, "@MeetingId", meetingId);
                AddParameter(cmd, "@Notes", (object?)notes ?? DBNull.Value);
                return Convert.ToBoolean(await cmd.ExecuteScalarAsync(innerCt));
            }, ct);
        }

        public async Task<IReadOnlyList<WorkloadMeetingProject>> GetProjectsForMeetingAsync(Guid meetingId, CancellationToken ct = default)
        {
            return await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = @"
SELECT Id, MeetingId, Wbs1, Priority, Notes
FROM dbo.WorkloadMeetingProjects
WHERE MeetingId = @MeetingId
ORDER BY Priority, Wbs1;";

                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var cmd = new SqlCommand(sql, cn);
                cmd.CommandTimeout = SqlTimeouts.UiFacing;
                AddParameter(cmd, "@MeetingId", meetingId);

                var list = new List<WorkloadMeetingProject>();
                await using var rd = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, innerCt);
                while (await rd.ReadAsync(innerCt))
                {
                    list.Add(new WorkloadMeetingProject
                    {
                        Id        = rd.GetGuid(0),
                        MeetingId = rd.GetGuid(1),
                        Wbs1      = rd.GetString(2),
                        Priority  = rd.GetByte(3),
                        Notes     = rd.IsDBNull(4) ? null : rd.GetString(4)
                    });
                }
                return list;
            }, ct);
        }

        public async Task<bool> UpsertProjectPriorityAsync(Guid meetingId, string wbs1, int priority, string? notes, CancellationToken ct = default)
        {
            return await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);

                if (priority == 0)
                {
                    const string deleteSql = LatestMeetingGuard + @"
DELETE FROM dbo.WorkloadMeetingProjects
WHERE MeetingId = @MeetingId AND Wbs1 = @Wbs1;
SELECT CAST(1 AS BIT);";
                    await using var cmd = new SqlCommand(deleteSql, cn);
                    cmd.CommandTimeout = SqlTimeouts.Batch;
                    AddParameter(cmd, "@MeetingId", meetingId);
                    cmd.Parameters.Add(new SqlParameter("@Wbs1", SqlDbType.NVarChar, 50) { Value = wbs1 });
                    return Convert.ToBoolean(await cmd.ExecuteScalarAsync(innerCt));
                }
                else
                {
                    const string upsertSql = LatestMeetingGuard + @"
MERGE dbo.WorkloadMeetingProjects AS target
USING (SELECT @MeetingId AS MeetingId, @Wbs1 AS Wbs1) AS source
ON target.MeetingId = source.MeetingId AND target.Wbs1 = source.Wbs1
WHEN MATCHED THEN
    UPDATE SET Priority = @Priority
WHEN NOT MATCHED THEN
    INSERT (Id, MeetingId, Wbs1, Priority, Notes)
    VALUES (NEWID(), @MeetingId, @Wbs1, @Priority, @Notes);
SELECT CAST(1 AS BIT);";
                    await using var cmd = new SqlCommand(upsertSql, cn);
                    cmd.CommandTimeout = SqlTimeouts.Batch;
                    AddParameter(cmd, "@MeetingId", meetingId);
                    cmd.Parameters.Add(new SqlParameter("@Wbs1", SqlDbType.NVarChar, 50) { Value = wbs1 });
                    AddParameter(cmd, "@Priority", (byte)priority);
                    AddParameter(cmd, "@Notes", (object?)notes ?? DBNull.Value);
                    return Convert.ToBoolean(await cmd.ExecuteScalarAsync(innerCt));
                }
            }, ct);
        }

        public async Task<bool> SaveProjectNotesAsync(Guid meetingId, string wbs1, string? notes, CancellationToken ct = default)
        {
            return await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = LatestMeetingGuard + @"
UPDATE dbo.WorkloadMeetingProjects
SET Notes = @Notes
WHERE MeetingId = @MeetingId AND Wbs1 = @Wbs1;
SELECT CAST(1 AS BIT);";

                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var cmd = new SqlCommand(sql, cn);
                cmd.CommandTimeout = SqlTimeouts.Batch;
                AddParameter(cmd, "@MeetingId", meetingId);
                cmd.Parameters.Add(new SqlParameter("@Wbs1", SqlDbType.NVarChar, 50) { Value = wbs1 });
                AddParameter(cmd, "@Notes", (object?)notes ?? DBNull.Value);
                return Convert.ToBoolean(await cmd.ExecuteScalarAsync(innerCt));
            }, ct);
        }

        public async Task DeleteMeetingAsync(Guid meetingId, CancellationToken ct = default)
        {
            await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = @"
DELETE FROM dbo.WorkloadMeetings WHERE Id = @Id;";

                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var cmd = new SqlCommand(sql, cn);
                cmd.CommandTimeout = SqlTimeouts.Batch;
                AddParameter(cmd, "@Id", meetingId);
                await cmd.ExecuteNonQueryAsync(innerCt);
            }, ct);
        }

        public async Task CarryForwardProjectsAsync(Guid sourceMeetingId, Guid targetMeetingId, CancellationToken ct = default)
        {
            await RetryPolicy.Pipeline.ExecuteAsync(async innerCt =>
            {
                const string sql = @"
INSERT INTO dbo.WorkloadMeetingProjects (Id, MeetingId, Wbs1, Priority, Notes)
SELECT NEWID(), @TargetMeetingId, src.Wbs1, src.Priority, src.Notes
FROM dbo.WorkloadMeetingProjects src
WHERE src.MeetingId = @SourceMeetingId
  AND NOT EXISTS (
      SELECT 1 FROM dbo.WorkloadMeetingProjects existing
      WHERE existing.MeetingId = @TargetMeetingId AND existing.Wbs1 = src.Wbs1
  );";

                await using var cn = new SqlConnection(_cs);
                await cn.OpenAsync(innerCt);
                await using var cmd = new SqlCommand(sql, cn);
                cmd.CommandTimeout = SqlTimeouts.Batch;
                AddParameter(cmd, "@SourceMeetingId", sourceMeetingId);
                AddParameter(cmd, "@TargetMeetingId", targetMeetingId);
                await cmd.ExecuteNonQueryAsync(innerCt);
            }, ct);
        }

        private static void AddParameter(SqlCommand cmd, string name, object? value)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }
}
