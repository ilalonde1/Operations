#nullable enable
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Ingestion;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Ingestion;

/// <summary>
/// Upserts into <c>opportunities.RelevanceGateRejects</c> keyed on
/// (SourceName, Title). Sources re-serve the same postings every scheduled
/// run, so repeats bump RejectCount/LastRejectedAtUtc rather than inserting.
/// Never throws: a bookkeeping failure logs a warning and the ingestion run
/// carries on.
/// </summary>
public sealed class SqlRelevanceGateRejectStore : IRelevanceGateRejectStore
{
    private const int CommandTimeoutSeconds = 30;

    private const string UpsertSql = @"
MERGE opportunities.RelevanceGateRejects WITH (HOLDLOCK) AS t
USING (VALUES (@source, @title)) AS s (SourceName, Title)
    ON t.SourceName = s.SourceName AND t.Title = s.Title
WHEN MATCHED THEN
    UPDATE SET LastRejectedAtUtc = SYSDATETIMEOFFSET(),
               RejectCount       = t.RejectCount + 1,
               RejectReason      = @reason,
               Buyer             = COALESCE(@buyer, t.Buyer),
               Url               = COALESCE(@url, t.Url),
               -- COALESCE(new, old): a later run that served a thinner row must
               -- not blank a field an earlier run captured.
               ExternalReference = COALESCE(@ref, t.ExternalReference),
               [Location]        = COALESCE(@location, t.[Location]),
               [Description]     = COALESCE(@description, t.[Description]),
               PostedDateUtc     = COALESCE(@posted, t.PostedDateUtc),
               ProjectCity       = COALESCE(@city, t.ProjectCity)
WHEN NOT MATCHED THEN
    INSERT (SourceName, Title, Buyer, Url, RejectReason, FirstRejectedAtUtc, LastRejectedAtUtc, RejectCount,
            ExternalReference, [Location], [Description], PostedDateUtc, ProjectCity)
    VALUES (@source, @title, @buyer, @url, @reason, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 1,
            @ref, @location, @description, @posted, @city);";

    private readonly string _connectionString;
    private readonly ILogger<SqlRelevanceGateRejectStore>? _logger;

    public SqlRelevanceGateRejectStore(string connectionString, ILogger<SqlRelevanceGateRejectStore>? logger = null)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger;
    }

    public async Task RecordAsync(
        string sourceName,
        OpportunityCandidate candidate,
        string rejectReason,
        CancellationToken ct)
    {
        var title = candidate.Title;
        try
        {
            await using var con = new SqlConnection(_connectionString);
            await con.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(UpsertSql, con) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@source", SqlDbType.NVarChar, 200).Value = Truncate(sourceName, 200);
            cmd.Parameters.Add("@title", SqlDbType.NVarChar, 500).Value = Truncate(title, 500);
            cmd.Parameters.Add("@buyer", SqlDbType.NVarChar, 300).Value = (object?)Truncate(candidate.Buyer, 300) ?? DBNull.Value;
            cmd.Parameters.Add("@url", SqlDbType.NVarChar, 2000).Value = (object?)Truncate(candidate.Url, 2000) ?? DBNull.Value;
            cmd.Parameters.Add("@reason", SqlDbType.NVarChar, 200).Value = Truncate(rejectReason, 200);
            cmd.Parameters.Add("@ref", SqlDbType.NVarChar, 200).Value = (object?)Truncate(candidate.ExternalReference, 200) ?? DBNull.Value;
            cmd.Parameters.Add("@location", SqlDbType.NVarChar, 500).Value = (object?)Truncate(candidate.Location, 500) ?? DBNull.Value;
            cmd.Parameters.Add("@description", SqlDbType.NVarChar, 4000).Value = (object?)Truncate(candidate.Description, 4000) ?? DBNull.Value;
            cmd.Parameters.Add("@city", SqlDbType.NVarChar, 200).Value = (object?)Truncate(candidate.ProjectCity, 200) ?? DBNull.Value;
            cmd.Parameters.Add("@posted", SqlDbType.DateTimeOffset).Value =
                (object?)candidate.PostedDateUtc ?? DBNull.Value;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Run is being torn down; nothing to salvage.
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex,
                "Failed to record relevance-gate reject for '{Title}' from {Source}.",
                title, sourceName);
        }
    }

    private static string? Truncate(string? value, int max)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
