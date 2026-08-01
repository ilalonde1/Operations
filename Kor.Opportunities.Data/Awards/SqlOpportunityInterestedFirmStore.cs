#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Awards;

public sealed class SqlOpportunityInterestedFirmStore : IOpportunityInterestedFirmStore
{
    private const int CommandTimeoutSeconds = 30;
    private readonly string _connectionString;

    public SqlOpportunityInterestedFirmStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
    }

    public async Task UpsertAsync(
        long opportunityId,
        string rawFirmName,
        long? resolvedCanonicalOrgId,
        string? resolvedKind,
        string sourcePortal,
        string? sourcePostingUrl,
        DateTimeOffset? expressedAtUtc,
        string? notes,
        string? rawJson,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawFirmName))
            throw new ArgumentException("Raw firm name required.", nameof(rawFirmName));

        // Round 14c pattern - UPDATE under UPDLOCK/HOLDLOCK; INSERT on no-match.
        // On match: bump LastSeenAtUtc + fill NULL canonical/kind/notes/expressed if we now have them.
        // Never overwrite an existing resolved canonical (the resolver may be wrong on a re-pass;
        // existing matches stay sticky until RepointCanonicalAsync is called explicitly).
        const string sql = @"
MERGE opportunities.OpportunityInterestedFirms WITH (UPDLOCK, HOLDLOCK) AS t
USING (SELECT @oppId AS OpportunityId, @rawName AS RawFirmName) AS s
   ON t.OpportunityId = s.OpportunityId AND t.RawFirmName = s.RawFirmName
WHEN MATCHED THEN UPDATE SET
    ResolvedCanonicalOrgId = COALESCE(t.ResolvedCanonicalOrgId, @canonicalId),
    ResolvedKind           = COALESCE(t.ResolvedKind, @resolvedKind),
    ExpressedAtUtc         = COALESCE(t.ExpressedAtUtc, @expressedAt),
    Notes                  = COALESCE(t.Notes, @notes),
    RawJson                = COALESCE(@rawJson, t.RawJson),
    LastSeenAtUtc          = sysdatetimeoffset()
WHEN NOT MATCHED THEN INSERT
    (OpportunityId, RawFirmName, ResolvedCanonicalOrgId, ResolvedKind, SourcePortal, SourcePostingUrl,
     ExpressedAtUtc, FirstSeenAtUtc, LastSeenAtUtc, Notes, RawJson)
    VALUES (@oppId, @rawName, @canonicalId, @resolvedKind, @sourcePortal, @sourceUrl,
            @expressedAt, sysdatetimeoffset(), sysdatetimeoffset(), @notes, @rawJson);";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@oppId", SqlDbType.BigInt).Value = opportunityId;
        cmd.Parameters.Add("@rawName", SqlDbType.NVarChar, 400).Value = rawFirmName.Trim();
        cmd.Parameters.Add("@canonicalId", SqlDbType.BigInt).Value = (object?)resolvedCanonicalOrgId ?? DBNull.Value;
        cmd.Parameters.Add("@resolvedKind", SqlDbType.NVarChar, 40).Value = (object?)resolvedKind ?? DBNull.Value;
        cmd.Parameters.Add("@sourcePortal", SqlDbType.NVarChar, 40).Value = sourcePortal;
        cmd.Parameters.Add("@sourceUrl", SqlDbType.NVarChar, 1000).Value = (object?)sourcePostingUrl ?? DBNull.Value;
        cmd.Parameters.Add("@expressedAt", SqlDbType.DateTimeOffset).Value = (object?)expressedAtUtc ?? DBNull.Value;
        cmd.Parameters.Add("@notes", SqlDbType.NVarChar, 1000).Value = (object?)notes ?? DBNull.Value;
        cmd.Parameters.Add("@rawJson", SqlDbType.NVarChar, -1).Value = (object?)rawJson ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OpportunityInterestedFirm>> ListByOpportunityAsync(long opportunityId, CancellationToken ct)
    {
        const string sql = @"
SELECT Id, OpportunityId, RawFirmName, ResolvedCanonicalOrgId, ResolvedKind, SourcePortal,
       SourcePostingUrl, ExpressedAtUtc, FirstSeenAtUtc, LastSeenAtUtc, Notes, RawJson
FROM   opportunities.OpportunityInterestedFirms
WHERE  OpportunityId = @id
ORDER  BY ExpressedAtUtc DESC, FirstSeenAtUtc DESC;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = opportunityId;
        return await ReadAllAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OpportunityInterestedFirm>> ListByCanonicalOrgAsync(long canonicalOrgId, int limit, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (@limit) Id, OpportunityId, RawFirmName, ResolvedCanonicalOrgId, ResolvedKind, SourcePortal,
       SourcePostingUrl, ExpressedAtUtc, FirstSeenAtUtc, LastSeenAtUtc, Notes, RawJson
FROM   opportunities.OpportunityInterestedFirms
WHERE  ResolvedCanonicalOrgId = @id
ORDER  BY ExpressedAtUtc DESC, FirstSeenAtUtc DESC;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalOrgId;
        cmd.Parameters.Add("@limit", SqlDbType.Int).Value = Math.Max(1, limit);
        return await ReadAllAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<int> RepointCanonicalAsync(long interestedFirmId, long resolvedCanonicalOrgId, string? resolvedKind, CancellationToken ct)
    {
        const string sql = @"
UPDATE opportunities.OpportunityInterestedFirms
SET    ResolvedCanonicalOrgId = @canonicalId,
       ResolvedKind = COALESCE(@resolvedKind, ResolvedKind),
       LastSeenAtUtc = sysdatetimeoffset()
WHERE  Id = @id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = interestedFirmId;
        cmd.Parameters.Add("@canonicalId", SqlDbType.BigInt).Value = resolvedCanonicalOrgId;
        cmd.Parameters.Add("@resolvedKind", SqlDbType.NVarChar, 40).Value = (object?)resolvedKind ?? DBNull.Value;
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<OpportunityInterestedFirm>> ReadAllAsync(SqlCommand cmd, CancellationToken ct)
    {
        var list = new List<OpportunityInterestedFirm>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new OpportunityInterestedFirm(
                Id: r.GetInt64(0),
                OpportunityId: r.GetInt64(1),
                RawFirmName: r.GetString(2),
                ResolvedCanonicalOrgId: r.IsDBNull(3) ? null : r.GetInt64(3),
                ResolvedKind: r.IsDBNull(4) ? null : r.GetString(4),
                SourcePortal: r.GetString(5),
                SourcePostingUrl: r.IsDBNull(6) ? null : r.GetString(6),
                ExpressedAtUtc: r.IsDBNull(7) ? null : r.GetDateTimeOffset(7),
                FirstSeenAtUtc: r.GetDateTimeOffset(8),
                LastSeenAtUtc: r.GetDateTimeOffset(9),
                Notes: r.IsDBNull(10) ? null : r.GetString(10),
                RawJson: r.IsDBNull(11) ? null : r.GetString(11)));
        }
        return list;
    }
}
