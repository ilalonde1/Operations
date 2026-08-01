#nullable enable
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.HistoricalOpportunities;

public sealed class SqlHistoricalOpportunityObservationStore : IHistoricalOpportunityObservationStore
{
    private const int CommandTimeoutSeconds = 30;
    private const int ErrorUniqueViolation = 2627;
    private const int ErrorUniqueIndexViolation = 2601;

    private const string AllColumns = @"
Id, HistoricalOpportunityId, OpportunitySourceId, Title, Buyer, Location, Url, Description, RawJson,
PostedDateUtc, IngestedAtUtc, HashSha256, IsActive";

    private readonly string _connectionString;

    public SqlHistoricalOpportunityObservationStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<OpportunityObservation?> TryInsertAsync(OpportunityObservation o, CancellationToken ct)
    {
        if (o.HashSha256.Length != 32)
        {
            throw new ArgumentException("HashSha256 must be a 32-byte SHA-256 digest.", nameof(o));
        }

        var sql = $@"
INSERT INTO opportunities.HistoricalOpportunityObservations
    (HistoricalOpportunityId, OpportunitySourceId, Title, Buyer, Location, Url, Description, RawJson,
     PostedDateUtc, HashSha256, IsActive)
OUTPUT {OutputInsertedColumns()}
VALUES
    (@oppId, @srcId, @title, @buyer, @location, @url, @description, @rawJson,
     @postedAt, @hash, @isActive);";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@oppId", SqlDbType.BigInt).Value = (object?)o.OpportunityId ?? DBNull.Value;
        cmd.Parameters.Add("@srcId", SqlDbType.UniqueIdentifier).Value = o.OpportunitySourceId;
        cmd.Parameters.Add("@title", SqlDbType.NVarChar, 400).Value = o.Title;
        cmd.Parameters.Add("@buyer", SqlDbType.NVarChar, 300).Value = o.Buyer;
        cmd.Parameters.Add("@location", SqlDbType.NVarChar, 300).Value = (object?)o.Location ?? DBNull.Value;
        cmd.Parameters.Add("@url", SqlDbType.NVarChar, 2000).Value = o.Url;
        cmd.Parameters.Add("@description", SqlDbType.NVarChar, -1).Value = (object?)o.Description ?? DBNull.Value;
        cmd.Parameters.Add("@rawJson", SqlDbType.NVarChar, -1).Value = (object?)o.RawJson ?? DBNull.Value;
        cmd.Parameters.Add("@postedAt", SqlDbType.DateTimeOffset).Value = (object?)o.PostedDateUtc ?? DBNull.Value;
        cmd.Parameters.Add("@hash", SqlDbType.VarBinary, 32).Value = o.HashSha256;
        cmd.Parameters.Add("@isActive", SqlDbType.Bit).Value = o.IsActive;

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                throw new InvalidOperationException("INSERT did not return a row.");
            }

            return MapReader(reader);
        }
        catch (SqlException ex) when (ex.Number == ErrorUniqueViolation || ex.Number == ErrorUniqueIndexViolation)
        {
            return null;
        }
    }

    public async Task LinkAsync(long observationId, long historicalOpportunityId, CancellationToken ct)
    {
        const string sql = @"
UPDATE opportunities.HistoricalOpportunityObservations
SET HistoricalOpportunityId = @oppId
WHERE Id = @id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@oppId", SqlDbType.BigInt).Value = historicalOpportunityId;
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = observationId;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<OpportunityObservation?> TryGetByHashAsync(byte[] hashSha256, CancellationToken ct)
    {
        var sql = $@"
SELECT {AllColumns}
FROM opportunities.HistoricalOpportunityObservations
WHERE HashSha256 = @hash;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@hash", SqlDbType.VarBinary, 32).Value = hashSha256;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? MapReader(reader) : null;
    }

    private static string OutputInsertedColumns()
    {
        var columnNames = AllColumns
            .Replace("\n", " ")
            .Replace("\r", " ")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return string.Join(", ", Array.ConvertAll(columnNames, c => $"INSERTED.{c}"));
    }

    private static OpportunityObservation MapReader(SqlDataReader r) => new()
    {
        Id = r.GetInt64(0),
        OpportunityId = r.IsDBNull(1) ? null : r.GetInt64(1),
        OpportunitySourceId = r.GetGuid(2),
        Title = r.GetString(3),
        Buyer = r.GetString(4),
        Location = r.IsDBNull(5) ? null : r.GetString(5),
        Url = r.GetString(6),
        Description = r.IsDBNull(7) ? null : r.GetString(7),
        RawJson = r.IsDBNull(8) ? null : r.GetString(8),
        PostedDateUtc = r.IsDBNull(9) ? null : r.GetDateTimeOffset(9),
        IngestedAtUtc = r.GetDateTimeOffset(10),
        HashSha256 = (byte[])r.GetValue(11),
        IsActive = r.GetBoolean(12),
    };
}
