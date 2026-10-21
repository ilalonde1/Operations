#nullable enable
using System;
using System.Data;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Crm;

/// <summary>Outcome of a Bazaar grab attempt.</summary>
public enum GrabOutcome
{
    /// <summary>The caller now owns the opportunity; a Drafting engagement was created.</summary>
    Grabbed,

    /// <summary>Someone else grabbed it first (or it left the New pool). No change made.</summary>
    AlreadyTaken,

    /// <summary>The request was dropped without touching the database (e.g. a
    /// re-entrant double-click while a grab was in flight). Show nothing.</summary>
    Ignored,
}

/// <summary>Result of <see cref="IPursuitGrabStore.GrabAsync"/>.</summary>
/// <param name="Outcome">Whether the grab succeeded.</param>
/// <param name="EngagementId">The new engagement id when <see cref="GrabOutcome.Grabbed"/>; 0 otherwise.</param>
public sealed record GrabResult(GrabOutcome Outcome, long EngagementId);

/// <summary>
/// Atomically claims an un-claimed opportunity from the Bazaar for a staff
/// member: flips the opportunity to owned + Pursuing, creates the owned
/// Drafting engagement, and writes the assignment audit row — all in one
/// guarded transaction. The WHERE guard (still New, still un-owned) makes
/// concurrent grabs race-safe: the loser sees <see cref="GrabOutcome.AlreadyTaken"/>.
/// </summary>
public interface IPursuitGrabStore
{
    Task<GrabResult> GrabAsync(long opportunityId, string staffUpn, CancellationToken ct);

    /// <summary>
    /// Compatibility entry point with exactly the same eligibility and atomic
    /// ownership/status transition as GrabAsync: New, unowned, not dismissed.
    /// Returns the new engagement id, or 0 on refusal. A refused claim must be
    /// surfaced to the caller, never retried through an unguarded creator.
    /// </summary>
    Task<long> ClaimOwnedAsync(long opportunityId, string staffUpn, CancellationToken ct);
}

public sealed class SqlPursuitGrabStore : IPursuitGrabStore
{
    private const int CommandTimeoutSeconds = 30;

    // OpportunityStatus.New = 1, Pursuing = 4; CrmEngagementStage.Drafting = 1.
    // Mirrored as literals here so the Data layer stays free of the Core enum
    // dependency in raw SQL; values are stable on disk (OpportunityEnums.cs).
    // Both public creation methods execute the same guarded transaction below.
    private const string EngagementBirthSql = @"
    DECLARE @eng TABLE (Id bigint);

    -- Carry the opportunity's already-resolved buyer canonical org onto the
    -- engagement (Stage 1 = Drafting) so the Pursuit Cockpit intel spoke and
    -- the overwatch buyer column light up for grabbed pursuits, not just
    -- BD-tracking ones. The opportunity row is locked by the UPDATE above.
    --
    -- ExternalSource/Key stamp the engagement's provenance at birth (fix F1,
    -- CRM plan 2026-07-06): 'Grab.<feed>' + the unique OpportunityKey, so the
    -- attribution by-source dimension fills from the first real grab instead
    -- of '(unrecorded)'. The 'Grab.' prefix can never collide with the
    -- migration-268 backfill key ('Deltek.CustomProposal').
    INSERT INTO opportunities.CrmEngagements
        (OpportunityId, Stage, OwnerStaffId, BuyerCanonicalOrgId, ExternalSource, ExternalSourceKey, CreatedBy, UpdatedBy)
    OUTPUT inserted.Id INTO @eng
    SELECT @id, 1, @me, o.BuyerCanonicalOrgId,
           LEFT(N'Grab.' + COALESCE(
               (SELECT TOP 1 s.Name
                FROM opportunities.OpportunityObservations ob
                JOIN opportunities.OpportunitySources s ON s.Id = ob.OpportunitySourceId
                WHERE ob.OpportunityId = o.Id
                ORDER BY ob.IngestedAtUtc ASC, ob.Id ASC),
               N'Unknown'), 50),
           o.OpportunityKey,
           @me, @me
    FROM opportunities.Opportunities o
    WHERE o.Id = @id;

    DECLARE @newEng bigint = (SELECT TOP 1 Id FROM @eng);
";

    private const string GrabSql = @"
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

UPDATE opportunities.Opportunities
   SET OwnerStaffId     = @me,
       Status           = 4,                      -- Pursuing
       PursuingSinceUtc  = SYSDATETIMEOFFSET(),
       UpdatedAtUtc      = SYSDATETIMEOFFSET(),
       UpdatedBy         = @me
 WHERE Id = @id
   AND Status = 1                                 -- still New
   AND OwnerStaffId IS NULL                       -- still un-claimed
   AND DismissedAtUtc IS NULL;                    -- still in the actionable pool

IF @@ROWCOUNT = 0
BEGIN
    ROLLBACK TRANSACTION;
    SELECT CAST(0 AS bigint) AS EngagementId;
END
ELSE
BEGIN
" + EngagementBirthSql + @"

    -- Record the pursuit's opening stage so stage-age analytics have a start
    -- point (audit 2026-07-01 M2 — the history table previously had no writers).
    INSERT INTO opportunities.CrmEngagementStageHistory (EngagementId, Stage, ByStaffId)
    VALUES (@newEng, 1, @me);

    INSERT INTO opportunities.OpportunityAssignmentLog
        (OpportunityId, EngagementId, Action, ToStaffId, ByStaffId, Reason)
    VALUES (@id, @newEng, N'Grab', @me, @me, N'Grabbed from the Bazaar');

    COMMIT TRANSACTION;
    SELECT @newEng AS EngagementId;
END";

    // BD-A01: a rejected grab is not permission to bypass eligibility. Keep the
    // legacy API for callers, but share the entire transaction, not just INSERT.
    private const string ClaimOwnedSql = GrabSql;

    /// <summary>
    /// Every branch of both statements SELECTs something, so today the scalar is
    /// never null — but that is an assumption about SQL that is a long way from the
    /// C# asserting it, and <c>Convert.ToInt64(DBNull.Value)</c> throws
    /// InvalidCastException rather than returning 0. A future branch that returns no
    /// row should mean "nothing was created", which is an outcome both callers
    /// already handle, not an exception out of a committed transaction.
    /// </summary>
    private static long ReadEngagementId(object? scalar)
        => scalar switch
        {
            long l => l,
            null or DBNull => 0L,
            _ => Convert.ToInt64(scalar, CultureInfo.InvariantCulture),
        };

    private readonly string _connectionString;

    public SqlPursuitGrabStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<GrabResult> GrabAsync(long opportunityId, string staffUpn, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(staffUpn))
        {
            throw new ArgumentException("A staff identity is required to grab an opportunity.", nameof(staffUpn));
        }

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(GrabSql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = opportunityId;
        cmd.Parameters.Add("@me", SqlDbType.NVarChar, 150).Value = staffUpn.Trim();

        var engagementId = ReadEngagementId(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
        if (engagementId > 0) await RequestDocumentsNowAsync(con, staffUpn.Trim(), ct).ConfigureAwait(false);

        return engagementId > 0
            ? new GrabResult(GrabOutcome.Grabbed, engagementId)
            : new GrabResult(GrabOutcome.AlreadyTaken, 0);
    }

    /// <summary>The portal download jobs a grab asks for at once (Ian, 21 Oct 2026: "grab it right away").</summary>
    internal static readonly string[] DocumentJobs = { "ApcDocumentJob", "BidsAndTendersDocumentJob" };

    /// <summary>
    /// A grab asks the Worker to fetch the tender's documents now instead of at the next scheduled run: one trigger per
    /// portal job (opportunities.JobTriggerRequests; the Worker's poller fires it within seconds). Each job only touches
    /// pursuits on its own portal, so asking both is harmless. Best effort: the grab has already succeeded, and the
    /// schedules still pick the pursuit up if this fails.
    /// </summary>
    private static async Task RequestDocumentsNowAsync(SqlConnection con, string by, CancellationToken ct)
    {
        foreach (var job in DocumentJobs)
        {
            try
            {
                await using var trigger = new SqlCommand(
                    "INSERT INTO opportunities.JobTriggerRequests (JobName, RequestedBy) VALUES (@j, @r);", con) { CommandTimeout = CommandTimeoutSeconds };
                trigger.Parameters.Add("@j", SqlDbType.NVarChar, 200).Value = job;
                trigger.Parameters.Add("@r", SqlDbType.NVarChar, 150).Value = (by + " (grab)").Length <= 150 ? by + " (grab)" : by;
                await trigger.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            catch (SqlException)
            {
                // The schedule is the fallback; a refused trigger must not undo or fail the grab.
            }
        }
    }

    public async Task<long> ClaimOwnedAsync(long opportunityId, string staffUpn, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(staffUpn))
        {
            throw new ArgumentException("A staff identity is required to claim an opportunity.", nameof(staffUpn));
        }

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(ClaimOwnedSql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = opportunityId;
        cmd.Parameters.Add("@me", SqlDbType.NVarChar, 150).Value = staffUpn.Trim();

        try
        {
            var engagementId = ReadEngagementId(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
            if (engagementId > 0) await RequestDocumentsNowAsync(con, staffUpn.Trim(), ct).ConfigureAwait(false);
            return engagementId;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // A concurrent creator won (unique OpportunityId or provenance key).
            // XACT_ABORT rolled back; surface the refusal without another write.
            return 0;
        }
    }
}
