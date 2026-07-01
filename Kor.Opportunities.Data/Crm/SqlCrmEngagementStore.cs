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
    // with MapReader's ordinal access below — append-at-end-before-audit keeps
    // existing call sites' Id/OpportunityId/Stage/... ordinals stable.
    private const string AllColumns = @"
Id, OpportunityId, Stage, OwnerStaffId, AssignedStaffIds,
TargetMargin, ProposedFee, ProposedHours, Notes,
OpenedAtUtc, ClosedAtUtc, OutcomeNotes,
BuyerCanonicalOrgId, Region, ProposalsSubmittedCad, ProposalsAcceptedCad, PotentialProjects,
CreatedAtUtc, CreatedBy, UpdatedAtUtc, UpdatedBy, RowVersion";

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
     CreatedBy, UpdatedBy)
OUTPUT
    inserted.Id, inserted.OpportunityId, inserted.Stage, inserted.OwnerStaffId, inserted.AssignedStaffIds,
    inserted.TargetMargin, inserted.ProposedFee, inserted.ProposedHours, inserted.Notes,
    inserted.OpenedAtUtc, inserted.ClosedAtUtc, inserted.OutcomeNotes,
    inserted.BuyerCanonicalOrgId, inserted.Region, inserted.ProposalsSubmittedCad, inserted.ProposalsAcceptedCad, inserted.PotentialProjects,
    inserted.CreatedAtUtc, inserted.CreatedBy, inserted.UpdatedAtUtc, inserted.UpdatedBy, inserted.RowVersion
VALUES
    (@oppId, @stage, @owner, @assigned,
     @margin, @fee, @hours, @notes,
     @openedAt, @closedAt, @outcomeNotes,
     @buyerCanonicalOrgId, @region, @proposalsSubmittedCad, @proposalsAcceptedCad, @potentialProjects,
     @actor, @actor);";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        BindEngagementParams(cmd, engagement);
        cmd.Parameters.Add("@actor", SqlDbType.NVarChar, 150).Value = actorDisplay;

        SqlDataReader reader;
        try
        {
            reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        }
        catch (SqlException ex) when (hasBdTrackingNaturalKey && ex.Number is 2601 or 2627)
        {
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

        await using (reader)
        {
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                throw new InvalidOperationException("INSERT did not return a row.");
            }

            return MapReader(reader);
        }
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
    ClosedAtUtc           = @closedAt,
    OutcomeNotes          = @outcomeNotes,
    BuyerCanonicalOrgId   = @buyerCanonicalOrgId,
    Region                = @region,
    ProposalsSubmittedCad = @proposalsSubmittedCad,
    ProposalsAcceptedCad  = @proposalsAcceptedCad,
    PotentialProjects     = @potentialProjects,
    UpdatedAtUtc          = sysdatetimeoffset(),
    UpdatedBy             = @actor
OUTPUT
    inserted.Id, inserted.OpportunityId, inserted.Stage, inserted.OwnerStaffId, inserted.AssignedStaffIds,
    inserted.TargetMargin, inserted.ProposedFee, inserted.ProposedHours, inserted.Notes,
    inserted.OpenedAtUtc, inserted.ClosedAtUtc, inserted.OutcomeNotes,
    inserted.BuyerCanonicalOrgId, inserted.Region, inserted.ProposalsSubmittedCad, inserted.ProposalsAcceptedCad, inserted.PotentialProjects,
    inserted.CreatedAtUtc, inserted.CreatedBy, inserted.UpdatedAtUtc, inserted.UpdatedBy, inserted.RowVersion
WHERE Id = @id AND RowVersion = @rv;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        BindEngagementParams(cmd, engagement);
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = engagement.Id;
        cmd.Parameters.Add("@rv", SqlDbType.Binary, 8).Value = engagement.RowVersion;
        cmd.Parameters.Add("@actor", SqlDbType.NVarChar, 150).Value = actorDisplay;

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new CrmConcurrencyException(nameof(CrmEngagement), engagement.Id);
        }

        return MapReader(reader);
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
    };
}
