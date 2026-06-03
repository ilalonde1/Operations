#nullable enable
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Awards;

public sealed class SqlArchitectDisplacementBriefStore : IArchitectDisplacementBriefStore
{
    private const int CommandTimeoutSeconds = 30;
    private readonly string _connectionString;

    public SqlArchitectDisplacementBriefStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
    }

    public async Task<ArchitectDisplacementBrief?> GetByArchitectAsync(long architectCanonicalOrgId, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP 1 Id, ArchitectCanonicalOrgId, Market, KorPriority, ConfidenceScore,
       BriefJson, GeneratedAtUtc, UpdatedAtUtc
FROM   opportunities.ArchitectDisplacementBriefs
WHERE  ArchitectCanonicalOrgId = @id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = architectCanonicalOrgId;

        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;

        return new ArchitectDisplacementBrief(
            r.GetInt64(0),
            r.GetInt64(1),
            r.IsDBNull(2) ? null : r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3),
            r.IsDBNull(4) ? null : r.GetDecimal(4),
            r.GetString(5),
            r.GetDateTimeOffset(6),
            r.GetDateTimeOffset(7));
    }

    public async Task UpsertAsync(
        long architectCanonicalOrgId,
        string? market,
        string? korPriority,
        decimal? confidenceScore,
        string briefJson,
        DateTimeOffset generatedAtUtc,
        CancellationToken ct)
    {
        // Round 14c pattern - UPDATE under UPDLOCK,HOLDLOCK; if no row, INSERT.
        // Single statement keeps the lock scope tight without sp_getapplock.
        const string sql = @"
MERGE opportunities.ArchitectDisplacementBriefs WITH (UPDLOCK, HOLDLOCK) AS target
USING (SELECT @architectId AS ArchitectCanonicalOrgId) AS src
   ON target.ArchitectCanonicalOrgId = src.ArchitectCanonicalOrgId
WHEN MATCHED THEN UPDATE SET
    Market           = @market,
    KorPriority      = @korPriority,
    ConfidenceScore  = @confidence,
    BriefJson        = @briefJson,
    GeneratedAtUtc   = @generatedAt,
    UpdatedAtUtc     = sysdatetimeoffset()
WHEN NOT MATCHED THEN INSERT
    (ArchitectCanonicalOrgId, Market, KorPriority, ConfidenceScore, BriefJson, GeneratedAtUtc, UpdatedAtUtc)
    VALUES (@architectId, @market, @korPriority, @confidence, @briefJson, @generatedAt, sysdatetimeoffset());";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@architectId", SqlDbType.BigInt).Value = architectCanonicalOrgId;
        cmd.Parameters.Add("@market", SqlDbType.NVarChar, 80).Value = (object?)market ?? DBNull.Value;
        cmd.Parameters.Add("@korPriority", SqlDbType.NVarChar, 20).Value = (object?)korPriority ?? DBNull.Value;
        cmd.Parameters.Add("@confidence", SqlDbType.Decimal).Value = (object?)confidenceScore ?? DBNull.Value;
        cmd.Parameters["@confidence"].Precision = 4;
        cmd.Parameters["@confidence"].Scale = 3;
        cmd.Parameters.Add("@briefJson", SqlDbType.NVarChar, -1).Value = briefJson ?? string.Empty;
        cmd.Parameters.Add("@generatedAt", SqlDbType.DateTimeOffset).Value = generatedAtUtc;

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
