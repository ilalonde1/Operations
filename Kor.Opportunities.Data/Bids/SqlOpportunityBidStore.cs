#nullable enable
using System;
using System.Data;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Bids;

public sealed class SqlOpportunityBidStore : IOpportunityBidStore
{
    private const int CommandTimeoutSeconds = 15;
    private readonly string _connectionString;

    public SqlOpportunityBidStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<long> UpsertAsync(OpportunityBid bid, CancellationToken ct)
    {
        const string sql = @"
MERGE opportunities.OpportunityBids AS target
USING (SELECT @sourceId AS OpportunitySourceId,
              @extRef AS ExternalReference,
              @bidderName AS BidderName) AS src
   ON target.OpportunitySourceId = src.OpportunitySourceId
  AND target.ExternalReference = src.ExternalReference
  AND target.BidderName = src.BidderName
WHEN MATCHED THEN UPDATE SET
    BidAmount = @amount,
    BidCurrency = @currency,
    BidderRank = @rank,
    BidderAddress = @address,
    SourceUrl = @url,
    RawJson = @raw,
    UpdatedAtUtc = sysdatetimeoffset(),
    IngestionRunId = COALESCE(@runId, target.IngestionRunId)
WHEN NOT MATCHED THEN INSERT
    (OpportunitySourceId, ExternalReference, BidderName, BidAmount,
     BidCurrency, BidderRank, BidderAddress, SourceUrl, RawJson,
     IngestionRunId)
VALUES
    (@sourceId, @extRef, @bidderName, @amount,
     @currency, @rank, @address, @url, @raw,
     @runId)
OUTPUT CASE WHEN $action = 'INSERT' THEN INSERTED.Id ELSE CONVERT(BIGINT, 0) END;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@sourceId", SqlDbType.UniqueIdentifier).Value = bid.OpportunitySourceId;
        cmd.Parameters.Add("@extRef", SqlDbType.NVarChar, 200).Value = bid.ExternalReference;
        cmd.Parameters.Add("@bidderName", SqlDbType.NVarChar, 300).Value = bid.BidderName;
        cmd.Parameters.Add("@amount", SqlDbType.Decimal).Value = (object?)bid.BidAmount ?? DBNull.Value;
        ((SqlParameter)cmd.Parameters["@amount"]).Precision = 18;
        ((SqlParameter)cmd.Parameters["@amount"]).Scale = 2;
        cmd.Parameters.Add("@currency", SqlDbType.Char, 3).Value = bid.BidCurrency;
        cmd.Parameters.Add("@rank", SqlDbType.Int).Value = (object?)bid.BidderRank ?? DBNull.Value;
        cmd.Parameters.Add("@address", SqlDbType.NVarChar, 500).Value = (object?)bid.BidderAddress ?? DBNull.Value;
        cmd.Parameters.Add("@url", SqlDbType.NVarChar, 800).Value = (object?)bid.SourceUrl ?? DBNull.Value;
        cmd.Parameters.Add("@raw", SqlDbType.NVarChar, -1).Value = (object?)bid.RawJson ?? DBNull.Value;
        cmd.Parameters.Add("@runId", SqlDbType.UniqueIdentifier).Value = (object?)bid.IngestionRunId ?? DBNull.Value;

        var id = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return id is null ? 0 : (long)id;
    }

    public async Task<int> ListBidderCountForAsync(Guid sourceId, string externalReference, CancellationToken ct)
    {
        const string sql = @"
SELECT COUNT_BIG(1)
FROM opportunities.OpportunityBids
WHERE OpportunitySourceId = @sourceId
  AND ExternalReference = @extRef;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@sourceId", SqlDbType.UniqueIdentifier).Value = sourceId;
        cmd.Parameters.Add("@extRef", SqlDbType.NVarChar, 200).Value = externalReference;

        var count = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return count is null ? 0 : Convert.ToInt32(count, CultureInfo.InvariantCulture);
    }
}
