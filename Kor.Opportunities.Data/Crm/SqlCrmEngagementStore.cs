#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Crm;

public sealed class SqlCrmEngagementStore : ICrmEngagementStore
{
    private const int CommandTimeoutSeconds = 30;

    // Round 37a (BD-AUDIT-20260530-R2 T1.001): adds the five migration-48
    // BD-tracking columns to the shared column list. Order must stay in sync
    // with MapReader's ordinal access below — append-at-end keeps existing
    // call sites' Id/OpportunityId/Stage/... ordinals stable.
    //
    // Fix F2 (CRM plan 2026-07-06): the eight migration-267 outcome/provenance
    // columns finally get a read path (ordinals 22-29, appended after
    // RowVersion so nothing existing shifts). UpdateAsync's deleted.Stage
    // moves to ordinal 30 accordingly.
    private const string AllColumns = @"
Id, OpportunityId, Stage, OwnerStaffId, AssignedStaffIds,
TargetMargin, ProposedFee, ProposedHours, Notes,
OpenedAtUtc, ClosedAtUtc, OutcomeNotes,
BuyerCanonicalOrgId, Region, ProposalsSubmittedCad, ProposalsAcceptedCad, PotentialProjects,
CreatedAtUtc, CreatedBy, UpdatedAtUtc, UpdatedBy, RowVersion,
WonLostOutcome, OutcomeReason, LostToCanonicalOrgId, WonProjectWbs1,
NextActionDueUtc, NextActionNote, ExternalSource, ExternalSourceKey";

    private readonly string _connectionString;

    public SqlCrmEngagementStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<CrmEngagement>> ListAsync(CancellationToken ct)
    {
        var sql = $@"
SELECT {AllColumns}
FROM opportunities.CrmEngagements
ORDER BY UpdatedAtUtc DESC;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var rows = new List<CrmEngagement>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(MapReader(reader));
        }

        return rows;
    }

    public async Task<CrmEngagement?> GetByIdAsync(long id, CancellationToken ct)
    {
        var sql = $@"
SELECT {AllColumns}
FROM opportunities.CrmEngagements
WHERE Id = @id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? MapReader(reader) : null;
    }

    public async Task<CrmEngagement?> GetByOpportunityAsync(long opportunityId, CancellationToken ct)
    {
        var sql = $@"
SELECT {AllColumns}
FROM opportunities.CrmEngagements
WHERE OpportunityId = @oppId;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@oppId", SqlDbType.BigInt).Value = opportunityId;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? MapReader(reader) : null;
    }

    public async Task<CrmEngagement> InsertAsync(CrmEngagement engagement, string actorDisplay, CancellationToken ct)
    {
        var hasBdTrackingNaturalKey = engagement.OpportunityId is null
            && engagement.BuyerCanonicalOrgId is not null
            && !string.IsNullOrWhiteSpace(engagement.OwnerStaffId)
            && !string.IsNullOrWhiteSpace(engagement.Region);

        if (hasBdTrackingNaturalKey)
        {
            var existing = await GetByBdTrackingNaturalKeyAsync(
                engagement.BuyerCanonicalOrgId.Value,
                engagement.OwnerStaffId,
                engagement.Region,
                ct).ConfigureAwait(false);
            if (existing is not null)
            {
                return existing;
            }
        }

        var sql = $@"
INSERT INTO opportunities.CrmEngagements
    (OpportunityId, Stage, OwnerStaffId, AssignedStaffIds,
     TargetMargin, ProposedFee, ProposedHours, Notes,
     OpenedAtUtc, ClosedAtUtc, OutcomeNotes,
     BuyerCanonicalOrgId, Region, ProposalsSubmittedCad, ProposalsAcceptedCad, PotentialProjects,
     WonLostOutcome, OutcomeReason, LostToCanonicalOrgId, WonProjectWbs1,
     NextActionDueUtc, NextActionNote, ExternalSource, ExternalSourceKey,
     CreatedBy, UpdatedBy)
OUTPUT
    inserted.Id, inserted.OpportunityId, inserted.Stage, inserted.OwnerStaffId, inserted.AssignedStaffIds,
    inserted.TargetMargin, inserted.ProposedFee, inserted.ProposedHours, inserted.Notes,
    inserted.OpenedAtUtc, inserted.ClosedAtUtc, inserted.OutcomeNotes,
    inserted.BuyerCanonicalOrgId, inserted.Region, inserted.ProposalsSubmittedCad, inserted.ProposalsAcceptedCad, inserted.PotentialProjects,
    inserted.CreatedAtUtc, inserted.CreatedBy, inserted.UpdatedAtUtc, inserted.UpdatedBy, inserted.RowVersion,
    inserted.WonLostOutcome, inserted.OutcomeReason, inserted.LostToCanonicalOrgId, inserted.WonProjectWbs1,
    inserted.NextActionDueUtc, inserted.NextActionNote, inserted.ExternalSource, inserted.ExternalSourceKey
VALUES
    (@oppId, @stage, @owner, @assigned,
     @margin, @fee, @hours, @notes,
     @openedAt, @closedAt, @outcomeNotes,
     @buyerCanonicalOrgId, @region, @proposalsSubmittedCad, @proposalsAcceptedCad, @potentialProjects,
     @wonLostOutcome, @outcomeReason, @lostToCanonicalOrgId, @wonProjectWbs1,
     @nextActionDueUtc, @nextActionNote, @externalSource, @externalSourceKey,
     @actor, @actor);";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        // Engagement insert + its stage-history row commit together — a history
        // failure must not leave a committed engagement behind a thrown "save
        // failed" (Codex P1 on the audit-fix review).
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con, tx) { CommandTimeout = CommandTimeoutSeconds };
        BindEngagementParams(cmd, engagement);
        cmd.Parameters.Add("@actor", SqlDbType.NVarChar, 150).Value = actorDisplay;

        SqlDataReader reader;
        try
        {
            reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        }
        catch (SqlException ex) when (hasBdTrackingNaturalKey && ex.Number is 2601 or 2627)
        {
            await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            var existing = await GetByBdTrackingNaturalKeyAsync(
                engagement.BuyerCanonicalOrgId!.Value,
                engagement.OwnerStaffId!,
                engagement.Region!,
                ct).ConfigureAwait(false);
            if (existing is not null)
            {
                return existing;
            }

            throw;
        }

        CrmEngagement inserted;
        await using (reader)
        {
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                throw new InvalidOperationException("INSERT did not return a row.");
            }

            inserted = MapReader(reader);
        }

        // Record the opening stage so stage-age analytics have a start point
        // (audit 2026-07-01 M2 — the history table previously had no writers).
        await WriteStageHistoryAsync(con, tx, inserted.Id, (int)inserted.Stage, actorDisplay, ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return inserted;
    }

    private static async Task WriteStageHistoryAsync(
        SqlConnection con, SqlTransaction tx, long engagementId, int stage, string actorDisplay, CancellationToken ct)
    {
        const string sql = @"
INSERT INTO opportunities.CrmEngagementStageHistory (EngagementId, Stage, ByStaffId)
VALUES (@eng, @stage, @by);";

        await using var cmd = new SqlCommand(sql, con, tx) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@eng", SqlDbType.BigInt).Value = engagementId;
        cmd.Parameters.Add("@stage", SqlDbType.Int).Value = stage;
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 150).Value = actorDisplay;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<CrmEngagement?> GetByBdTrackingNaturalKeyAsync(
        long buyerCanonicalOrgId,
        string ownerStaffId,
        string region,
        CancellationToken ct)
    {
        // No lock hints: in autocommit mode UPDLOCK/HOLDLOCK release at statement
        // end, so they never provided the check-then-insert protection they
        // implied. Race safety for InsertAsync rests (correctly) on the unique
        // BD-relationship index + its 2601/2627 catch.
        var sql = $@"
SELECT {AllColumns}
FROM opportunities.CrmEngagements
WHERE OpportunityId IS NULL
  AND BuyerCanonicalOrgId = @buyerCanonicalOrgId
  AND OwnerStaffId = @ownerStaffId
  AND Region = @region;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@buyerCanonicalOrgId", SqlDbType.BigInt).Value = buyerCanonicalOrgId;
        cmd.Parameters.Add("@ownerStaffId", SqlDbType.NVarChar, 150).Value = ownerStaffId;
        cmd.Parameters.Add("@region", SqlDbType.NVarChar, 40).Value = region;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? MapReader(reader) : null;
    }

    public async Task<CrmEngagement> UpdateAsync(CrmEngagement engagement, string actorDisplay, CancellationToken ct)
    {
        // Close-out columns are SERVER-owned (fix F7 + adversarial-review fix
        // 2026-07-07): the stage decides what outcome data can exist, so no
        // caller — either close door, a stale model, or a future path — can
        // commit a contradictory row (e.g. Stage=Won carrying WonLostOutcome=
        // Lost + a phantom competitor after a reopen→re-close cycle).
        //   ClosedAtUtc: entering terminal stamps once (honouring a caller-
        //     supplied historical date — seed importers — else now); staying
        //     terminal preserves the original; reopening clears it.
        //   WonLostOutcome: Won forces 1; Lost accepts 2/3/4 (Lost/NoBid/
        //     Withdrawn); non-terminal clears.
        //   LostToCanonicalOrgId: Lost only. WonProjectWbs1: Won only.
        //   OutcomeReason: terminal only.
        //
        // ExternalSource/ExternalSourceKey are deliberately NOT in the SET list:
        // provenance is immutable after insert (fix F2).
        var sql = $@"
UPDATE opportunities.CrmEngagements
SET Stage                 = @stage,
    OwnerStaffId          = @owner,
    AssignedStaffIds      = @assigned,
    TargetMargin          = @margin,
    ProposedFee           = @fee,
    ProposedHours         = @hours,
    Notes                 = @notes,
    OpenedAtUtc           = @openedAt,
    ClosedAtUtc           = CASE WHEN @stage IN (6, 7)
                                 THEN CASE WHEN Stage IN (6, 7) THEN ClosedAtUtc ELSE COALESCE(@closedAt, sysdatetimeoffset()) END
                                 ELSE NULL END,
    OutcomeNotes          = @outcomeNotes,
    BuyerCanonicalOrgId   = @buyerCanonicalOrgId,
    Region                = @region,
    ProposalsSubmittedCad = @proposalsSubmittedCad,
    ProposalsAcceptedCad  = @proposalsAcceptedCad,
    PotentialProjects     = @potentialProjects,
    WonLostOutcome        = CASE WHEN @stage = 6 THEN 1
                                 WHEN @stage = 7 THEN CASE WHEN @wonLostOutcome IN (2, 3, 4) THEN @wonLostOutcome ELSE NULL END
                                 ELSE NULL END,
    OutcomeReason         = CASE WHEN @stage IN (6, 7) THEN @outcomeReason ELSE NULL END,
    LostToCanonicalOrgId  = CASE WHEN @stage = 7 THEN @lostToCanonicalOrgId ELSE NULL END,
    WonProjectWbs1        = CASE WHEN @stage = 6 THEN @wonProjectWbs1 ELSE NULL END,
    NextActionDueUtc      = @nextActionDueUtc,
    NextActionNote        = @nextActionNote,
    UpdatedAtUtc          = sysdatetimeoffset(),
    UpdatedBy             = @actor
OUTPUT
    inserted.Id, inserted.OpportunityId, inserted.Stage, inserted.OwnerStaffId, inserted.AssignedStaffIds,
    inserted.TargetMargin, inserted.ProposedFee, inserted.ProposedHours, inserted.Notes,
    inserted.OpenedAtUtc, inserted.ClosedAtUtc, inserted.OutcomeNotes,
    inserted.BuyerCanonicalOrgId, inserted.Region, inserted.ProposalsSubmittedCad, inserted.ProposalsAcceptedCad, inserted.PotentialProjects,
    inserted.CreatedAtUtc, inserted.CreatedBy, inserted.UpdatedAtUtc, inserted.UpdatedBy, inserted.RowVersion,
    inserted.WonLostOutcome, inserted.OutcomeReason, inserted.LostToCanonicalOrgId, inserted.WonProjectWbs1,
    inserted.NextActionDueUtc, inserted.NextActionNote, inserted.ExternalSource, inserted.ExternalSourceKey,
    deleted.Stage
WHERE Id = @id AND RowVersion = @rv;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        // Update + stage-history commit together (Codex P1 on the audit-fix review).
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con, tx) { CommandTimeout = CommandTimeoutSeconds };
        BindEngagementParams(cmd, engagement);
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = engagement.Id;
        cmd.Parameters.Add("@rv", SqlDbType.Binary, 8).Value = engagement.RowVersion;
        cmd.Parameters.Add("@actor", SqlDbType.NVarChar, 150).Value = actorDisplay;

        CrmEngagement? updated = null;
        var previousStage = 0;
        await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                updated = MapReader(reader);
                previousStage = reader.GetInt32(30); // deleted.Stage, appended after the row image (22-29 = outcome columns)
            }
        }

        if (updated is null)
        {
            // Reader must be disposed before the rollback can use the connection.
            await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw new CrmConcurrencyException(nameof(CrmEngagement), engagement.Id);
        }

        // Record the transition so stage-age analytics have a timeline
        // (audit 2026-07-01 M2 — the history table previously had no writers).
        if ((int)updated.Stage != previousStage)
        {
            await WriteStageHistoryAsync(con, tx, updated.Id, (int)updated.Stage, actorDisplay, ct).ConfigureAwait(false);
        }

        // Audit-v2 #3: a closed engagement must reach the Opportunities ledger the
        // win-rate KPI reads — without this write-through the parent opportunity
        // stays "Pursuing" forever and two dashboards answer "did we win?"
        // differently. Same transaction as the stage change; CrmEngagementStage and
        // OpportunityStatus share Won=6 / Lost=7 values by design (CrmEngagement.cs).
        if ((int)updated.Stage != previousStage
            && updated.OpportunityId is long parentOppId
            && updated.Stage is CrmEngagementStage.Won or CrmEngagementStage.Lost)
        {
            await using var statusSync = new SqlCommand(
                @"UPDATE opportunities.Opportunities
                  SET Status = @status, UpdatedAtUtc = sysdatetimeoffset()
                  WHERE Id = @oppId AND Status <> @status;",
                con, tx)
            { CommandTimeout = CommandTimeoutSeconds };
            statusSync.Parameters.Add("@status", SqlDbType.Int).Value = (int)updated.Stage;
            statusSync.Parameters.Add("@oppId", SqlDbType.BigInt).Value = parentOppId;
            await statusSync.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
        return updated;
    }

    private static void BindEngagementParams(SqlCommand cmd, CrmEngagement e)
    {
        // OpportunityId is nullable post-migration 48 (BD-tracking engagements
        // have no parent RFP). Convert null -> DBNull explicitly.
        cmd.Parameters.Add("@oppId", SqlDbType.BigInt).Value = (object?)e.OpportunityId ?? DBNull.Value;
        cmd.Parameters.Add("@stage", SqlDbType.Int).Value = (int)e.Stage;
        cmd.Parameters.Add("@owner", SqlDbType.NVarChar, 150).Value = (object?)e.OwnerStaffId ?? DBNull.Value;
        cmd.Parameters.Add("@assigned", SqlDbType.NVarChar, 500).Value = (object?)e.AssignedStaffIds ?? DBNull.Value;
        AddDecimal(cmd, "@margin", precision: 5, scale: 2, value: e.TargetMargin);
        AddDecimal(cmd, "@fee", precision: 18, scale: 2, value: e.ProposedFee);
        AddDecimal(cmd, "@hours", precision: 10, scale: 2, value: e.ProposedHours);
        cmd.Parameters.Add("@notes", SqlDbType.NVarChar, -1).Value = (object?)e.Notes ?? DBNull.Value;
        cmd.Parameters.Add("@openedAt", SqlDbType.DateTimeOffset).Value = e.OpenedAtUtc;
        cmd.Parameters.Add("@closedAt", SqlDbType.DateTimeOffset).Value = (object?)e.ClosedAtUtc ?? DBNull.Value;
        cmd.Parameters.Add("@outcomeNotes", SqlDbType.NVarChar, -1).Value = (object?)e.OutcomeNotes ?? DBNull.Value;
        // Round 37a (T1.001): migration-48 BD-tracking columns.
        cmd.Parameters.Add("@buyerCanonicalOrgId", SqlDbType.BigInt).Value = (object?)e.BuyerCanonicalOrgId ?? DBNull.Value;
        cmd.Parameters.Add("@region", SqlDbType.NVarChar, 40).Value = (object?)e.Region ?? DBNull.Value;
        AddDecimal(cmd, "@proposalsSubmittedCad", precision: 18, scale: 2, value: e.ProposalsSubmittedCad);
        AddDecimal(cmd, "@proposalsAcceptedCad", precision: 18, scale: 2, value: e.ProposalsAcceptedCad);
        cmd.Parameters.Add("@potentialProjects", SqlDbType.NVarChar, -1).Value = (object?)e.PotentialProjects ?? DBNull.Value;
        // Fix F2: migration-267 outcome/provenance columns. @externalSource/Key
        // are referenced by INSERT only (UPDATE treats provenance as immutable);
        // unreferenced parameters on the UPDATE command are harmless.
        cmd.Parameters.Add("@wonLostOutcome", SqlDbType.Int).Value = (object?)(int?)e.WonLostOutcome ?? DBNull.Value;
        cmd.Parameters.Add("@outcomeReason", SqlDbType.NVarChar, 2000).Value = (object?)e.OutcomeReason ?? DBNull.Value;
        cmd.Parameters.Add("@lostToCanonicalOrgId", SqlDbType.BigInt).Value = (object?)e.LostToCanonicalOrgId ?? DBNull.Value;
        cmd.Parameters.Add("@wonProjectWbs1", SqlDbType.NVarChar, 50).Value = (object?)e.WonProjectWbs1 ?? DBNull.Value;
        cmd.Parameters.Add("@nextActionDueUtc", SqlDbType.DateTimeOffset).Value = (object?)e.NextActionDueUtc ?? DBNull.Value;
        cmd.Parameters.Add("@nextActionNote", SqlDbType.NVarChar, 500).Value = (object?)e.NextActionNote ?? DBNull.Value;
        cmd.Parameters.Add("@externalSource", SqlDbType.NVarChar, 50).Value = (object?)e.ExternalSource ?? DBNull.Value;
        cmd.Parameters.Add("@externalSourceKey", SqlDbType.NVarChar, 200).Value = (object?)e.ExternalSourceKey ?? DBNull.Value;
    }

    private static void AddDecimal(SqlCommand cmd, string name, byte precision, byte scale, decimal? value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.Decimal);
        p.Precision = precision;
        p.Scale = scale;
        p.Value = (object?)value ?? DBNull.Value;
    }

    private static CrmEngagement MapReader(SqlDataReader r) => new()
    {
        Id = r.GetInt64(0),
        // Migration 48 made OpportunityId nullable (BD-tracking engagements
        // have no parent Opportunity). MapReader respects null on the read
        // path; insert/update paths convert null -> DBNull below.
        OpportunityId = r.IsDBNull(1) ? null : r.GetInt64(1),
        Stage = (CrmEngagementStage)r.GetInt32(2),
        OwnerStaffId = r.IsDBNull(3) ? null : r.GetString(3),
        AssignedStaffIds = r.IsDBNull(4) ? null : r.GetString(4),
        TargetMargin = r.IsDBNull(5) ? null : r.GetDecimal(5),
        ProposedFee = r.IsDBNull(6) ? null : r.GetDecimal(6),
        ProposedHours = r.IsDBNull(7) ? null : r.GetDecimal(7),
        Notes = r.IsDBNull(8) ? null : r.GetString(8),
        OpenedAtUtc = r.GetDateTimeOffset(9),
        ClosedAtUtc = r.IsDBNull(10) ? null : r.GetDateTimeOffset(10),
        OutcomeNotes = r.IsDBNull(11) ? null : r.GetString(11),
        // Round 37a (T1.001): migration-48 BD-tracking columns at ordinals 12-16.
        BuyerCanonicalOrgId = r.IsDBNull(12) ? null : r.GetInt64(12),
        Region = r.IsDBNull(13) ? null : r.GetString(13),
        ProposalsSubmittedCad = r.IsDBNull(14) ? null : r.GetDecimal(14),
        ProposalsAcceptedCad = r.IsDBNull(15) ? null : r.GetDecimal(15),
        PotentialProjects = r.IsDBNull(16) ? null : r.GetString(16),
        CreatedAtUtc = r.GetDateTimeOffset(17),
        CreatedBy = r.GetString(18),
        UpdatedAtUtc = r.GetDateTimeOffset(19),
        UpdatedBy = r.GetString(20),
        RowVersion = (byte[])r.GetValue(21),
        // Fix F2: migration-267 outcome/provenance columns at ordinals 22-29.
        WonLostOutcome = r.IsDBNull(22) ? null : (WonLostOutcome)r.GetInt32(22),
        OutcomeReason = r.IsDBNull(23) ? null : r.GetString(23),
        LostToCanonicalOrgId = r.IsDBNull(24) ? null : r.GetInt64(24),
        WonProjectWbs1 = r.IsDBNull(25) ? null : r.GetString(25),
        NextActionDueUtc = r.IsDBNull(26) ? null : r.GetDateTimeOffset(26),
        NextActionNote = r.IsDBNull(27) ? null : r.GetString(27),
        ExternalSource = r.IsDBNull(28) ? null : r.GetString(28),
        ExternalSourceKey = r.IsDBNull(29) ? null : r.GetString(29),
    };
}
