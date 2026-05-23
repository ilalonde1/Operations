#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Awards;

public sealed class SqlVendorSiteCrawlStore : IVendorSiteCrawlStore
{
    private const int CommandTimeoutSeconds = 30;
    private readonly string _connectionString;

    public SqlVendorSiteCrawlStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
    }

    public async Task<int> CountCrawledAsync(CancellationToken ct)
    {
        const string sql = "SELECT COUNT(*) FROM opportunities.VendorSiteCrawl WHERE Status = 'ok';";
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(v ?? 0);
    }

    public async Task<IReadOnlyList<string>> ListPendingWebsitesAsync(
        int batchSize,
        int maxAttempts,
        CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (@batch) w
FROM (
    SELECT DISTINCT a.AgentVendorWebsite AS w
    FROM   opportunities.OpportunityAwards a
    LEFT   JOIN opportunities.VendorSiteCrawl c ON c.VendorWebsite = a.AgentVendorWebsite
    WHERE  a.AgentVendorWebsite IS NOT NULL
      AND  LEN(a.AgentVendorWebsite) > 5
      AND  (c.Id IS NULL OR (c.Status NOT IN ('ok','blocked','no_robots') AND c.Attempts < @maxAttempts))
) x
ORDER BY w;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@batch", SqlDbType.Int).Value = batchSize;
        cmd.Parameters.Add("@maxAttempts", SqlDbType.Int).Value = maxAttempts;

        var list = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            if (!r.IsDBNull(0)) list.Add(r.GetString(0));
        }

        return list;
    }

    public async Task RecordCaptureAsync(string website, RawSiteCapture capture, CancellationToken ct)
    {
        const string sql = @"
MERGE opportunities.VendorSiteCrawl AS t
USING (SELECT @website AS VendorWebsite) AS s ON t.VendorWebsite = s.VendorWebsite
WHEN MATCHED THEN UPDATE SET
    Status = 'ok',
    CrawledAtUtc = sysdatetimeoffset(),
    LastAttemptAtUtc = sysdatetimeoffset(),
    Attempts = t.Attempts + 1,
    ErrorMessage = NULL,
    RawCapture = @raw
WHEN NOT MATCHED THEN INSERT
    (VendorWebsite, Status, CrawledAtUtc, LastAttemptAtUtc, Attempts, RawCapture)
    VALUES (@website, 'ok', sysdatetimeoffset(), sysdatetimeoffset(), 1, @raw);";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@website", SqlDbType.NVarChar, 500).Value = website;
        cmd.Parameters.Add("@raw", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(capture);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RecordFailureAsync(string website, string status, string errorMessage, CancellationToken ct)
    {
        const string sql = @"
MERGE opportunities.VendorSiteCrawl AS t
USING (SELECT @website AS VendorWebsite) AS s ON t.VendorWebsite = s.VendorWebsite
WHEN MATCHED THEN UPDATE SET
    Status = @status,
    LastAttemptAtUtc = sysdatetimeoffset(),
    Attempts = t.Attempts + 1,
    ErrorMessage = @err
WHEN NOT MATCHED THEN INSERT
    (VendorWebsite, Status, LastAttemptAtUtc, Attempts, ErrorMessage)
    VALUES (@website, @status, sysdatetimeoffset(), 1, @err);";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@website", SqlDbType.NVarChar, 500).Value = website;
        cmd.Parameters.Add("@status", SqlDbType.NVarChar, 20).Value = status;
        cmd.Parameters.Add("@err", SqlDbType.NVarChar, 2000).Value = (object?)errorMessage ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
