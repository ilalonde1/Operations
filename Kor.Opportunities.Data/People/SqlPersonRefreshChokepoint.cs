#nullable enable
using System.Data;
using System.Globalization;
using System.Text.Json;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Intel;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.People;

/// <summary>
/// Person-research chokepoint. Archives the raw JSON in a per-person
/// CanonicalOrgEnrichment row keyed to the person's current employer + a
/// synthetic ProviderName like <c>"PersonBrief-{intelPersonId}"</c>; then
/// decomposes via <see cref="PersonBriefExtractor"/> and persists through
/// the existing <see cref="IntelPersistenceService"/>. After persist, retires
/// superseded rows for the synthetic provider — same R89 retirement pattern
/// the org and project chokepoints use.
///
/// Symmetry note: by tagging IntelPerson/Affiliation/Signal/Action rows with
/// SourceProviderName=<c>"PersonBrief-{personId}"</c> we keep them clearly
/// distinguishable from org-side <c>"FirmNarrative"</c> rows. Org-side
/// staleness queries that look at LastSeenAtUtc rollups should be scoped to
/// <c>SourceProviderName='FirmNarrative'</c> so that person-side activity
/// doesn't make an org appear falsely fresh.
/// </summary>
public sealed class SqlPersonRefreshChokepoint : IPersonRefreshChokepoint
{
    private const int CommandTimeoutSeconds = 30;

    private readonly string _connectionString;
    private readonly PersonBriefExtractor _extractor;
    private readonly IntelPersistenceService _persistence;
    private readonly ILogger<SqlPersonRefreshChokepoint> _logger;

    public SqlPersonRefreshChokepoint(
        string connectionString,
        PersonBriefExtractor extractor,
        IntelPersistenceService persistence,
        ILogger<SqlPersonRefreshChokepoint> logger)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RecordAttemptAsync(
        long intelPersonId,
        EnrichmentResult result,
        DateTimeOffset nextRefreshAtUtc,
        CancellationToken ct)
    {
        var refreshStartTime = DateTimeOffset.UtcNow;
        var providerName = BuildProviderName(intelPersonId);

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);

        var lookup = await LookupPersonAsync(con, intelPersonId, ct).ConfigureAwait(false);
        if (lookup is null)
        {
            _logger.LogWarning(
                "Person refresh chokepoint skipped: IntelPerson {IntelPersonId} not found or retired.",
                intelPersonId);
            return;
        }

        if (lookup.CurrentEmployerCanonicalOrgId is null)
        {
            _logger.LogInformation(
                "Person refresh chokepoint skipped: IntelPerson {IntelPersonId} ({DisplayName}) has no current affiliation; cannot archive.",
                intelPersonId,
                lookup.DisplayName);
            return;
        }

        var canonicalOrgId = lookup.CurrentEmployerCanonicalOrgId.Value;

        await UpsertEnrichmentRowAsync(con, canonicalOrgId, providerName, result, nextRefreshAtUtc, ct)
            .ConfigureAwait(false);

        if (result.Status != EnrichmentStatuses.Ok
            || string.IsNullOrWhiteSpace(result.ResultJson))
        {
            return;
        }

        var enrichmentId = await GetEnrichmentIdAsync(con, canonicalOrgId, providerName, ct)
            .ConfigureAwait(false);
        if (enrichmentId is null)
        {
            _logger.LogWarning(
                "Person refresh chokepoint: enrichment id not found after MERGE for {ProviderName}/{CanonicalOrgId}.",
                providerName,
                canonicalOrgId);
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(result.ResultJson!);
            var personCtx = new PersonBriefExtractionContext(
                IntelPersonId: intelPersonId,
                PersonDisplayName: lookup.DisplayName,
                CurrentEmployerCanonicalOrgId: canonicalOrgId,
                SourceEnrichmentId: enrichmentId.Value,
                ProviderName: providerName,
                ResultJson: doc,
                RefreshedAtUtc: DateTimeOffset.UtcNow);

            var drafts = _extractor.Extract(personCtx);
            if (drafts.People.Count + drafts.Affiliations.Count + drafts.Signals.Count
                + drafts.Actions.Count + drafts.Works.Count + drafts.Risks.Count
                + drafts.Narratives.Count == 0)
            {
                _logger.LogInformation(
                    "Person refresh chokepoint: extractor returned empty for IntelPerson {IntelPersonId}.",
                    intelPersonId);
                return;
            }

            var ctxIntel = new IntelExtractionContext(
                CanonicalOrgId: canonicalOrgId,
                SourceEnrichmentId: enrichmentId.Value,
                ProviderName: providerName,
                ResultJson: doc,
                RefreshedAtUtc: DateTimeOffset.UtcNow);

            await _persistence.PersistAsync(drafts, ctxIntel, ct).ConfigureAwait(false);
            await RetireSupersededAsync(con, canonicalOrgId, providerName, refreshStartTime, ct)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Person refresh chokepoint: persisted refresh for IntelPerson {IntelPersonId} ({DisplayName}) at canonical org {CanonicalOrgId} — persons={Persons}, affiliations={Affiliations}, signals={Signals}, actions={Actions}.",
                intelPersonId,
                lookup.DisplayName,
                canonicalOrgId,
                drafts.People.Count,
                drafts.Affiliations.Count,
                drafts.Signals.Count,
                drafts.Actions.Count);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Person refresh chokepoint: ResultJson parse failed for IntelPerson {IntelPersonId}.",
                intelPersonId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Person refresh chokepoint: extraction or persistence failed for IntelPerson {IntelPersonId}.",
                intelPersonId);
        }
    }

    private static string BuildProviderName(long intelPersonId) =>
        "PersonBrief-" + intelPersonId.ToString(CultureInfo.InvariantCulture);

    private static async Task<PersonLookup?> LookupPersonAsync(
        SqlConnection con,
        long intelPersonId,
        CancellationToken ct)
    {
        const string sql = @"
SELECT TOP 1
    p.Id,
    p.DisplayName,
    cur.CanonicalOrgId
FROM opportunities.IntelPerson p
OUTER APPLY (
    SELECT TOP 1 a.CanonicalOrgId
    FROM opportunities.IntelPersonAffiliation a
    WHERE a.IntelPersonId = p.Id
      AND a.IsCurrent = 1
      AND a.RetiredAtUtc IS NULL
    ORDER BY a.LastSeenAtUtc DESC
) cur
WHERE p.Id = @id
  AND p.RetiredAtUtc IS NULL;";

        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = intelPersonId;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return new PersonLookup(
            r.GetInt64(0),
            r.GetString(1),
            r.IsDBNull(2) ? null : r.GetInt64(2));
    }

    private static async Task UpsertEnrichmentRowAsync(
        SqlConnection con,
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

    private static async Task<long?> GetEnrichmentIdAsync(
        SqlConnection con,
        long canonicalOrgId,
        string providerName,
        CancellationToken ct)
    {
        const string sql = "SELECT Id FROM opportunities.CanonicalOrgEnrichment WHERE CanonicalOrgId = @id AND ProviderName = @prov;";
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalOrgId;
        cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 60).Value = providerName;
        var raw = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return raw is null or DBNull ? null : Convert.ToInt64(raw, CultureInfo.InvariantCulture);
    }

    private static async Task RetireSupersededAsync(
        SqlConnection con,
        long canonicalOrgId,
        string providerName,
        DateTimeOffset refreshStartTime,
        CancellationToken ct)
    {
        const string sql = @"
DECLARE @reason nvarchar(200) = N'Superseded by person refresh at ' +
    CONVERT(nvarchar(30), @cutoff, 127);

UPDATE opportunities.IntelPersonAffiliation
SET RetiredAtUtc = sysdatetimeoffset(), RetiredReason = @reason
WHERE CanonicalOrgId = @org
  AND SourceProviderName = @prov
  AND RetiredAtUtc IS NULL
  AND LastSeenAtUtc < @cutoff;

UPDATE opportunities.IntelSignal
SET RetiredAtUtc = sysdatetimeoffset(), RetiredReason = @reason
WHERE CanonicalOrgId = @org
  AND SourceProviderName = @prov
  AND RetiredAtUtc IS NULL
  AND LastSeenAtUtc < @cutoff;

UPDATE opportunities.IntelAction
SET RetiredAtUtc = sysdatetimeoffset(), RetiredReason = @reason
WHERE CanonicalOrgId = @org
  AND SourceProviderName = @prov
  AND RetiredAtUtc IS NULL
  AND Status = 'Open'
  AND LastSeenAtUtc < @cutoff;";

        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@org", SqlDbType.BigInt).Value = canonicalOrgId;
        cmd.Parameters.Add("@prov", SqlDbType.NVarChar, 60).Value = providerName;
        cmd.Parameters.Add("@cutoff", SqlDbType.DateTimeOffset).Value = refreshStartTime;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private sealed record PersonLookup(
        long IntelPersonId,
        string DisplayName,
        long? CurrentEmployerCanonicalOrgId);
}
