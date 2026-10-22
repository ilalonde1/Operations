#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.MajorProjects;
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
public sealed record GrabResult(GrabOutcome Outcome, long EngagementId)
{
    /// <summary>Set when the tender joined someone's forward pursuit instead of opening a new one: who owns it.</summary>
    public string? JoinedOwner { get; init; }
    /// <summary>The forward pursuit's project, when the tender joined one.</summary>
    public string? JoinedProject { get; init; }
}

/// <summary>
/// Atomically claims an un-claimed opportunity from the Bazaar for a staff
/// member: flips the opportunity to owned + Pursuing, creates the owned
/// Drafting engagement, and writes the assignment audit row — all in one
/// guarded transaction. The WHERE guard (still New, still un-owned) makes
/// concurrent grabs race-safe: the loser sees <see cref="GrabOutcome.AlreadyTaken"/>.
/// </summary>
public interface IPursuitGrabStore
{
    /// <summary>
    /// Grabs a tender. When it is the RFP for a project someone already holds as a forward pursuit (same province,
    /// <see cref="ProjectNameMatch"/> confident), the tender joins that pursuit and its owner keeps it, whoever pressed
    /// Grab: one project, one pursuit (Ian, 21 Oct 2026). <see cref="GrabResult.JoinedOwner"/> says so.
    /// </summary>
    Task<GrabResult> GrabAsync(long opportunityId, string staffUpn, CancellationToken ct);

    /// <summary>What the team grabbed in the last two weeks that the manager has not yet kept or given to someone else:
    /// grabbing stays instant, and the manager manages by exception (Ian, 21 Oct 2026).</summary>
    Task<IReadOnlyList<RecentGrab>> RecentGrabsAsync(string manager, CancellationToken ct);

    /// <summary>The manager keeps a grab with its grabber: it leaves "New grabs". Logged, like every ownership change.</summary>
    Task KeepAsync(long engagementId, string manager, CancellationToken ct);

    /// <summary>Joins onto the owner's pursuits that the owner has not yet accepted or rejected (Ian, 21 Oct 2026: automatic,
    /// but obvious it is a join, and acceptable or rejectable the first time it comes up).</summary>
    Task<IReadOnlyList<PendingJoin>> PendingJoinsAsync(string owner, CancellationToken ct);

    /// <summary>The owner confirms the tender is this project's RFP. Logged. Settles only the join of THAT tender, and only
    /// for the pursuit's owner: false when it was already settled, the pursuit now holds another tender, or
    /// <paramref name="by"/> is not the owner (audit C3, 2026-10-21: a stale card settled whichever join was latest).</summary>
    Task<bool> AcceptJoinAsync(long engagementId, long opportunityId, string by, CancellationToken ct);

    /// <summary>
    /// "Not this project": the tender leaves the forward pursuit and becomes the grabber's own pursuit, taking with it what
    /// came with the tender (portal files and the grabber's, fetch records, Claude's review and its run, requirements the
    /// owner did not add, the review's asks and the next step they set). Returns the new pursuit's id, or 0 when that
    /// tender's join was already settled or <paramref name="by"/> is not the owner.
    /// </summary>
    Task<long> RejectJoinAsync(long engagementId, long opportunityId, string by, CancellationToken ct);
}

/// <summary>A grab waiting for the manager's keep-or-redirect.</summary>
public sealed record RecentGrab(long EngagementId, string Project, string? Buyer, string? OwnerMailbox, string? OwnerName,
    DateTimeOffset GrabbedAtUtc, bool IsForward)
{
    /// <summary>Set when this was a tender joining a forward pursuit: that pursuit's project.</summary>
    public string? JoinedProject { get; init; }
}

/// <summary>A tender that joined someone's forward pursuit and is waiting for that owner's first look: accept, or "not this
/// project", which makes the tender the grabber's own pursuit.</summary>
public sealed record PendingJoin(long EngagementId, long OpportunityId, string Tender, string? Project, string? GrabbedBy,
    string? GrabbedByName, DateTimeOffset JoinedAtUtc);

/// <summary>A forward pursuit a tender matches: whose it is and which project.</summary>
public sealed record ForwardMatch(long EngagementId, string ProjectName, string? OwnerMailbox, string? OwnerName, double Score)
{
    public string Owner => OwnerName ?? OwnerMailbox?.Split('@')[0] ?? "someone";
    /// <summary>The project's province, for narrowing the match; not shown.</summary>
    public string? Province { get; init; }
    /// <summary>The project's municipality: the tender must name it (audit C9, 2026-10-21).</summary>
    public string? Municipality { get; init; }
}

public sealed class SqlPursuitGrabStore : IPursuitGrabStore
{
    private const int CommandTimeoutSeconds = 30;

    // OpportunityStatus.New = 1, Pursuing = 4; CrmEngagementStage.Drafting = 1.
    // Mirrored as literals here so the Data layer stays free of the Core enum
    // dependency in raw SQL; values are stable on disk (OpportunityEnums.cs).
    // A grab and a join share the eligibility guard (New, unowned, not dismissed); only a grab creates an engagement.
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

    // The tender joins a forward pursuit (no tender yet, still Drafting): the same eligibility guard as GrabSql on the
    // opportunity, and the pursuit's owner, read under lock, becomes the tender's owner whoever pressed the button.
    private const string JoinSql = @"
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

DECLARE @owner nvarchar(300) =
    (SELECT OwnerStaffId FROM opportunities.CrmEngagements WITH (UPDLOCK, HOLDLOCK)
     WHERE Id = @eng AND Stage = 1 AND OpportunityId IS NULL);

UPDATE opportunities.Opportunities
   SET OwnerStaffId     = @owner,
       Status           = 4,                      -- Pursuing
       PursuingSinceUtc  = SYSDATETIMEOFFSET(),
       UpdatedAtUtc      = SYSDATETIMEOFFSET(),
       UpdatedBy         = @me
 WHERE Id = @id
   AND @owner IS NOT NULL
   AND Status = 1
   AND OwnerStaffId IS NULL
   AND DismissedAtUtc IS NULL;

IF @@ROWCOUNT = 0
BEGIN
    ROLLBACK TRANSACTION;
    SELECT CAST(0 AS bigint) AS EngagementId;
END
ELSE
BEGIN
    UPDATE e
       SET OpportunityId       = @id,
           BuyerCanonicalOrgId = COALESCE(e.BuyerCanonicalOrgId, o.BuyerCanonicalOrgId),
           UpdatedAtUtc        = SYSDATETIMEOFFSET(),
           UpdatedBy           = @me
      FROM opportunities.CrmEngagements e
      JOIN opportunities.Opportunities o ON o.Id = @id
     WHERE e.Id = @eng;

    INSERT INTO opportunities.OpportunityAssignmentLog
        (OpportunityId, EngagementId, Action, ToStaffId, ByStaffId, Reason)
    VALUES (@id, @eng, N'Join', @owner, @me, N'The RFP joined the forward pursuit');

    COMMIT TRANSACTION;
    SELECT @eng AS EngagementId;
END";

    // The latest grab or join of each live pursuit, still with its owner, made by someone other than the manager, and not
    // kept or reassigned since.
    internal const string RecentGrabsSql = @"
SELECT g.EngagementId, COALESCE(o.Name, fwd.ProjectName, N'(unnamed)'), COALESCE(o.BuyerName, fwd.ProponentName), g.ToStaffId, who.DisplayName,
       g.AtUtc, CAST(CASE WHEN e.OpportunityId IS NULL THEN 1 ELSE 0 END AS bit),
       CASE WHEN g.Action = N'Join' THEN fwd.ProjectName END
FROM opportunities.OpportunityAssignmentLog g
JOIN opportunities.CrmEngagements e ON e.Id = g.EngagementId AND e.Stage IN (1, 3) AND e.OwnerStaffId = g.ToStaffId
LEFT JOIN opportunities.Opportunities o ON o.Id = e.OpportunityId
OUTER APPLY (SELECT TOP (1) m.ProjectName, m.ProponentName
             FROM opportunities.CrmEngagementProjectLink l JOIN opportunities.MajorProjectsInventory m ON m.Id = l.MajorProjectsInventoryId
             WHERE l.EngagementId = e.Id AND l.MatchedBy = N'Grab') fwd
OUTER APPLY (SELECT TOP (1) s.DisplayName FROM opportunities.BdStaff s WHERE s.Mailbox = g.ToStaffId COLLATE DATABASE_DEFAULT) who
WHERE g.Action IN (N'Grab', N'Join')
  AND g.AtUtc >= DATEADD(day, -14, SYSDATETIMEOFFSET())
  AND g.ToStaffId <> @manager
  AND g.Id = (SELECT MAX(x.Id) FROM opportunities.OpportunityAssignmentLog x WHERE x.EngagementId = g.EngagementId AND x.Action IN (N'Grab', N'Join'))
  AND NOT EXISTS (SELECT 1 FROM opportunities.OpportunityAssignmentLog k
                  WHERE k.EngagementId = g.EngagementId AND k.Id > g.Id
                    AND (k.Action IN (N'Kept', N'Reassign') OR (g.Action = N'Join' AND k.Action = N'JoinRejected')))
ORDER BY g.AtUtc DESC;";

    // A join is settled by the first JoinAccepted or JoinRejected after it.
    private const string JoinSettledSql = @"EXISTS (SELECT 1 FROM opportunities.OpportunityAssignmentLog k
                  WHERE k.EngagementId = j.EngagementId AND k.Action IN (N'JoinAccepted', N'JoinRejected') AND k.Id > j.Id)";

    internal const string PendingJoinsSql = @"
SELECT j.EngagementId, j.OpportunityId, o.Name, fwd.ProjectName, j.ByStaffId, who.DisplayName, j.AtUtc
FROM opportunities.OpportunityAssignmentLog j
JOIN opportunities.CrmEngagements e ON e.Id = j.EngagementId AND e.OpportunityId = j.OpportunityId AND e.Stage IN (1, 3)
JOIN opportunities.Opportunities o ON o.Id = j.OpportunityId
OUTER APPLY (SELECT TOP (1) m.ProjectName
             FROM opportunities.CrmEngagementProjectLink l JOIN opportunities.MajorProjectsInventory m ON m.Id = l.MajorProjectsInventoryId
             WHERE l.EngagementId = e.Id AND l.MatchedBy = N'Grab') fwd
OUTER APPLY (SELECT TOP (1) s.DisplayName FROM opportunities.BdStaff s WHERE s.Mailbox = j.ByStaffId COLLATE DATABASE_DEFAULT) who
WHERE j.Action = N'Join'
  AND e.OwnerStaffId = @owner
  AND NOT " + JoinSettledSql + @"
ORDER BY j.AtUtc DESC;";

    // Locked on the pursuit so an accept and a reject of the same join cannot both land (audit C3).
    private const string AcceptJoinSql = @"
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM opportunities.CrmEngagements WITH (UPDLOCK, HOLDLOCK)
               WHERE Id = @eng AND OpportunityId = @opp AND OwnerStaffId = @by AND Stage IN (1, 3))
BEGIN
    ROLLBACK TRANSACTION;
    SELECT CAST(0 AS int);
    RETURN;
END
INSERT INTO opportunities.OpportunityAssignmentLog (OpportunityId, EngagementId, Action, ToStaffId, ByStaffId, Reason)
SELECT TOP (1) j.OpportunityId, j.EngagementId, N'JoinAccepted', j.ToStaffId, @by, N'The RFP is this project''s'
FROM opportunities.OpportunityAssignmentLog j
WHERE j.EngagementId = @eng AND j.OpportunityId = @opp AND j.Action = N'Join' AND NOT " + JoinSettledSql + @"
ORDER BY j.Id DESC;
DECLARE @n int = @@ROWCOUNT;
COMMIT TRANSACTION;
SELECT @n;";

    // "Not this project": split the tender back out as the grabber's own pursuit. What arrived for the tender after the
    // join goes with it; what the forward pursuit had before (and anything a person wrote) stays.
    internal const string RejectJoinSql = @"
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

DECLARE @joinId bigint, @grabber nvarchar(300), @joinedAt datetimeoffset, @owner nvarchar(300), @stage int;
-- The pursuit first, locked, and only for its owner and the tender the card showed (audit C3); then that tender's join.
SELECT @owner = OwnerStaffId, @stage = Stage FROM opportunities.CrmEngagements WITH (UPDLOCK, HOLDLOCK)
WHERE Id = @eng AND OpportunityId = @opp AND OwnerStaffId = @by AND Stage IN (1, 3);
SELECT TOP (1) @joinId = j.Id, @grabber = j.ByStaffId, @joinedAt = j.AtUtc
FROM opportunities.OpportunityAssignmentLog j WITH (UPDLOCK, HOLDLOCK)
WHERE j.EngagementId = @eng AND j.OpportunityId = @opp AND j.Action = N'Join' AND NOT " + JoinSettledSql + @"
ORDER BY j.Id DESC;

IF @joinId IS NULL OR @owner IS NULL OR @grabber IS NULL
BEGIN
    ROLLBACK TRANSACTION;
    SELECT CAST(0 AS bigint);
    RETURN;
END

-- A submitted bid is the tender's: the split pursuit keeps 'submitted', and the forward pursuit goes back to waiting for
-- its RFP (audit C8, 2026-10-21: the split came out at stage 1 and the forward pursuit stayed submitted with no tender).
UPDATE opportunities.CrmEngagements SET OpportunityId = NULL, Stage = 1, UpdatedAtUtc = SYSDATETIMEOFFSET(), UpdatedBy = @by WHERE Id = @eng;
IF @stage = 3 INSERT INTO opportunities.CrmEngagementStageHistory (EngagementId, Stage, ByStaffId) VALUES (@eng, 1, @by);

DECLARE @new TABLE (Id bigint);
INSERT INTO opportunities.CrmEngagements
    (OpportunityId, Stage, OwnerStaffId, BuyerCanonicalOrgId, ExternalSource, ExternalSourceKey, CreatedBy, UpdatedBy)
OUTPUT inserted.Id INTO @new
SELECT @opp, @stage, @grabber, o.BuyerCanonicalOrgId, N'Grab.Unjoined', o.OpportunityKey, @by, @by
FROM opportunities.Opportunities o WHERE o.Id = @opp;
DECLARE @newEng bigint = (SELECT TOP (1) Id FROM @new);

UPDATE opportunities.Opportunities SET OwnerStaffId = @grabber, UpdatedAtUtc = SYSDATETIMEOFFSET(), UpdatedBy = @by WHERE Id = @opp;

-- What came with the tender goes with it; what the forward owner added stays (audit C1, 2026-10-21: everything stamped after
-- the join moved, including the owner's own uploads and requirements). Files: a portal job's download (UploadedBy is the
-- job, no @) or the grabber's own. Requirements: anything not added by the owner. Fetches, reviews and review runs are about
-- the RFP by definition, so they all go.
UPDATE opportunities.OpportunityFiles     SET EngagementId = @newEng
 WHERE EngagementId = @eng AND UploadedAtUtc >= @joinedAt AND (UploadedBy NOT LIKE N'%@%' OR UploadedBy = @grabber);
UPDATE opportunities.PursuitDocumentFetch SET EngagementId = @newEng WHERE EngagementId = @eng AND FetchedAtUtc >= @joinedAt;
UPDATE opportunities.PursuitReview        SET EngagementId = @newEng WHERE EngagementId = @eng AND ReportedAtUtc >= @joinedAt;
UPDATE opportunities.BdPromptRun          SET EngagementId = @newEng WHERE EngagementId = @eng AND Kind = N'rfp' AND CreatedAtUtc >= @joinedAt;
UPDATE opportunities.PursuitRequirement   SET EngagementId = @newEng
 WHERE EngagementId = @eng AND CreatedAtUtc >= @joinedAt AND ISNULL(CreatedBy, N'') <> @owner;

-- The review's next step (it is one of the review's asks) goes with the review; a person's own next step stays.
DECLARE @reviewAsks TABLE (Ask nvarchar(500));
INSERT INTO @reviewAsks SELECT a.Ask FROM opportunities.BdAsk a
WHERE a.EngagementId = @eng AND a.AskedAtUtc >= @joinedAt AND a.AskedBy LIKE N'Claude (RFP review%';
UPDATE n SET NextActionNote = f.NextActionNote, NextActionDueUtc = f.NextActionDueUtc
  FROM opportunities.CrmEngagements n JOIN opportunities.CrmEngagements f ON f.Id = @eng
 WHERE n.Id = @newEng AND f.NextActionNote IN (SELECT Ask FROM @reviewAsks);
UPDATE opportunities.CrmEngagements SET NextActionNote = NULL, NextActionDueUtc = NULL
 WHERE Id = @eng AND NextActionNote IN (SELECT Ask FROM @reviewAsks);

-- The review's asks follow the tender; the forward owner's become the grabber's, anyone else's stay theirs.
UPDATE opportunities.BdAsk
   SET EngagementId = @newEng, ToStaffKey = CASE WHEN ToStaffKey = @owner THEN @grabber ELSE ToStaffKey END
 WHERE EngagementId = @eng AND AskedAtUtc >= @joinedAt AND AskedBy LIKE N'Claude (RFP review%';

INSERT INTO opportunities.CrmEngagementStageHistory (EngagementId, Stage, ByStaffId) VALUES (@newEng, @stage, @by);

-- The forward pursuit is a forward pursuit again, so its project must be held by its owner (audit C6, 2026-10-21): if the
-- project was released while the tender was joined, the owner holds it again; if it was dismissed or retired since,
-- the pursuit is archived like any let-go forward pursuit.
UPDATE m SET OwnerStaffId = @owner, OwnedAtUtc = SYSDATETIMEOFFSET()
  FROM opportunities.MajorProjectsInventory m
  JOIN opportunities.CrmEngagementProjectLink l ON l.MajorProjectsInventoryId = m.Id AND l.MatchedBy = N'Grab'
 WHERE l.EngagementId = @eng AND m.OwnerStaffId IS NULL AND m.DismissedAtUtc IS NULL AND m.RetiredAtUtc IS NULL;
IF NOT EXISTS (SELECT 1 FROM opportunities.CrmEngagementProjectLink l
               JOIN opportunities.MajorProjectsInventory m ON m.Id = l.MajorProjectsInventoryId
               WHERE l.EngagementId = @eng AND l.MatchedBy = N'Grab' AND m.OwnerStaffId = @owner)
BEGIN
    UPDATE opportunities.CrmEngagements SET Stage = 8, ClosedAtUtc = SYSDATETIMEOFFSET(), UpdatedAtUtc = SYSDATETIMEOFFSET(), UpdatedBy = @by WHERE Id = @eng;
    INSERT INTO opportunities.CrmEngagementStageHistory (EngagementId, Stage, ByStaffId) VALUES (@eng, 8, @by);
END
INSERT INTO opportunities.OpportunityAssignmentLog (OpportunityId, EngagementId, Action, FromStaffId, ToStaffId, ByStaffId, Reason)
VALUES (@opp, @eng, N'JoinRejected', @owner, @grabber, @by, N'Not this project: the tender is its own pursuit');
INSERT INTO opportunities.OpportunityAssignmentLog (OpportunityId, EngagementId, Action, ToStaffId, ByStaffId, Reason)
VALUES (@opp, @newEng, N'Grab', @grabber, @grabber, N'Grabbed; the join to a forward pursuit was rejected');

COMMIT TRANSACTION;
SELECT @newEng;";

    private const string KeepSql = @"
INSERT INTO opportunities.OpportunityAssignmentLog (OpportunityId, EngagementId, Action, ToStaffId, ByStaffId, Reason)
SELECT e.OpportunityId, e.Id, N'Kept', e.OwnerStaffId, @me, N'Kept with its grabber'
FROM opportunities.CrmEngagements e WHERE e.Id = @eng;";

    // Live forward pursuits (no tender yet) with their project, for the carry-over match.
    private const string ForwardSql = @"
SELECT e.Id, m.ProjectName, m.Province, e.OwnerStaffId, who.DisplayName, m.MunicipalityName
FROM opportunities.CrmEngagements e
JOIN opportunities.CrmEngagementProjectLink l ON l.EngagementId = e.Id AND l.MatchedBy = N'Grab'
JOIN opportunities.MajorProjectsInventory m ON m.Id = l.MajorProjectsInventoryId
OUTER APPLY (SELECT TOP (1) s.DisplayName FROM opportunities.BdStaff s WHERE s.Mailbox = e.OwnerStaffId COLLATE DATABASE_DEFAULT) who
WHERE e.Stage = 1 AND e.OpportunityId IS NULL AND m.ProjectName IS NOT NULL;
SELECT Name, ProjectProvince, CONCAT(Name, N' ', BuyerName, N' ', ProjectCity) FROM opportunities.Opportunities WHERE Id = @id;";

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

        await using var grabLock = await PursuitGrabLock.AcquireAsync(_connectionString, ct).ConfigureAwait(false);
        if (await FindForwardMatchAsync(opportunityId, ct).ConfigureAwait(false) is { } match)
        {
            var joined = await JoinAsync(opportunityId, match.EngagementId, staffUpn, ct).ConfigureAwait(false);
            return joined.Outcome == GrabOutcome.Grabbed ? joined with { JoinedOwner = match.Owner, JoinedProject = match.ProjectName } : joined;
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

    /// <summary>The live forward pursuit this tender is the RFP for, if any.</summary>
    internal async Task<ForwardMatch?> FindForwardMatchAsync(long opportunityId, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(ForwardSql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = opportunityId;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var pursuits = new List<ForwardMatch>();
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            pursuits.Add(new ForwardMatch(r.GetInt64(0), r.GetString(1), r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), 0)
                { Province = r.IsDBNull(2) ? null : r.GetString(2), Municipality = r.IsDBNull(5) ? null : r.GetString(5) });
        if (pursuits.Count == 0 || !await r.NextResultAsync(ct).ConfigureAwait(false) || !await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        var tender = r.GetString(0);
        var province = r.IsDBNull(1) ? null : r.GetString(1);
        return Best(tender, province, pursuits, r.IsDBNull(2) ? tender : r.GetString(2));
    }

    /// <summary>Above this a name match stands on its own; under it the tender must also name the project's place.</summary>
    internal const double NearMatch = 0.80;

    // Words in a municipality that are not the place ("City of", "District of Mission", "Invermere area").
    private static readonly HashSet<string> PlaceNoise = new(StringComparer.OrdinalIgnoreCase)
        { "city", "town", "district", "village", "county", "municipality", "regional", "area", "township", "first", "nation" };

    /// <summary>
    /// The pursuit whose project the tender names: same province when both say one, a name score at or above
    /// <see cref="ProjectNameMatch.Confident"/>, and the project's municipality named somewhere in the tender's title, buyer
    /// or city. With no municipality on file, the names must nearly match (<see cref="NearMatch"/>). Audit C9, 2026-10-21:
    /// "Burnaby Fire Hall Replacement" took "Surrey Fire Hall Replacement" at exactly 0.60 on three generic words.
    /// </summary>
    internal static ForwardMatch? Best(string tender, string? province, IEnumerable<ForwardMatch> pursuits, string? tenderPlaceText = null)
    {
        var tokens = ProjectNameMatch.Tokens(tender);
        var placeTokens = ProjectNameMatch.Tokens(tenderPlaceText ?? tender);
        return pursuits
            .Where(p => province is null || p.Province is null || string.Equals(p.Province, province, StringComparison.OrdinalIgnoreCase))
            .Select(p => p with { Score = ProjectNameMatch.Score(tender, tokens, p.ProjectName, ProjectNameMatch.Tokens(p.ProjectName)) })
            .Where(p => p.Score >= ProjectNameMatch.Confident && (NamesThePlace(p.Municipality, placeTokens) ?? p.Score >= NearMatch))
            .OrderByDescending(p => p.Score)
            .FirstOrDefault();
    }

    /// <summary>True when the tender names the municipality, false when it does not, null when there is no municipality.</summary>
    private static bool? NamesThePlace(string? municipality, HashSet<string> tenderTokens)
    {
        var place = ProjectNameMatch.Tokens(municipality ?? "").Where(t => !PlaceNoise.Contains(t)).ToList();
        return place.Count == 0 ? null : place.Any(tenderTokens.Contains);
    }

    public async Task<IReadOnlyList<RecentGrab>> RecentGrabsAsync(string manager, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(RecentGrabsSql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@manager", SqlDbType.NVarChar, 300).Value = manager.Trim();
        var rows = new List<RecentGrab>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            rows.Add(new RecentGrab(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4), r.GetDateTimeOffset(5), r.GetBoolean(6)) { JoinedProject = r.IsDBNull(7) ? null : r.GetString(7) });
        return rows;
    }

    public async Task<IReadOnlyList<PendingJoin>> PendingJoinsAsync(string owner, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(PendingJoinsSql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@owner", SqlDbType.NVarChar, 300).Value = owner.Trim();
        var rows = new List<PendingJoin>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            rows.Add(new PendingJoin(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5), r.GetDateTimeOffset(6)));
        return rows;
    }

    public async Task<bool> AcceptJoinAsync(long engagementId, long opportunityId, string by, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(AcceptJoinSql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@eng", SqlDbType.BigInt).Value = engagementId;
        cmd.Parameters.Add("@opp", SqlDbType.BigInt).Value = opportunityId;
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 300).Value = by.Trim();
        return ReadEngagementId(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false)) > 0;
    }

    public async Task<long> RejectJoinAsync(long engagementId, long opportunityId, string by, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(RejectJoinSql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@eng", SqlDbType.BigInt).Value = engagementId;
        cmd.Parameters.Add("@opp", SqlDbType.BigInt).Value = opportunityId;
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 300).Value = by.Trim();
        return ReadEngagementId(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
    }

    public async Task KeepAsync(long engagementId, string manager, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(KeepSql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@eng", SqlDbType.BigInt).Value = engagementId;
        cmd.Parameters.Add("@me", SqlDbType.NVarChar, 150).Value = manager.Trim();
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The tender joins an existing forward pursuit: the grab's guard, the pursuit's owner, documents asked for at once.</summary>
    internal async Task<GrabResult> JoinAsync(long opportunityId, long engagementId, string staffUpn, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(staffUpn))
        {
            throw new ArgumentException("A staff identity is required to grab an opportunity.", nameof(staffUpn));
        }

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(JoinSql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = opportunityId;
        cmd.Parameters.Add("@eng", SqlDbType.BigInt).Value = engagementId;
        cmd.Parameters.Add("@me", SqlDbType.NVarChar, 150).Value = staffUpn.Trim();

        var joined = ReadEngagementId(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
        if (joined > 0) await RequestDocumentsNowAsync(con, staffUpn.Trim(), ct).ConfigureAwait(false);
        return joined > 0 ? new GrabResult(GrabOutcome.Grabbed, joined) : new GrabResult(GrabOutcome.AlreadyTaken, 0);
    }
}
