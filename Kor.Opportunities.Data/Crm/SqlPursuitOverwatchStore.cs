#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Crm;

/// <summary>
/// One row of the manager overwatch board: an owned, active pursuit with the
/// signals a manager scans — owner, stage, last-activity age (staleness),
/// how long it has been open. <paramref name="OpportunityId"/> is null for
/// BD-tracking pursuits (no parent Opportunity); non-null for grabbed ones.
/// </summary>
public sealed record PursuitOverwatchRow(
    long EngagementId,
    long? OpportunityId,
    string OwnerStaffId,
    int Stage,
    string ProjectName,
    string Buyer,
    string? Region,
    DateTimeOffset? LastActivityUtc,
    DateTimeOffset OpenedAtUtc,
    int ActivityCount);

/// <summary>
/// Read model for the manager overwatch board — every owned, active pursuit
/// (engagement owned by someone, in Drafting/Submitted) with its staleness
/// signal. Set-based single query (no per-row brief loading; see design M6).
/// Reassign lands as a follow-up guarded transaction.
/// </summary>
public interface IPursuitOverwatchStore
{
    Task<IReadOnlyList<PursuitOverwatchRow>> ListAsync(CancellationToken ct);
}

public sealed class SqlPursuitOverwatchStore : IPursuitOverwatchStore
{
    private const int CommandTimeoutSeconds = 30;

    // Active stages: Drafting(1), Submitted(3). Won(6)/Lost(7) are closed and
    // not the manager's concern here. Coldest-first so what is going stale
    // floats to the top.
    private const string BoardSql = @"
SELECT e.Id                                            AS EngagementId,
       e.OpportunityId                                 AS OpportunityId,
       e.OwnerStaffId                                  AS OwnerStaffId,
       e.Stage                                         AS Stage,
       COALESCE(o.Name, e.PotentialProjects, N'(unnamed)') AS ProjectName,
       COALESCE(o.BuyerName, N'')                      AS Buyer,
       e.Region                                        AS Region,
       la.LastActivityUtc                              AS LastActivityUtc,
       e.OpenedAtUtc                                   AS OpenedAtUtc,
       COALESCE(la.ActivityCount, 0)                   AS ActivityCount
FROM opportunities.CrmEngagements e
LEFT JOIN opportunities.Opportunities o ON o.Id = e.OpportunityId
OUTER APPLY (
    SELECT MAX(a.OccurredAtUtc) AS LastActivityUtc, COUNT(*) AS ActivityCount
    FROM opportunities.CrmActivities a
    WHERE a.EngagementId = e.Id
) la
WHERE e.OwnerStaffId IS NOT NULL
  AND e.Stage IN (1, 3)
ORDER BY COALESCE(la.LastActivityUtc, e.OpenedAtUtc) ASC;";

    private readonly string _connectionString;

    public SqlPursuitOverwatchStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<PursuitOverwatchRow>> ListAsync(CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(BoardSql, con) { CommandTimeout = CommandTimeoutSeconds };
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var rows = new List<PursuitOverwatchRow>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new PursuitOverwatchRow(
                EngagementId: reader.GetInt64(0),
                OpportunityId: reader.IsDBNull(1) ? null : reader.GetInt64(1),
                OwnerStaffId: reader.GetString(2),
                Stage: reader.GetInt32(3),
                ProjectName: reader.GetString(4),
                Buyer: reader.GetString(5),
                Region: reader.IsDBNull(6) ? null : reader.GetString(6),
                LastActivityUtc: reader.IsDBNull(7) ? null : reader.GetDateTimeOffset(7),
                OpenedAtUtc: reader.GetDateTimeOffset(8),
                ActivityCount: reader.GetInt32(9)));
        }

        return rows;
    }
}
