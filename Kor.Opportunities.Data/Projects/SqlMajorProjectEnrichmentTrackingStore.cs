#nullable enable
using System.Data;
using System.Text.Json;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Intel;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Projects;

public sealed class SqlMajorProjectEnrichmentTrackingStore : IMajorProjectEnrichmentTrackingStore
{
    private const int CommandTimeoutSeconds = 30;

    private readonly string _connectionString;
    private readonly ProjectIntelExtractorRegistry _registry;
    private readonly ProjectIntelPersistenceService _persistence;
    private readonly ILogger<SqlMajorProjectEnrichmentTrackingStore> _logger;

    public SqlMajorProjectEnrichmentTrackingStore(
        string connectionString,
        ProjectIntelExtractorRegistry registry,
        ProjectIntelPersistenceService persistence,
        ILogger<SqlMajorProjectEnrichmentTrackingStore> logger)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RecordAttemptAsync(
        long majorProjectsInventoryId,
        string providerName,
        EnrichmentResult result,
        DateTimeOffset? nextRefreshAtUtc,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new ArgumentException("Provider name is required.", nameof(providerName));
        }

        // UPDATE-then-INSERT upsert under UPDLOCK/HOLDLOCK in one transaction
        // (same pattern as SqlOpportunityAwardStore.UpsertAsync) so two
        // concurrent attempts for the same (MPI, Provider) can't both take the
        // INSERT branch and violate UQ_MajorProjectEnrichment_ProjectProvider.
        const string sql = @"
SET XACT_ABORT ON;

DECLARE @ids table (Id bigint NOT NULL);

BEGIN TRAN;

UPDATE opportunities.MajorProjectEnrichment WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
   SET Status = @status,
       LastAttemptAtUtc = sysdatetimeoffset(),
       LastRefreshAtUtc = CASE WHEN @status = 'ok' THEN sysdatetimeoffset() ELSE LastRefreshAtUtc END,
       NextRefreshAtUtc = @next,
       Attempts = Attempts + 1,
       ErrorMessage = @err,
       ResultJson = COALESCE(@json, ResultJson),
       Notes = COALESCE(@notes, Notes),
       UpdatedAtUtc = sysdatetimeoffset()
OUTPUT inserted.Id INTO @ids
 WHERE MajorProjectsInventoryId = @mpi
   AND ProviderName = @provider;

IF NOT EXISTS (SELECT 1 FROM @ids)
BEGIN
    INSERT opportunities.MajorProjectEnrichment
        (MajorProjectsInventoryId, ProviderName, Status, LastAttemptAtUtc,
         LastRefreshAtUtc, NextRefreshAtUtc, Attempts, ErrorMessage, ResultJson, Notes)
    OUTPUT inserted.Id INTO @ids
    VALUES
        (@mpi, @provider, @status, sysdatetimeoffset(),
         CASE WHEN @status = 'ok' THEN sysdatetimeoffset() ELSE NULL END,
         @next, 1, @err, @json, @notes);
END

COMMIT TRAN;

SELECT TOP 1 Id FROM @ids;";

        long enrichmentId;
        await using (var con = new SqlConnection(_connectionString))
        {
            await con.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@mpi", SqlDbType.BigInt).Value = majorProjectsInventoryId;
            cmd.Parameters.Add("@provider", SqlDbType.NVarChar, 60).Value = providerName;
            cmd.Parameters.Add("@status", SqlDbType.NVarChar, 20).Value = result.Status;
            cmd.Parameters.Add("@next", SqlDbType.DateTimeOffset).Value = (object?)nextRefreshAtUtc ?? DBNull.Value;
            cmd.Parameters.Add("@err", SqlDbType.NVarChar, 2000).Value = (object?)result.ErrorMessage ?? DBNull.Value;
            cmd.Parameters.Add("@json", SqlDbType.NVarChar, -1).Value = (object?)result.ResultJson ?? DBNull.Value;
            cmd.Parameters.Add("@notes", SqlDbType.NVarChar, -1).Value = (object?)result.Notes ?? DBNull.Value;

            var idResult = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (idResult is null or DBNull)
            {
                throw new InvalidOperationException("MajorProjectEnrichment upsert did not return an id.");
            }

            enrichmentId = Convert.ToInt64(idResult);
        }

        await TryExtractProjectIntelAsync(
            majorProjectsInventoryId,
            providerName,
            enrichmentId,
            result,
            ct).ConfigureAwait(false);
    }

    private async Task TryExtractProjectIntelAsync(
        long majorProjectsInventoryId,
        string providerName,
        long sourceEnrichmentId,
        EnrichmentResult result,
        CancellationToken ct)
    {
        if (result.Status != EnrichmentStatuses.Ok || string.IsNullOrWhiteSpace(result.ResultJson))
        {
            return;
        }

        try
        {
            var extractor = _registry.Resolve(providerName);
            if (extractor is DefaultProjectIntelExtractor)
            {
                _logger.LogInformation(
                    "Project intel extractor missing for provider {ProviderName}; MPI {MpiId} archived only.",
                    providerName,
                    majorProjectsInventoryId);
                return;
            }

            using var doc = JsonDocument.Parse(result.ResultJson);
            var refreshed = DateTimeOffset.UtcNow;
            var ctx = new ProjectIntelExtractionContext(
                majorProjectsInventoryId,
                sourceEnrichmentId,
                providerName,
                doc,
                refreshed);

            var extracted = extractor.Extract(ctx);
            if (extracted.Projects.Count + extracted.Signals.Count + extracted.Actions.Count
                + extracted.Risks.Count + extracted.KeyPeople.Count == 0)
            {
                return;
            }

            await _persistence.PersistAsync(extracted, providerName, sourceEnrichmentId, refreshed, ct)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Project intel extraction skipped: ResultJson parse failed for {ProviderName}/{MpiId}.",
                providerName,
                majorProjectsInventoryId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Project intel extraction failed for {ProviderName}/{MpiId}.",
                providerName,
                majorProjectsInventoryId);
        }
    }
}
