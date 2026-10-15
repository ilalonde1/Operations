#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Crm;

/// <summary>
/// One aggregate per resolved person. Activities and stage moves are reduced per
/// pursuit before summing; opens are counted once per person, never per pursuit.
/// Both commands are SELECT-only and use a single UTC reference within each read.
/// </summary>
public sealed class SqlBdDirectorStore : IBdDirectorStore
{
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

    public SqlBdDirectorStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
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
