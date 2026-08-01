#nullable enable
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.MajorProjects;

/// <summary>Outcome of a lifecycle transition attempt.</summary>
public enum LifecycleOutcome
{
    /// <summary>The transition was applied and audited.</summary>
    Applied,

    /// <summary>The guard failed — someone else changed the row first (already
    /// owned, already dismissed, or retired). No change made; caller re-fetches.</summary>
    Conflict,
}

/// <summary>
/// Human lifecycle transitions for prime-target projects (MajorProjectsInventory)
/// and tenders (Opportunities): own / release / dismiss ("not for us") / restore.
/// Every transition is a single guarded transaction that also writes the shared
/// audit stream (OpportunityAssignmentLog, MpiId column from migration 284) —
/// the WHERE guard makes concurrent actions race-safe, mirroring
/// <see cref="Crm.SqlPursuitGrabStore"/>. Dismissal is deliberately distinct
/// from the system's RetiredAtUtc staleness reaper: a person said no, and the
/// row keeps who/when/why forever (archive-not-delete).
/// </summary>
public interface IPursuitLifecycleStore
{
    /// <summary>Claims an un-owned, un-dismissed, live project. Owned rows leave
    /// vw_ActionableProjects (boards + weekly sheet) and enter the owner's digest.</summary>
    Task<LifecycleOutcome> OwnProjectAsync(long mpiId, string staffUpn, CancellationToken ct);

    /// <summary>Releases ownership back to the shared pool (owner or admin).</summary>
    Task<LifecycleOutcome> ReleaseProjectAsync(long mpiId, string staffUpn, CancellationToken ct);

    /// <summary>Marks a project "not for us" with a reason. Also clears ownership
    /// so a dismissed row never lingers in someone's owned list.</summary>
    Task<LifecycleOutcome> DismissProjectAsync(long mpiId, string staffUpn, string reason, CancellationToken ct);

    /// <summary>Admin undo for a dismissal — the row returns to the actionable pool.</summary>
    Task<LifecycleOutcome> RestoreProjectAsync(long mpiId, string staffUpn, CancellationToken ct);

    /// <summary>Marks a tender "not for us" with a reason (New pool only — owned
    /// pursuits are managed through their engagement, not dismissed from under an owner).</summary>
    Task<LifecycleOutcome> DismissOpportunityAsync(long opportunityId, string staffUpn, string reason, CancellationToken ct);

    /// <summary>Admin undo for a tender dismissal.</summary>
    Task<LifecycleOutcome> RestoreOpportunityAsync(long opportunityId, string staffUpn, CancellationToken ct);
}

public sealed class SqlPursuitLifecycleStore : IPursuitLifecycleStore
{
    private const int CommandTimeoutSeconds = 30;

    // Action literals in OpportunityAssignmentLog. Existing vocabulary is
    // 'Grab' (SqlPursuitGrabStore); these extend it for the lifecycle verbs.
    private const string OwnSql = @"
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

UPDATE opportunities.MajorProjectsInventory
   SET OwnerStaffId = @me,
       OwnedAtUtc   = SYSDATETIMEOFFSET()
 WHERE Id = @id
   AND RetiredAtUtc IS NULL
   AND DismissedAtUtc IS NULL
   AND OwnerStaffId IS NULL;

IF @@ROWCOUNT = 0
BEGIN
    ROLLBACK TRANSACTION;
    SELECT CAST(0 AS int);
END
ELSE
BEGIN
    INSERT INTO opportunities.OpportunityAssignmentLog (MpiId, Action, ToStaffId, ByStaffId, Reason)
    VALUES (@id, N'MpiOwn', @me, @me, N'Owned from the board');
    COMMIT TRANSACTION;
    SELECT CAST(1 AS int);
END";

    private const string ReleaseSql = @"
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

DECLARE @prev nvarchar(300) =
    (SELECT OwnerStaffId FROM opportunities.MajorProjectsInventory WITH (UPDLOCK, HOLDLOCK) WHERE Id = @id);

UPDATE opportunities.MajorProjectsInventory
   SET OwnerStaffId = NULL,
       OwnedAtUtc   = NULL
 WHERE Id = @id
   AND OwnerStaffId IS NOT NULL;

IF @@ROWCOUNT = 0
BEGIN
    ROLLBACK TRANSACTION;
    SELECT CAST(0 AS int);
END
ELSE
BEGIN
    INSERT INTO opportunities.OpportunityAssignmentLog (MpiId, Action, FromStaffId, ByStaffId, Reason)
    VALUES (@id, N'MpiRelease', @prev, @me, N'Released back to the pool');
    COMMIT TRANSACTION;
    SELECT CAST(1 AS int);
END";

    private const string DismissProjectSql = @"
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

DECLARE @prev nvarchar(300) =
    (SELECT OwnerStaffId FROM opportunities.MajorProjectsInventory WITH (UPDLOCK, HOLDLOCK) WHERE Id = @id);

UPDATE opportunities.MajorProjectsInventory
   SET DismissedAtUtc  = SYSDATETIMEOFFSET(),
       DismissedBy     = @me,
       DismissedReason = @reason,
       OwnerStaffId    = NULL,
       OwnedAtUtc      = NULL
 WHERE Id = @id
   AND RetiredAtUtc IS NULL
   AND DismissedAtUtc IS NULL;

IF @@ROWCOUNT = 0
BEGIN
    ROLLBACK TRANSACTION;
    SELECT CAST(0 AS int);
END
ELSE
BEGIN
    INSERT INTO opportunities.OpportunityAssignmentLog (MpiId, Action, FromStaffId, ByStaffId, Reason)
    VALUES (@id, N'MpiDismiss', @prev, @me, @reason);
    COMMIT TRANSACTION;
    SELECT CAST(1 AS int);
END";

    private const string RestoreProjectSql = @"
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

UPDATE opportunities.MajorProjectsInventory
   SET DismissedAtUtc  = NULL,
       DismissedBy     = NULL,
       DismissedReason = NULL
 WHERE Id = @id
   AND DismissedAtUtc IS NOT NULL;

IF @@ROWCOUNT = 0
BEGIN
    ROLLBACK TRANSACTION;
    SELECT CAST(0 AS int);
END
ELSE
BEGIN
    INSERT INTO opportunities.OpportunityAssignmentLog (MpiId, Action, ByStaffId, Reason)
    VALUES (@id, N'MpiRestore', @me, N'Restored to the actionable pool');
    COMMIT TRANSACTION;
    SELECT CAST(1 AS int);
END";

    // Status 1 = New (OpportunityEnums.cs; values stable on disk). Only the
    // un-owned New pool is dismissable — an owned pursuit has an engagement
    // and an owner; taking it away is that workflow's job, not a dismiss.
    private const string DismissOpportunitySql = @"
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

UPDATE opportunities.Opportunities
   SET DismissedAtUtc  = SYSDATETIMEOFFSET(),
       DismissedBy     = @me,
       DismissedReason = @reason,
       UpdatedAtUtc    = SYSDATETIMEOFFSET(),
       UpdatedBy       = @me
 WHERE Id = @id
   AND Status = 1
   AND OwnerStaffId IS NULL
   AND DismissedAtUtc IS NULL;

IF @@ROWCOUNT = 0
BEGIN
    ROLLBACK TRANSACTION;
    SELECT CAST(0 AS int);
END
ELSE
BEGIN
    INSERT INTO opportunities.OpportunityAssignmentLog (OpportunityId, Action, ByStaffId, Reason)
    VALUES (@id, N'OppDismiss', @me, @reason);
    COMMIT TRANSACTION;
    SELECT CAST(1 AS int);
END";

    private const string RestoreOpportunitySql = @"
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

UPDATE opportunities.Opportunities
   SET DismissedAtUtc  = NULL,
       DismissedBy     = NULL,
       DismissedReason = NULL,
       UpdatedAtUtc    = SYSDATETIMEOFFSET(),
       UpdatedBy       = @me
 WHERE Id = @id
   AND DismissedAtUtc IS NOT NULL;

IF @@ROWCOUNT = 0
BEGIN
    ROLLBACK TRANSACTION;
    SELECT CAST(0 AS int);
END
ELSE
BEGIN
    INSERT INTO opportunities.OpportunityAssignmentLog (OpportunityId, Action, ByStaffId, Reason)
    VALUES (@id, N'OppRestore', @me, N'Restored to the grabbable pool');
    COMMIT TRANSACTION;
    SELECT CAST(1 AS int);
END";

    private readonly string _connectionString;

    public SqlPursuitLifecycleStore(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A KorOpportunitiesDb connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public Task<LifecycleOutcome> OwnProjectAsync(long mpiId, string staffUpn, CancellationToken ct)
        => ExecuteAsync(OwnSql, mpiId, staffUpn, reason: null, ct);

    public Task<LifecycleOutcome> ReleaseProjectAsync(long mpiId, string staffUpn, CancellationToken ct)
        => ExecuteAsync(ReleaseSql, mpiId, staffUpn, reason: null, ct);

    public Task<LifecycleOutcome> DismissProjectAsync(long mpiId, string staffUpn, string reason, CancellationToken ct)
        => ExecuteAsync(DismissProjectSql, mpiId, staffUpn, RequireReason(reason), ct);

    public Task<LifecycleOutcome> RestoreProjectAsync(long mpiId, string staffUpn, CancellationToken ct)
        => ExecuteAsync(RestoreProjectSql, mpiId, staffUpn, reason: null, ct);

    public Task<LifecycleOutcome> DismissOpportunityAsync(long opportunityId, string staffUpn, string reason, CancellationToken ct)
        => ExecuteAsync(DismissOpportunitySql, opportunityId, staffUpn, RequireReason(reason), ct);

    public Task<LifecycleOutcome> RestoreOpportunityAsync(long opportunityId, string staffUpn, CancellationToken ct)
        => ExecuteAsync(RestoreOpportunitySql, opportunityId, staffUpn, reason: null, ct);

    private static string RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A dismissal needs a reason — it is the audit trail.", nameof(reason));
        }

        var trimmed = reason.Trim();
        return trimmed.Length > 1000 ? trimmed[..1000] : trimmed;
    }

    private async Task<LifecycleOutcome> ExecuteAsync(
        string sql, long id, string staffUpn, string? reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(staffUpn))
        {
            throw new ArgumentException("A staff identity is required for lifecycle changes.", nameof(staffUpn));
        }

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        cmd.Parameters.Add("@me", SqlDbType.NVarChar, 300).Value = staffUpn.Trim();
        if (sql.Contains("@reason", StringComparison.Ordinal))
        {
            cmd.Parameters.Add("@reason", SqlDbType.NVarChar, 1000).Value = (object?)reason ?? DBNull.Value;
        }

        var scalar = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(scalar) == 1 ? LifecycleOutcome.Applied : LifecycleOutcome.Conflict;
    }
}
