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
    INSERT INTO opportunities.CrmEngagements
        (OpportunityId, Stage, OwnerStaffId, BuyerCanonicalOrgId, CreatedBy, UpdatedBy)
    OUTPUT inserted.Id INTO @eng
    SELECT @id, 1, @me, o.BuyerCanonicalOrgId, @me, @me
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
}
