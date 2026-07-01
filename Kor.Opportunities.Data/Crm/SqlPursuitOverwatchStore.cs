#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
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

/// <summary>Outcome of a reassign attempt.</summary>
public enum ReassignOutcome
{
    /// <summary>Owner changed; the move was logged.</summary>
    Reassigned,

    /// <summary>The pursuit was no longer owned by the expected owner (someone
    /// else moved it first); the board is stale. No change made.</summary>
    OwnerChanged,

    /// <summary>The target owner already holds a BD-tracking pursuit for the same
    /// buyer + region (the unique BD-relationship key). No change made.</summary>
    DuplicateForTarget,

    /// <summary>The request was dropped without touching the database (e.g. a
    /// re-entrant double-click while a reassign was in flight). Show nothing.</summary>
    Ignored,
}

/// <summary>
/// Read model + reassign for the manager overwatch board — every owned, active
/// pursuit (engagement owned by someone, in Drafting/Submitted) with its
/// staleness signal. Set-based single query (no per-row brief loading; see
/// design M6).
/// </summary>
public interface IPursuitOverwatchStore
{
    Task<IReadOnlyList<PursuitOverwatchRow>> ListAsync(CancellationToken ct);

    /// <summary>
    /// Move a pursuit from <paramref name="fromOwner"/> to
    /// <paramref name="toOwner"/> in one guarded transaction: re-owns the
    /// engagement (and its parent opportunity, if any), and logs the move.
    /// Guarded on the current owner so a stale board can't clobber a concurrent
    /// reassign.
    /// </summary>
    Task<ReassignOutcome> ReassignAsync(
        long engagementId,
        long? opportunityId,
        string fromOwner,
        string toOwner,
        string byStaff,
        string? reason,
        CancellationToken ct);
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

    // Re-own the engagement (guard: still owned by @from), sync the parent
    // opportunity's owner when present, and log the move — one transaction.
    private const string ReassignSql = @"
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

UPDATE opportunities.CrmEngagements
   SET OwnerStaffId = @to,
       UpdatedAtUtc  = SYSDATETIMEOFFSET(),
       UpdatedBy     = @by
 WHERE Id = @engId
   AND OwnerStaffId = @from;

IF @@ROWCOUNT = 0
BEGIN
    ROLLBACK TRANSACTION;
    SELECT CAST(0 AS int) AS Ok;
END
ELSE
BEGIN
    IF @oppId IS NOT NULL
        UPDATE opportunities.Opportunities
           SET OwnerStaffId = @to,
               UpdatedAtUtc  = SYSDATETIMEOFFSET(),
               UpdatedBy     = @by
         WHERE Id = @oppId;

    INSERT INTO opportunities.OpportunityAssignmentLog
        (OpportunityId, EngagementId, Action, FromStaffId, ToStaffId, ByStaffId, Reason)
    VALUES (@oppId, @engId, N'Reassign', @from, @to, @by, @reason);

    COMMIT TRANSACTION;
    SELECT CAST(1 AS int) AS Ok;
END";

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

    public async Task<ReassignOutcome> ReassignAsync(
        long engagementId,
        long? opportunityId,
        string fromOwner,
        string toOwner,
        string byStaff,
        string? reason,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(toOwner))
        {
            throw new ArgumentException("A target owner is required.", nameof(toOwner));
        }

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(ReassignSql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@engId", SqlDbType.BigInt).Value = engagementId;
        cmd.Parameters.Add("@oppId", SqlDbType.BigInt).Value = (object?)opportunityId ?? DBNull.Value;
        cmd.Parameters.Add("@from", SqlDbType.NVarChar, 150).Value = fromOwner;
        cmd.Parameters.Add("@to", SqlDbType.NVarChar, 150).Value = toOwner.Trim();
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 150).Value = byStaff;
        cmd.Parameters.Add("@reason", SqlDbType.NVarChar, 400).Value =
            string.IsNullOrWhiteSpace(reason) ? DBNull.Value : reason.Trim();

        try
        {
            var scalar = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            var ok = scalar is int i ? i : Convert.ToInt32(scalar);
            return ok == 1 ? ReassignOutcome.Reassigned : ReassignOutcome.OwnerChanged;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // Target already holds a BD-tracking pursuit for this buyer+region
            // (the unique UX_CrmEngagements_BdRelationship key). XACT_ABORT rolled back.
            return ReassignOutcome.DuplicateForTarget;
        }
    }
}
