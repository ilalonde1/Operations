#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Sources;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Ingestion;

public sealed class IngestionDispatcher : IIngestionDispatcher
{
    private readonly IOpportunitySourceStore _sourceStore;
    private readonly IIngestionService _ingestionService;
    private readonly IReadOnlyDictionary<OpportunitySourceType, IOpportunityProvider> _providersByType;
    private readonly ILogger<IngestionDispatcher> _logger;

    public IngestionDispatcher(
        IOpportunitySourceStore sourceStore,
        IIngestionService ingestionService,
        IEnumerable<IOpportunityProvider> providers,
        ILogger<IngestionDispatcher> logger)
    {
        _sourceStore = sourceStore;
        _ingestionService = ingestionService;
        _logger = logger;

        // Last-registration-wins is fine — the host wires one provider per source type.
        // If a duplicate slips in we log it and keep the most recently registered one,
        // which matches typical DI conventions and avoids a startup crash.
        var dict = new Dictionary<OpportunitySourceType, IOpportunityProvider>();
        foreach (var p in providers)
        {
            if (dict.ContainsKey(p.SourceType))
            {
                _logger.LogWarning(
                    "Multiple providers registered for {Type}; using {NewProvider} and replacing {OldProvider}.",
                    p.SourceType, p.GetType().Name, dict[p.SourceType].GetType().Name);
            }

            dict[p.SourceType] = p;
        }

        _providersByType = dict;
    }

    public async Task<DispatchResult> RunByNameAsync(string sourceName, string? correlationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            throw new ArgumentException("sourceName is required.", nameof(sourceName));
        }

        var source = await _sourceStore.GetByNameAsync(sourceName, ct).ConfigureAwait(false);
        if (source is null)
        {
            throw new InvalidOperationException($"No OpportunitySource named '{sourceName}'.");
        }

        return await DispatchAsync(source, correlationId, ct).ConfigureAwait(false);
    }

    public async Task<DispatchResult> RunByIdAsync(Guid opportunitySourceId, string? correlationId, CancellationToken ct)
    {
        // No GetById on the store yet; list-enabled is cheap (handful of rows).
        var sources = await _sourceStore.ListEnabledAsync(ct).ConfigureAwait(false);
        var source = sources.FirstOrDefault(s => s.Id == opportunitySourceId);
        if (source is null)
        {
            throw new InvalidOperationException($"OpportunitySource {opportunitySourceId} not found or disabled.");
        }

        return await DispatchAsync(source, correlationId, ct).ConfigureAwait(false);
    }

    private async Task<DispatchResult> DispatchAsync(OpportunitySource source, string? correlationId, CancellationToken ct)
    {
        if (!source.IsEnabled)
        {
            _logger.LogInformation("Skipping disabled source {Source}.", source.Name);
            return new DispatchResult(source, new IngestionResult { Success = true });
        }

        if (!_providersByType.TryGetValue(source.SourceType, out var provider))
        {
            throw new InvalidOperationException(
                $"No provider registered for source type {source.SourceType} (source '{source.Name}'). " +
                "Register an IOpportunityProvider implementation in DI.");
        }

        _logger.LogInformation(
            "Dispatching ingestion for {Source} ({Type}) via {Provider}.",
            source.Name, source.SourceType, provider.GetType().Name);

        var result = await _ingestionService.IngestAsync(provider, source, correlationId, ct).ConfigureAwait(false);
        return new DispatchResult(source, result);
    }
}
