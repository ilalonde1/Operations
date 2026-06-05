#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Intel;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Awards;

public sealed class SqlEnrichmentTrackingStore : IEnrichmentTrackingStore
{
    private const int CommandTimeoutSeconds = 30;
    private static long _missingExtractorCount;
    private static long _extractedCount;
    private readonly string _connectionString;
    private readonly IntelExtractorRegistry? _intelRegistry;
    private readonly IntelPersistenceService? _intelPersistence;

    public SqlEnrichmentTrackingStore(
        string connectionString,
        IntelExtractorRegistry? intelRegistry = null,
        IntelPersistenceService? intelPersistence = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
        _intelRegistry = intelRegistry;
        _intelPersistence = intelPersistence;
    }

    public static (long Extracted, long MissingExtractor) GetIntelCounters() =>
        (
            System.Threading.Interlocked.Read(ref _extractedCount),
            System.Threading.Interlocked.Read(ref _missingExtractorCount));

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

    public async Task<IReadOnlyList<EnrichmentTrackingRow>> ListByOrgAsync(long canonicalOrgId, CancellationToken ct)
    {
        const string sql = @"
SELECT Id, CanonicalOrgId, ProviderName, Status,
       LastRefreshAtUtc, LastAttemptAtUtc, NextRefreshAtUtc,
       Attempts, ErrorMessage, ResultJson
FROM   opportunities.CanonicalOrgEnrichment
WHERE  CanonicalOrgId = @id
  AND  Status = 'ok'
ORDER  BY ProviderName;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalOrgId;

        var rows = new List<EnrichmentTrackingRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new EnrichmentTrackingRow(
                r.GetInt64(0),
                r.GetInt64(1),
                r.GetString(2),
                r.GetString(3),
                r.IsDBNull(4) ? null : r.GetDateTimeOffset(4),
                r.IsDBNull(5) ? null : r.GetDateTimeOffset(5),
                r.IsDBNull(6) ? null : r.GetDateTimeOffset(6),
                r.GetInt32(7),
                r.IsDBNull(8) ? null : r.GetString(8),
                r.IsDBNull(9) ? null : r.GetString(9)));
        }

        return rows;
    }

    public async Task RecordAttemptAsync(
        long canonicalOrgId,
        string providerName,
        EnrichmentResult result,
        DateTimeOffset nextRefreshAtUtc,
        CancellationToken ct)
    {
        var refreshStartTime = DateTimeOffset.UtcNow;
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

        await TryExtractIntelAsync(con, canonicalOrgId, providerName, result, refreshStartTime, ct).ConfigureAwait(false);
    }

    private async Task TryExtractIntelAsync(
        SqlConnection con,
        long canonicalOrgId,
        string providerName,
        EnrichmentResult result,
        DateTimeOffset refreshStartTime,
        CancellationToken ct)
    {
        if (result.Status != EnrichmentStatuses.Ok
            || string.IsNullOrWhiteSpace(result.ResultJson)
            || _intelRegistry is null
            || _intelPersistence is null)
        {
            return;
        }

        try
        {
            const string idSql = @"SELECT Id FROM opportunities.CanonicalOrgEnrichment WHERE CanonicalOrgId = @id AND ProviderName = @prov;";
            await using var idCmd = new SqlCommand(idSql, con) { CommandTimeout = CommandTimeoutSeconds };
            idCmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalOrgId;
            idCmd.Parameters.Add("@prov", SqlDbType.NVarChar, 60).Value = providerName;
            var idResult = await idCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (idResult is null or DBNull)
            {
                System.Diagnostics.Trace.TraceWarning(
                    "Intel extraction skipped: enrichment id not found for {0}/{1}.",
                    providerName,
                    canonicalOrgId);
                return;
            }

            var enrichmentId = Convert.ToInt64(idResult);
            var extractor = _intelRegistry.Resolve(providerName);
            if (extractor.ProviderName == "*" || extractor is DefaultIntelExtractor)
            {
                System.Threading.Interlocked.Increment(ref _missingExtractorCount);
                System.Diagnostics.Trace.TraceInformation(
                    "Intel extractor missing for provider {0}; row archived only.",
                    providerName);
                return;
            }

            using var doc = JsonDocument.Parse(result.ResultJson);
            var ctxIntel = new IntelExtractionContext(
                CanonicalOrgId: canonicalOrgId,
                SourceEnrichmentId: enrichmentId,
                ProviderName: providerName,
                ResultJson: doc,
                RefreshedAtUtc: DateTimeOffset.UtcNow);

            var drafts = extractor.Extract(ctxIntel);
            System.Threading.Interlocked.Increment(ref _extractedCount);
            if (drafts.People.Count + drafts.Affiliations.Count + drafts.Signals.Count
                + drafts.Actions.Count + drafts.Works.Count + drafts.Risks.Count
                + drafts.Narratives.Count == 0)
            {
                return;
            }

            await _intelPersistence.PersistAsync(drafts, ctxIntel, ct).ConfigureAwait(false);
            await RetireSupersededIntelAsync(con, canonicalOrgId, providerName, refreshStartTime, ct).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                "Intel extraction skipped: ResultJson parse failed for {0}/{1}: {2}",
                providerName,
                canonicalOrgId,
                ex.Message);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                "Intel extraction failed for {0}/{1}: {2}",
                providerName,
                canonicalOrgId,
                ex.Message);
        }
    }

    private static async Task<int> RetireSupersededIntelAsync(
        SqlConnection con,
        long canonicalOrgId,
        string sourceProviderName,
        DateTimeOffset refreshStartTime,
        CancellationToken ct)
    {
        const string sql = @"
DECLARE @retired int = 0;
DECLARE @reason nvarchar(200) = N'Superseded by refresh at ' +
    CONVERT(nvarchar(30), @cutoff, 127);

UPDATE opportunities.IntelPersonAffiliation
SET RetiredAtUtc = sysdatetimeoffset(), RetiredReason = @reason
WHERE CanonicalOrgId = @org
  AND SourceProviderName = @prov
  AND RetiredAtUtc IS NULL
  AND LastSeenAtUtc < @cutoff;
SET @retired += @@ROWCOUNT;

UPDATE opportunities.IntelSignal
SET RetiredAtUtc = sysdatetimeoffset(), RetiredReason = @reason
WHERE CanonicalOrgId = @org
  AND SourceProviderName = @prov
  AND RetiredAtUtc IS NULL
  AND LastSeenAtUtc < @cutoff;
SET @retired += @@ROWCOUNT;

UPDATE opportunities.IntelAction
SET RetiredAtUtc = sysdatetimeoffset(), RetiredReason = @reason
WHERE CanonicalOrgId = @org
  AND SourceProviderName = @prov
  AND RetiredAtUtc IS NULL
  AND Status = 'Open'
  AND LastSeenAtUtc < @cutoff;
SET @retired += @@ROWCOUNT;

UPDATE opportunities.IntelWork
SET RetiredAtUtc = sysdatetimeoffset(), RetiredReason = @reason
WHERE CanonicalOrgId = @org
  AND SourceProviderName = @prov
  AND RetiredAtUtc IS NULL
  AND LastSeenAtUtc < @cutoff;
SET @retired += @@ROWCOUNT;

UPDATE opportunities.IntelRisk
SET RetiredAtUtc = sysdatetimeoffset(), RetiredReason = @reason
WHERE CanonicalOrgId = @org
  AND SourceProviderName = @prov
  AND RetiredAtUtc IS NULL
  AND LastSeenAtUtc < @cutoff;
SET @retired += @@ROWCOUNT;

-- IntelNarrative is intentionally omitted because it already upserts cleanly
-- on (CanonicalOrgId, SourceProviderName, NarrativeType).

SELECT @retired;";

        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@org", SqlDbType.BigInt).Value = canonicalOrgId;
        cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 60).Value = sourceProviderName;
        cmd.Parameters.Add("@cutoff", SqlDbType.DateTimeOffset).Value = refreshStartTime;
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        var count = Convert.ToInt32(result, CultureInfo.InvariantCulture);
        if (count > 0)
        {
            System.Diagnostics.Trace.TraceInformation(
                "Intel retirement: org {0} provider {1} cutoff {2:O} retired {3} rows.",
                canonicalOrgId,
                sourceProviderName,
                refreshStartTime,
                count);
        }

        return count;
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
