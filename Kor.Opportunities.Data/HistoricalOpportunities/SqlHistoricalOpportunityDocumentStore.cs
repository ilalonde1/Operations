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
}
