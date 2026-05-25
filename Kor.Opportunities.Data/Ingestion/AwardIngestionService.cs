#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;
using Kor.Opportunities.Data.Sources;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Ingestion;

/// <summary>Orchestrates an awards ingestion run end-to-end: resolve the
/// IAwardProvider for the source type, run it, persist each candidate via
/// IOpportunityAwardStore.UpsertAsync on (OpportunitySourceId, ExternalReference). Logs into the existing
/// IngestionRuns table (provider name prefixed with "Awards: " for clarity).</summary>
public sealed class AwardIngestionService
{
    private readonly IOpportunitySourceStore _sourceStore;
    private readonly IOpportunityAwardStore _awardStore;
    private readonly IIngestionRunStore _runStore;
    private readonly IReadOnlyDictionary<OpportunitySourceType, IAwardProvider> _providersByType;
    private readonly ILogger<AwardIngestionService> _logger;

    public AwardIngestionService(
        IOpportunitySourceStore sourceStore,
        IOpportunityAwardStore awardStore,
        IIngestionRunStore runStore,
        IEnumerable<IAwardProvider> providers,
        ILogger<AwardIngestionService> logger)
    {
        _sourceStore = sourceStore;
        _awardStore = awardStore;
        _runStore = runStore;
        _logger = logger;

        var dict = new Dictionary<OpportunitySourceType, IAwardProvider>();
        foreach (var provider in providers)
        {
            dict[provider.SourceType] = provider;
        }
        _providersByType = dict;
    }

    public bool CanHandle(OpportunitySourceType type) =>
        type != OpportunitySourceType.Unknown && _providersByType.ContainsKey(type);

    public async Task<(bool Success, int Inserted, int Updated, string? Error, Guid? RunId)> IngestAsync(
        Guid opportunitySourceId,
        string? correlationId,
        CancellationToken ct)
    {
        var sources = await _sourceStore.ListEnabledAsync(ct).ConfigureAwait(false);
        var source = sources.FirstOrDefault(s => s.Id == opportunitySourceId);
        if (source is null)
        {
            return (false, 0, 0, $"Source {opportunitySourceId} not found or disabled.", null);
        }

        if (!_providersByType.TryGetValue(source.SourceType, out var provider))
        {
            return (false, 0, 0, $"No IAwardProvider registered for {source.SourceType}.", null);
        }

        var providerName = $"Awards: {source.Name} ({source.SourceType})";
        var hostInstance = $"{Environment.MachineName}/{Environment.ProcessId}";
        var runId = await _runStore.StartAsync(providerName, hostInstance, correlationId, ct).ConfigureAwait(false);

        var inserted = 0;
        var updated = 0;
        var sw = Stopwatch.StartNew();
        try
        {
            var mappings = await _sourceStore.GetMappingsAsync(source.Id, ct).ConfigureAwait(false);
            var candidates = await provider.FetchAsync(source, mappings, ct).ConfigureAwait(false);
            foreach (var candidate in candidates)
            {
                var award = new OpportunityAward
                {
                    ExternalReference = candidate.ExternalReference,
                    OpportunitySourceId = source.Id,
                    Title = candidate.Title,
                    SolicitationType = candidate.SolicitationType,
                    AwardingOrganization = candidate.AwardingOrganization,
                    AwardedToOrganization = candidate.AwardedToOrganization,
                    ContractValue = candidate.ContractValue,
                    ContractCurrency = candidate.ContractCurrency,
                    AwardedAtUtc = candidate.AwardedAtUtc,
                    IssuingLocation = candidate.IssuingLocation,
                    SupplierAddress = candidate.SupplierAddress,
                    ContactEmail = candidate.ContactEmail,
                    ContractNumber = candidate.ContractNumber,
                    SourceUrl = candidate.SourceUrl,
                    RawJson = candidate.RawJson,
                    IngestionRunId = runId,
                };
                var rowId = await _awardStore.UpsertAsync(award, ct).ConfigureAwait(false);
                if (rowId > 0)
                {
                    inserted++;
                }
                else
                {
                    updated++;
                }
            }

            sw.Stop();
            await _runStore.CompleteAsync(runId, success: true,
                insertedCount: inserted, duplicateCount: updated,
                skippedCount: 0, failedCount: 0, errorSummary: null, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Awards ingestion {Source}: {Inserted} new / {Updated} updated in {Elapsed}ms.",
                source.Name, inserted, updated, sw.ElapsedMilliseconds);
            return (true, inserted, updated, null, runId);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            const string error = "cancelled";
            await _runStore.CompleteAsync(runId, success: false,
                insertedCount: inserted, duplicateCount: updated,
                skippedCount: 0, failedCount: 0, errorSummary: error, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            var error = Truncate(ex.Message, 2000);
            await _runStore.CompleteAsync(runId, success: false,
                insertedCount: inserted, duplicateCount: updated,
                skippedCount: 0, failedCount: 0, errorSummary: error, ct).ConfigureAwait(false);
            _logger.LogError(ex,
                "Awards ingestion {Source} failed after {Elapsed}ms: {Message}",
                source.Name, sw.ElapsedMilliseconds, ex.Message);
            return (false, inserted, updated, error, runId);
        }
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value.Substring(0, max);
}
