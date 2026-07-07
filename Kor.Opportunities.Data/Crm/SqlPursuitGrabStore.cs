#nullable enable
using System;
using System.Data;
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
    /// Fallback claim for a NON-grabbable opportunity (already owned, or in a
    /// non-New status such as a Lost opp being promoted): creates the owned
    /// Drafting engagement server-side so it carries the same birth payload a
    /// grab does — buyer canonical org, ExternalSource/Key provenance, stage
    /// history, and an assignment-log row (fix F9: the old client-side create
    /// dropped the buyer org and never logged the claim). Returns the new
    /// engagement id, or 0 when an engagement already exists for the
    /// opportunity (caller re-fetches).
    /// </summary>
    Task<long> ClaimOwnedAsync(long opportunityId, string staffUpn, CancellationToken ct);
}

public sealed class SqlPursuitGrabStore : IPursuitGrabStore
{
    private const int CommandTimeoutSeconds = 30;

    // OpportunityStatus.New = 1, Pursuing = 4; CrmEngagementStage.Drafting = 1.
    // Mirrored as literals here so the Data layer stays free of the Core enum
    // dependency in raw SQL; values are stable on disk (OpportunityEnums.cs).
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
   AND OwnerStaffId IS NULL;                       -- still un-claimed

IF @@ROWCOUNT = 0
BEGIN
    ROLLBACK TRANSACTION;
    SELECT CAST(0 AS bigint) AS EngagementId;
END
ELSE
BEGIN
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

    // Same birth payload as GrabSql minus the opportunity-status flip (the
    // opportunity is not in the grabbable pool — its status is owned elsewhere).
    // The EXISTS guard + the filtered unique index on CrmEngagements.OpportunityId
    // make the claim race-safe: a concurrent creator wins and we return 0.
    private const string ClaimOwnedSql = @"
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

IF EXISTS (SELECT 1 FROM opportunities.CrmEngagements WHERE OpportunityId = @id)
BEGIN
    ROLLBACK TRANSACTION;
    SELECT CAST(0 AS bigint) AS EngagementId;
END
ELSE
BEGIN
    DECLARE @eng TABLE (Id bigint);

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

    IF @newEng IS NULL
    BEGIN
        -- Opportunity row vanished (retired/merged mid-claim): nothing created.
        ROLLBACK TRANSACTION;
        SELECT CAST(0 AS bigint) AS EngagementId;
    END
    ELSE
    BEGIN
        INSERT INTO opportunities.CrmEngagementStageHistory (EngagementId, Stage, ByStaffId)
        VALUES (@newEng, 1, @me);

        INSERT INTO opportunities.OpportunityAssignmentLog
            (OpportunityId, EngagementId, Action, ToStaffId, ByStaffId, Reason)
        VALUES (@id, @newEng, N'Claim', @me, @me, N'Claimed a non-grabbable opportunity (already owned / non-New status)');

        COMMIT TRANSACTION;
        SELECT @newEng AS EngagementId;
    END
END";

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

        var scalar = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        var engagementId = scalar is long l ? l : Convert.ToInt64(scalar);

        return engagementId > 0
            ? new GrabResult(GrabOutcome.Grabbed, engagementId)
            : new GrabResult(GrabOutcome.AlreadyTaken, 0);
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
            var scalar = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return scalar is long l ? l : Convert.ToInt64(scalar);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // A concurrent creator won the race between the EXISTS check and the
            // INSERT (unique index on OpportunityId or ExternalSource/Key).
            // XACT_ABORT rolled back; the caller re-fetches the winner's row.
            return 0;
        }
    }
}
