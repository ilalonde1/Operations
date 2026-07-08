#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Crm;

/// <summary>One buyer's nightly email-warmth rollup (plan 3.1). Aggregates
/// only — counts, dates, one correspondent address; never bodies.</summary>
public sealed record BuyerEmailWarmthRow(
    long CanonicalOrgId,
    string Domain,
    DateTimeOffset? LastTouchUtc,
    DateTimeOffset? LastInboundUtc,
    int Emails90d,
    long EmailsAllTime,
    string? TopCorrespondent,
    DateTimeOffset ComputedAtUtc);

/// <summary>One claim/reassign audit event (plan 2.2b — first reader of
/// opportunities.OpportunityAssignmentLog).</summary>
public sealed record PursuitAssignmentRow(
    string Action,
    string? FromStaffId,
    string? ToStaffId,
    string? ByStaffId,
    DateTimeOffset AtUtc);

/// <summary>
/// Read-only context for the CRM detail panel (plan 2.2a/2.2b/3.1): the
/// buyer's email warmth, the pursuit's claim/reassign history, and when the
/// current stage was entered. Pure readers over tables other components write.
/// </summary>
public interface IPursuitContextStore
{
    Task<BuyerEmailWarmthRow?> GetWarmthAsync(long canonicalOrgId, CancellationToken ct);

    Task<IReadOnlyList<PursuitAssignmentRow>> ListAssignmentsAsync(long engagementId, CancellationToken ct);

    /// <summary>When the engagement entered its CURRENT stage — newest history
    /// row for that stage, or null when no history exists (caller falls back
    /// to OpenedAtUtc per the F14 COALESCE contract).</summary>
    Task<DateTimeOffset?> GetStageSinceAsync(long engagementId, int stage, CancellationToken ct);
}

public sealed class SqlPursuitContextStore : IPursuitContextStore
{
    private const int CommandTimeoutSeconds = 30;

    private readonly string _connectionString;

    public SqlPursuitContextStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<BuyerEmailWarmthRow?> GetWarmthAsync(long canonicalOrgId, CancellationToken ct)
    {
        const string sql = @"
SELECT CanonicalOrgId, Domain, LastTouchUtc, LastInboundUtc, Emails90d, EmailsAllTime, TopCorrespondent, ComputedAtUtc
FROM opportunities.CrmBuyerEmailWarmth
WHERE CanonicalOrgId = @orgId;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@orgId", SqlDbType.BigInt).Value = canonicalOrgId;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return new BuyerEmailWarmthRow(
            CanonicalOrgId: r.GetInt64(0),
            Domain: r.GetString(1),
            LastTouchUtc: r.IsDBNull(2) ? null : r.GetDateTimeOffset(2),
            LastInboundUtc: r.IsDBNull(3) ? null : r.GetDateTimeOffset(3),
            Emails90d: r.GetInt32(4),
            EmailsAllTime: r.GetInt64(5),
            TopCorrespondent: r.IsDBNull(6) ? null : r.GetString(6),
            ComputedAtUtc: r.GetDateTimeOffset(7));
    }

    public async Task<IReadOnlyList<PursuitAssignmentRow>> ListAssignmentsAsync(long engagementId, CancellationToken ct)
    {
        const string sql = @"
SELECT Action, FromStaffId, ToStaffId, ByStaffId, AtUtc
FROM opportunities.OpportunityAssignmentLog
WHERE EngagementId = @eng
ORDER BY AtUtc ASC;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@eng", SqlDbType.BigInt).Value = engagementId;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var rows = new List<PursuitAssignmentRow>();
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new PursuitAssignmentRow(
                Action: r.GetString(0),
                FromStaffId: r.IsDBNull(1) ? null : r.GetString(1),
                ToStaffId: r.IsDBNull(2) ? null : r.GetString(2),
                ByStaffId: r.IsDBNull(3) ? null : r.GetString(3),
                AtUtc: r.GetDateTimeOffset(4)));
        }

        return rows;
    }

    public async Task<DateTimeOffset?> GetStageSinceAsync(long engagementId, int stage, CancellationToken ct)
    {
        const string sql = @"
SELECT MAX(EnteredAtUtc)
FROM opportunities.CrmEngagementStageHistory
WHERE EngagementId = @eng AND Stage = @stage;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@eng", SqlDbType.BigInt).Value = engagementId;
        cmd.Parameters.Add("@stage", SqlDbType.Int).Value = stage;
        var scalar = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return scalar is DateTimeOffset dto ? dto : null;
    }
}
