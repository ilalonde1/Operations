#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Crm;

/// <summary>
/// One aggregate per resolved person. Activities and stage moves are reduced per
/// pursuit before summing; opens are counted once per person, never per pursuit.
/// Commands are SELECT-only; dated aggregates use one UTC reference per read.
/// </summary>
public sealed class SqlBdDirectorStore : IBdDirectorStore
{
    private const string PendingBidsSql = @"
SELECT e.Id, e.OpportunityId, o.OpportunityKey, o.Name, o.BuyerName,
       e.OwnerStaffId, s.DisplayName, s.Mailbox, o.SubmissionDeadlineUtc AS DueUtc,
       e.Stage, h.SubmittedAtUtc, e.SubmittedFileName, e.FolderLink, e.NextActionDueUtc
FROM opportunities.CrmEngagements e
JOIN opportunities.Opportunities o ON o.Id = e.OpportunityId
LEFT JOIN opportunities.BdStaff s ON s.StaffKey = e.OwnerStaffId
OUTER APPLY (
    SELECT MAX(h.EnteredAtUtc) AS SubmittedAtUtc
    FROM opportunities.CrmEngagementStageHistory h
    WHERE h.EngagementId = e.Id AND h.Stage = 3
) h
WHERE e.OpportunityId IS NOT NULL AND e.Stage IN (1, 3)
ORDER BY CASE WHEN o.SubmissionDeadlineUtc IS NULL THEN 1 ELSE 0 END,
         o.SubmissionDeadlineUtc, e.Id;";

    private const string RelationshipPursuitsSql = @"
SELECT COUNT(*) FROM opportunities.CrmEngagements
WHERE OpportunityId IS NULL AND Stage IN (1, 3);";

    public async Task<IReadOnlyList<PendingBidRow>> ListPendingBidsAsync(CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(PendingBidsSql, con) { CommandTimeout = 30 };
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var rows = new List<PendingBidRow>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            rows.Add(new PendingBidRow(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetDateTimeOffset(8), (CrmEngagementStage)reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetDateTimeOffset(10), reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12), reader.IsDBNull(13) ? null : reader.GetDateTimeOffset(13)));
        return rows;
    }

    public async Task<int> CountRelationshipPursuitsAsync(CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(RelationshipPursuitsSql, con) { CommandTimeout = 30 };
        return (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    private const string PeopleSql = @"
DECLARE @now datetimeoffset = SYSUTCDATETIME();
WITH IdentityKeys AS (
    SELECT COALESCE(s.Mailbox, s.StaffKey) AS PersonKey, s.StaffKey AS OwnerKey
    FROM opportunities.BdStaff s
    UNION
    SELECT COALESCE(s.Mailbox, e.OwnerStaffId), e.OwnerStaffId
    FROM opportunities.CrmEngagements e
    LEFT JOIN opportunities.BdStaff s ON s.StaffKey = e.OwnerStaffId
    WHERE e.OwnerStaffId IS NOT NULL AND e.Stage IN (1, 3)
), People AS (
    SELECT COALESCE(s.Mailbox, e.OwnerStaffId) AS PersonKey,
           COALESCE(MAX(s.DisplayName), MIN(e.OwnerStaffId)) AS DisplayName,
           COUNT(*) AS Live,
           SUM(CASE WHEN e.NextActionDueUtc < @now THEN 1 ELSE 0 END) AS OverdueNextAction,
           SUM(CASE WHEN e.NextActionDueUtc IS NULL THEN 1 ELSE 0 END) AS NoNextAction,
           SUM(CASE WHEN e.UpdatedAtUtc < DATEADD(day, -14, @now) THEN 1 ELSE 0 END) AS Untouched14d,
           SUM(a.Activities30) AS Activities30,
           SUM(a.Activities90) AS Activities90,
           SUM(h.StageMoves90) AS StageMoves90,
           SUM(CASE WHEN w.LastTouchUtc >= DATEADD(day, -90, @now) AND w.LastTouchUtc <= @now
                    THEN 1 ELSE 0 END) AS BuyersMailed90
    FROM opportunities.CrmEngagements e
    LEFT JOIN opportunities.BdStaff s ON s.StaffKey = e.OwnerStaffId
    LEFT JOIN opportunities.CrmBuyerEmailWarmth w ON w.CanonicalOrgId = e.BuyerCanonicalOrgId
    OUTER APPLY (
        SELECT COUNT(CASE WHEN a.OccurredAtUtc >= DATEADD(day, -30, @now) THEN 1 END) AS Activities30,
               COUNT(*) AS Activities90
        FROM opportunities.CrmActivities a
        WHERE a.EngagementId = e.Id
          AND a.OccurredAtUtc >= DATEADD(day, -90, @now) AND a.OccurredAtUtc <= @now
    ) a
    OUTER APPLY (
        SELECT COUNT(*) AS StageMoves90
        FROM opportunities.CrmEngagementStageHistory h
        WHERE h.EngagementId = e.Id
          AND h.EnteredAtUtc >= DATEADD(day, -90, @now) AND h.EnteredAtUtc <= @now
    ) h
    WHERE e.OwnerStaffId IS NOT NULL AND e.Stage IN (1, 3)
    GROUP BY COALESCE(s.Mailbox, e.OwnerStaffId)
)
SELECT p.PersonKey, p.DisplayName, p.Live, p.OverdueNextAction, p.NoNextAction,
       p.Untouched14d, p.Activities30, p.Activities90, p.StageMoves90, p.BuyersMailed90,
       (SELECT COUNT(*) FROM opportunities.BdUiOpens u
        WHERE u.OpenedAtUtc >= DATEADD(day, -30, @now) AND u.OpenedAtUtc <= @now
          AND EXISTS (SELECT 1 FROM IdentityKeys k
                      WHERE k.PersonKey = p.PersonKey AND k.OwnerKey = u.ByStaffId)) AS Opens30
FROM People p
ORDER BY p.Live DESC, p.DisplayName;

-- Include spellings with no live pursuits: they can still carry screen opens
-- and must belong to the same person's drill filter.
SELECT COALESCE(s.Mailbox, s.StaffKey) AS PersonKey, s.StaffKey AS OwnerKey
FROM opportunities.BdStaff s
UNION
SELECT COALESCE(s.Mailbox, e.OwnerStaffId), e.OwnerStaffId
FROM opportunities.CrmEngagements e
LEFT JOIN opportunities.BdStaff s ON s.StaffKey = e.OwnerStaffId
WHERE e.OwnerStaffId IS NOT NULL AND e.Stage IN (1, 3);";

    private const string FunnelSql = @"
DECLARE @now datetimeoffset = SYSUTCDATETIME();
WITH StageAges AS (
    SELECT e.Stage,
           DATEDIFF_BIG(second, COALESCE(h.StageSinceUtc, e.OpenedAtUtc), @now) / 86400.0 AS DaysInStage
    FROM opportunities.CrmEngagements e
    OUTER APPLY (
        SELECT MAX(h.EnteredAtUtc) AS StageSinceUtc
        FROM opportunities.CrmEngagementStageHistory h
        WHERE h.EngagementId = e.Id AND h.Stage = e.Stage
    ) h
    WHERE e.Stage IN (1, 3)
)
SELECT DISTINCT Stage,
       PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY DaysInStage) OVER (PARTITION BY Stage) AS MedianDaysInStage
FROM StageAges;

SELECT e.Id, COALESCE(o.Name, e.PotentialProjects, N'(unnamed)') AS ProjectName,
       COALESCE(o.BuyerName, co.DisplayName, N'') AS Buyer, e.OwnerStaffId, o.SubmissionDeadlineUtc
FROM opportunities.CrmEngagements e
LEFT JOIN opportunities.Opportunities o ON o.Id = e.OpportunityId
LEFT JOIN opportunities.CanonicalOrg co ON co.Id = e.BuyerCanonicalOrgId
WHERE e.Stage IN (1, 3) AND o.SubmissionDeadlineUtc < DATEADD(day, -30, @now)
ORDER BY o.SubmissionDeadlineUtc, e.Id;

SELECT COUNT(*) AS UndecidedNoDeadline
FROM opportunities.CrmEngagements e
LEFT JOIN opportunities.Opportunities o ON o.Id = e.OpportunityId
WHERE e.Stage IN (1, 3) AND o.SubmissionDeadlineUtc IS NULL;

SELECT COUNT(CASE WHEN LostToCanonicalOrgId IS NOT NULL THEN 1 END) AS LostWithCompetitor,
       COUNT(CASE WHEN OutcomeReason IS NOT NULL THEN 1 END) AS LostWithReason,
       COUNT(*) AS LostTotal
FROM opportunities.CrmEngagements
WHERE Stage = 7 AND COALESCE(ExternalSource, N'') <> N'Deltek.CustomProposal';";

    private readonly string _connectionString;

    private const string AccountsSql = @"
SELECT TOP (@take) co.Id, co.DisplayName, co.Kind, co.ClendorClientId,
       ISNULL(co.KorProjectsCount, 0), ISNULL(co.KorProjectsCountIsRecordsNotWins, 0), co.LastKorProjectAtUtc,
       (SELECT COUNT(*) FROM opportunities.CrmEngagements e
        WHERE e.BuyerCanonicalOrgId = co.Id AND e.Stage IN (1, 3)) AS LivePursuits,
       w.LastTouchUtc,
       (SELECT COUNT(*) FROM opportunities.BdAsk a
        WHERE a.CanonicalOrgId = co.Id AND a.Status = N'Open') AS OpenAsks
FROM opportunities.CanonicalOrg co
LEFT JOIN opportunities.CrmBuyerEmailWarmth w ON w.CanonicalOrgId = co.Id
WHERE co.ClendorClientId IS NOT NULL AND co.RetiredAtUtc IS NULL
ORDER BY ISNULL(co.KorProjectsCount, 0) DESC, co.DisplayName;";

    public SqlBdDirectorStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<BdDirectorSignal>> GetRecentSignalsAsync(int days, int take, CancellationToken ct)
    {
        if (days is < 1 or > 365) throw new ArgumentOutOfRangeException(nameof(days));
        if (take is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(take));
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("""
            SELECT TOP (@take) s.CanonicalOrgId, o.DisplayName, s.SignalType, s.Subject, s.LastSeenAtUtc, s.Corroborations
            FROM opportunities.IntelSignal s JOIN opportunities.CanonicalOrg o ON o.Id = s.CanonicalOrgId
            WHERE s.RetiredAtUtc IS NULL AND o.RetiredAtUtc IS NULL
                AND s.LastSeenAtUtc >= DATEADD(day, -@days, SYSDATETIMEOFFSET())
            ORDER BY s.LastSeenAtUtc DESC, s.CanonicalOrgId, s.SignalType;
            """, con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@days", SqlDbType.Int).Value = days;
        cmd.Parameters.Add("@take", SqlDbType.Int).Value = take;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var rows = new List<BdDirectorSignal>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            // Accept either SQL timestamp type, treating datetime2 as UTC.
            var seen = reader.GetValue(4) is DateTimeOffset offset ? offset
                : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc));
            rows.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), seen,
                reader.IsDBNull(5) ? 0 : reader.GetInt32(5)));
        }
        return rows;
    }

    public async Task<IReadOnlyList<BdDirectorPerson>> GetPeopleAsync(CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(PeopleSql, con) { CommandTimeout = 30 };
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var people = new List<BdDirectorPerson>();
        var ownerKeys = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var personKey = reader.GetString(0);
            var keys = new List<string>();
            ownerKeys.Add(personKey, keys);
            people.Add(new BdDirectorPerson(personKey, reader.GetString(1), keys,
                reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5),
                reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9), reader.GetInt32(10)));
        }

        await reader.NextResultAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (ownerKeys.TryGetValue(reader.GetString(0), out var keys))
                keys.Add(reader.GetString(1));
        }
        return people;
    }

    public async Task<BdTargetActuals> GetTargetActualsAsync(IReadOnlyList<BdTargetKey> keys, CancellationToken ct)
    {
        // SQL Server permits 2100 parameters per command: five per target key.
        if (keys.Count > 420) throw new ArgumentException("Too many target periods for one read.", nameof(keys));
        await using var con = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand { Connection = con, CommandTimeout = 30 };
        var sql = new StringBuilder(@"
SELECT COUNT(*) AS Live, COALESCE(SUM(CASE WHEN ProposedFee > 0 THEN 1 ELSE 0 END), 0) AS LiveWithFee
FROM opportunities.CrmEngagements WHERE Stage IN (1, 3);");
        for (var index = 0; index < keys.Count; index++)
        {
            var n = index.ToString(CultureInfo.InvariantCulture);
            var key = keys[index];
            // Only the generated ordinal enters SQL text. Every target value is a parameter.
            sql.Append($@"
SELECT CAST(COALESCE(CASE @m{n}
    WHEN N'LivePursuits' THEN COUNT(*)
    WHEN N'PipelineFee' THEN SUM(COALESCE(e.ProposedFee, 0))
    WHEN N'AccountsWithLivePursuit' THEN COUNT(DISTINCT e.BuyerCanonicalOrgId)
    WHEN N'PursuitsOpened' THEN COUNT(*)
    WHEN N'BuyersTouched' THEN COUNT(DISTINCT e.BuyerCanonicalOrgId)
    ELSE 0 END, 0) AS decimal(18,2))
FROM opportunities.CrmEngagements e
LEFT JOIN opportunities.BdStaff s ON s.StaffKey = e.OwnerStaffId
LEFT JOIN opportunities.CrmBuyerEmailWarmth w ON w.CanonicalOrgId = e.BuyerCanonicalOrgId
WHERE ((@s{n} = N'Firm')
    OR (@s{n} = N'Region' AND e.Region COLLATE SQL_Latin1_General_CP1_CI_AS = @k{n})
    OR (@s{n} = N'Person' AND COALESCE(s.Mailbox, e.OwnerStaffId) COLLATE SQL_Latin1_General_CP1_CI_AS = @k{n}))
  AND ((@m{n} = N'PursuitsOpened' AND e.OpenedAtUtc >= @ps{n} AND e.OpenedAtUtc < @pe{n}
        AND COALESCE(e.ExternalSource, N'') NOT LIKE N'Deltek.%')
    OR (@m{n} <> N'PursuitsOpened' AND e.Stage IN (1, 3)
        AND (@m{n} <> N'BuyersTouched' OR (w.LastTouchUtc >= @ps{n} AND w.LastTouchUtc < @pe{n}))));");
            cmd.Parameters.Add("@m" + n, SqlDbType.NVarChar, 40).Value = key.Metric;
            cmd.Parameters.Add("@s" + n, SqlDbType.NVarChar, 20).Value = key.Scope;
            cmd.Parameters.Add("@k" + n, SqlDbType.NVarChar, 150).Value = (object?)key.ScopeKey ?? DBNull.Value;
            cmd.Parameters.Add("@ps" + n, SqlDbType.DateTimeOffset).Value = key.PeriodStartUtc;
            cmd.Parameters.Add("@pe" + n, SqlDbType.DateTimeOffset).Value = key.PeriodEndUtc;
        }
        cmd.CommandText = sql.ToString();
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await reader.ReadAsync(ct).ConfigureAwait(false);
        var live = reader.GetInt32(0);
        var withFee = reader.GetInt32(1);
        var actuals = new Dictionary<BdTargetKey, decimal>();
        foreach (var key in keys)
        {
            await reader.NextResultAsync(ct).ConfigureAwait(false);
            await reader.ReadAsync(ct).ConfigureAwait(false);
            actuals[key] = reader.GetDecimal(0);
        }
        return new BdTargetActuals(actuals, live, withFee);
    }

    public async Task<IReadOnlyList<BdDirectorAccount>> GetAccountsAsync(int take, CancellationToken ct)
    {
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(AccountsSql, con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@take", SqlDbType.Int).Value = take;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var accounts = new List<BdDirectorAccount>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            accounts.Add(new BdDirectorAccount(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetInt32(4), reader.GetBoolean(5), reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                reader.GetInt32(7), reader.IsDBNull(8) ? null : reader.GetDateTimeOffset(8), reader.GetInt32(9)));
        return accounts;
    }

    public async Task<BdDirectorFunnel> GetFunnelAsync(CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(FunnelSql, con) { CommandTimeout = 30 };
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var medians = new Dictionary<int, double>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            medians.Add(reader.GetInt32(0), reader.GetDouble(1));

        var undecided = new List<BdDirectorUndecidedPursuit>();
        await reader.NextResultAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            undecided.Add(new BdDirectorUndecidedPursuit(reader.GetInt64(0), reader.GetString(1),
                reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetDateTimeOffset(4)));

        await reader.NextResultAsync(ct).ConfigureAwait(false);
        await reader.ReadAsync(ct).ConfigureAwait(false);
        var noDeadline = reader.GetInt32(0);
        await reader.NextResultAsync(ct).ConfigureAwait(false);
        await reader.ReadAsync(ct).ConfigureAwait(false);
        return new BdDirectorFunnel(medians, undecided, noDeadline,
            reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
    }
}
