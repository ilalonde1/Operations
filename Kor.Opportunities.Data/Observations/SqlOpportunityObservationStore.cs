#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Observations;

public sealed class SqlOpportunityObservationStore : IOpportunityObservationStore
{
    private const int CommandTimeoutSeconds = 30;

    // SQL Server unique-violation error numbers.
    private const int ErrorUniqueViolation = 2627;
    private const int ErrorUniqueIndexViolation = 2601;

    private const string AllColumns = @"
Id, OpportunityId, OpportunitySourceId, Title, Buyer, Location, Url, Description, RawJson,
PostedDateUtc, IngestedAtUtc, HashSha256, IsActive";

    private readonly string _connectionString;

    public SqlOpportunityObservationStore(string connectionString)
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
INSERT INTO opportunities.OpportunityObservations
    (OpportunityId, OpportunitySourceId, Title, Buyer, Location, Url, Description, RawJson,
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
            // Hash already in the table — caller treats as duplicate, not failure.
            return null;
        }
    }

    public async Task LinkAsync(long observationId, long opportunityId, CancellationToken ct)
    {
        const string sql = @"
UPDATE opportunities.OpportunityObservations
SET OpportunityId = @oppId
WHERE Id = @id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@oppId", SqlDbType.BigInt).Value = opportunityId;
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = observationId;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OpportunityObservation>> ListByOpportunityAsync(long opportunityId, CancellationToken ct)
    {
        var sql = $@"
SELECT {AllColumns}
FROM opportunities.OpportunityObservations
WHERE OpportunityId = @oppId
ORDER BY IngestedAtUtc DESC;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@oppId", SqlDbType.BigInt).Value = opportunityId;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var rows = new List<OpportunityObservation>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(MapReader(reader));
        }

        return rows;
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
