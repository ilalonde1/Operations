#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.HistoricalOpportunities;

public sealed class SqlHistoricalOpportunityDocumentStore : IHistoricalOpportunityDocumentStore
{
    private const int CommandTimeoutSeconds = 30;

    private readonly string _connectionString;

    public SqlHistoricalOpportunityDocumentStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<int> UpsertManyAsync(
        long historicalOpportunityId,
        IReadOnlyList<DiscoveredDocument> documents,
        CancellationToken ct)
    {
        if (documents.Count == 0) return 0;

        const string sql = @"
IF NOT EXISTS (SELECT 1 FROM opportunities.HistoricalOpportunityDocuments
               WHERE HistoricalOpportunityId = @oppId AND SourceUrl = @url)
BEGIN
    INSERT INTO opportunities.HistoricalOpportunityDocuments
        (HistoricalOpportunityId, FileName, SourceUrl)
    VALUES (@oppId, @file, @url);
END;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);

        var inserted = 0;
        foreach (var doc in documents)
        {
            await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@oppId", SqlDbType.BigInt).Value = historicalOpportunityId;
            cmd.Parameters.Add("@file", SqlDbType.NVarChar, 500).Value = doc.FileName;
            cmd.Parameters.Add("@url", SqlDbType.NVarChar, 2000).Value = doc.SourceUrl;
            inserted += await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return inserted;
    }

    public async Task<IReadOnlyList<PendingDocumentRow>> ListPendingAsync(
        int batchSize,
        int maxAttempts,
        CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (@n) Id, HistoricalOpportunityId, FileName, SourceUrl, DownloadAttemptCount
FROM   opportunities.HistoricalOpportunityDocuments
WHERE  LocalPath IS NULL AND DownloadAttemptCount < @max
ORDER  BY DiscoveredAtUtc ASC, Id ASC;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@n", SqlDbType.Int).Value = batchSize;
        cmd.Parameters.Add("@max", SqlDbType.Int).Value = maxAttempts;

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var rows = new List<PendingDocumentRow>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new PendingDocumentRow(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4)));
        }

        return rows;
    }

    public async Task RecordSuccessAsync(
        long id,
        string localPath,
        byte[] sha256,
        long sizeBytes,
        string? contentType,
        CancellationToken ct)
    {
        const string sql = @"
UPDATE opportunities.HistoricalOpportunityDocuments
SET    LocalPath           = @path,
       Sha256              = @sha,
       SizeBytes           = @size,
       ContentType         = @ctype,
       DownloadedAtUtc     = sysdatetimeoffset(),
       LastAttemptAtUtc    = sysdatetimeoffset(),
       LastAttemptError    = NULL,
       DownloadAttemptCount= DownloadAttemptCount + 1
WHERE Id = @id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        cmd.Parameters.Add("@path", SqlDbType.NVarChar, 2000).Value = localPath;
        cmd.Parameters.Add("@sha", SqlDbType.VarBinary, 32).Value = sha256;
        cmd.Parameters.Add("@size", SqlDbType.BigInt).Value = sizeBytes;
        cmd.Parameters.Add("@ctype", SqlDbType.NVarChar, 100).Value = (object?)contentType ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RecordFailureAsync(long id, string error, CancellationToken ct)
    {
        const string sql = @"
UPDATE opportunities.HistoricalOpportunityDocuments
SET    DownloadAttemptCount = DownloadAttemptCount + 1,
       LastAttemptAtUtc     = sysdatetimeoffset(),
       LastAttemptError     = @err
WHERE Id = @id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        cmd.Parameters.Add("@err", SqlDbType.NVarChar, 1000).Value =
            error.Length > 1000 ? error.Substring(0, 1000) : error;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
