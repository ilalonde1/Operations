#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Awards;

public sealed class SqlEnrichmentTrackingStore : IEnrichmentTrackingStore
{
    private const int CommandTimeoutSeconds = 30;
    private readonly string _connectionString;

    public SqlEnrichmentTrackingStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<long>> ListDueAsync(string providerName, int batchSize, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (@batch) co.Id
FROM   opportunities.CanonicalOrg co
LEFT   JOIN opportunities.CanonicalOrgEnrichment e
       ON e.CanonicalOrgId = co.Id AND e.ProviderName = @provider
WHERE  (e.Id IS NULL)
   OR  (e.Status NOT IN ('blocked') AND (e.NextRefreshAtUtc IS NULL OR e.NextRefreshAtUtc <= sysdatetimeoffset()))
ORDER  BY CASE WHEN e.Id IS NULL THEN 0 ELSE 1 END,
          COALESCE(e.LastAttemptAtUtc, '1900-01-01'),
          co.Id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@batch", SqlDbType.Int).Value = batchSize;
        cmd.Parameters.Add("@provider", SqlDbType.NVarChar, 60).Value = providerName;

        var list = new List<long>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(r.GetInt64(0));
        }

        return list;
    }

    public async Task<EnrichmentTrackingRow?> GetAsync(long canonicalOrgId, string providerName, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP 1 Id, CanonicalOrgId, ProviderName, Status,
       LastRefreshAtUtc, LastAttemptAtUtc, NextRefreshAtUtc,
       Attempts, ErrorMessage, ResultJson
FROM   opportunities.CanonicalOrgEnrichment
WHERE  CanonicalOrgId = @id AND ProviderName = @prov;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalOrgId;
        cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 60).Value = providerName;

        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;

        return new EnrichmentTrackingRow(
            r.GetInt64(0),
            r.GetInt64(1),
            r.GetString(2),
            r.GetString(3),
            r.IsDBNull(4) ? null : r.GetDateTimeOffset(4),
            r.IsDBNull(5) ? null : r.GetDateTimeOffset(5),
            r.IsDBNull(6) ? null : r.GetDateTimeOffset(6),
            r.GetInt32(7),
            r.IsDBNull(8) ? null : r.GetString(8),
            r.IsDBNull(9) ? null : r.GetString(9));
    }

    public async Task RecordAttemptAsync(
        long canonicalOrgId,
        string providerName,
        EnrichmentResult result,
        DateTimeOffset nextRefreshAtUtc,
        CancellationToken ct)
    {
        const string sql = @"
MERGE opportunities.CanonicalOrgEnrichment AS t
USING (SELECT @id AS CanonicalOrgId, @prov AS ProviderName) AS s
   ON t.CanonicalOrgId = s.CanonicalOrgId AND t.ProviderName = s.ProviderName
WHEN MATCHED THEN UPDATE SET
    Status            = @status,
    LastAttemptAtUtc  = sysdatetimeoffset(),
    LastRefreshAtUtc  = CASE WHEN @status = 'ok' THEN sysdatetimeoffset() ELSE t.LastRefreshAtUtc END,
    NextRefreshAtUtc  = @next,
    Attempts          = t.Attempts + 1,
    ErrorMessage      = @err,
    ResultJson        = COALESCE(@json, t.ResultJson),
    Notes             = COALESCE(@notes, t.Notes),
    UpdatedAtUtc      = sysdatetimeoffset()
WHEN NOT MATCHED THEN INSERT
    (CanonicalOrgId, ProviderName, Status, LastAttemptAtUtc,
     LastRefreshAtUtc, NextRefreshAtUtc, Attempts, ErrorMessage, ResultJson, Notes)
    VALUES
    (@id, @prov, @status, sysdatetimeoffset(),
     CASE WHEN @status = 'ok' THEN sysdatetimeoffset() ELSE NULL END,
     @next, 1, @err, @json, @notes);";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalOrgId;
        cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 60).Value = providerName;
        cmd.Parameters.Add("@status", SqlDbType.NVarChar, 20).Value = result.Status;
        cmd.Parameters.Add("@next", SqlDbType.DateTimeOffset).Value = nextRefreshAtUtc;
        cmd.Parameters.Add("@err", SqlDbType.NVarChar, 2000).Value = (object?)result.ErrorMessage ?? DBNull.Value;
        cmd.Parameters.Add("@json", SqlDbType.NVarChar, -1).Value = (object?)result.ResultJson ?? DBNull.Value;
        cmd.Parameters.Add("@notes", SqlDbType.NVarChar, -1).Value = (object?)result.Notes ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> CountByStatusAsync(string providerName, string status, CancellationToken ct)
    {
        const string sql = @"
SELECT COUNT(*) FROM opportunities.CanonicalOrgEnrichment
WHERE ProviderName = @prov AND Status = @status;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 60).Value = providerName;
        cmd.Parameters.Add("@status", SqlDbType.NVarChar, 20).Value = status;
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return v is null || v is DBNull ? 0 : Convert.ToInt32(v);
    }
}
