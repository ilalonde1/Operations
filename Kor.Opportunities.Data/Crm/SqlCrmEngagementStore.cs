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

    private const string AllColumns = @"
Id, OpportunityId, Stage, OwnerStaffId, AssignedStaffIds,
TargetMargin, ProposedFee, ProposedHours, Notes,
OpenedAtUtc, ClosedAtUtc, OutcomeNotes,
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
        var sql = $@"
INSERT INTO opportunities.CrmEngagements
    (OpportunityId, Stage, OwnerStaffId, AssignedStaffIds,
     TargetMargin, ProposedFee, ProposedHours, Notes,
     OpenedAtUtc, ClosedAtUtc, OutcomeNotes,
     CreatedBy, UpdatedBy)
OUTPUT
    inserted.Id, inserted.OpportunityId, inserted.Stage, inserted.OwnerStaffId, inserted.AssignedStaffIds,
    inserted.TargetMargin, inserted.ProposedFee, inserted.ProposedHours, inserted.Notes,
    inserted.OpenedAtUtc, inserted.ClosedAtUtc, inserted.OutcomeNotes,
    inserted.CreatedAtUtc, inserted.CreatedBy, inserted.UpdatedAtUtc, inserted.UpdatedBy, inserted.RowVersion
VALUES
    (@oppId, @stage, @owner, @assigned,
     @margin, @fee, @hours, @notes,
     @openedAt, @closedAt, @outcomeNotes,
     @actor, @actor);";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        BindEngagementParams(cmd, engagement);
        cmd.Parameters.Add("@actor", SqlDbType.NVarChar, 150).Value = actorDisplay;

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("INSERT did not return a row.");
        }

        return MapReader(reader);
    }

    public async Task<CrmEngagement> UpdateAsync(CrmEngagement engagement, string actorDisplay, CancellationToken ct)
    {
        var sql = $@"
UPDATE opportunities.CrmEngagements
SET Stage           = @stage,
    OwnerStaffId    = @owner,
    AssignedStaffIds= @assigned,
    TargetMargin    = @margin,
    ProposedFee     = @fee,
    ProposedHours   = @hours,
    Notes           = @notes,
    OpenedAtUtc     = @openedAt,
    ClosedAtUtc     = @closedAt,
    OutcomeNotes    = @outcomeNotes,
    UpdatedAtUtc    = sysdatetimeoffset(),
    UpdatedBy       = @actor
OUTPUT
    inserted.Id, inserted.OpportunityId, inserted.Stage, inserted.OwnerStaffId, inserted.AssignedStaffIds,
    inserted.TargetMargin, inserted.ProposedFee, inserted.ProposedHours, inserted.Notes,
    inserted.OpenedAtUtc, inserted.ClosedAtUtc, inserted.OutcomeNotes,
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
        cmd.Parameters.Add("@oppId", SqlDbType.BigInt).Value = e.OpportunityId;
        cmd.Parameters.Add("@stage", SqlDbType.Int).Value = (int)e.Stage;
        cmd.Parameters.Add("@owner", SqlDbType.NVarChar, 20).Value = (object?)e.OwnerStaffId ?? DBNull.Value;
        cmd.Parameters.Add("@assigned", SqlDbType.NVarChar, 500).Value = (object?)e.AssignedStaffIds ?? DBNull.Value;
        AddDecimal(cmd, "@margin", precision: 5, scale: 2, value: e.TargetMargin);
        AddDecimal(cmd, "@fee", precision: 18, scale: 2, value: e.ProposedFee);
        AddDecimal(cmd, "@hours", precision: 10, scale: 2, value: e.ProposedHours);
        cmd.Parameters.Add("@notes", SqlDbType.NVarChar, -1).Value = (object?)e.Notes ?? DBNull.Value;
        cmd.Parameters.Add("@openedAt", SqlDbType.DateTimeOffset).Value = e.OpenedAtUtc;
        cmd.Parameters.Add("@closedAt", SqlDbType.DateTimeOffset).Value = (object?)e.ClosedAtUtc ?? DBNull.Value;
        cmd.Parameters.Add("@outcomeNotes", SqlDbType.NVarChar, -1).Value = (object?)e.OutcomeNotes ?? DBNull.Value;
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
        OpportunityId = r.GetInt64(1),
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
        CreatedAtUtc = r.GetDateTimeOffset(12),
        CreatedBy = r.GetString(13),
        UpdatedAtUtc = r.GetDateTimeOffset(14),
        UpdatedBy = r.GetString(15),
        RowVersion = (byte[])r.GetValue(16),
    };
}
