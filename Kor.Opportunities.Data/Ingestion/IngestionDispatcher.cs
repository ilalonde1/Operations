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
    private readonly IReadOnlyDictionary<OpportunitySourceType, IReadOnlyList<IOpportunityProvider>> _providersByType;
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

        var dict = new Dictionary<OpportunitySourceType, List<IOpportunityProvider>>();
        foreach (var p in providers)
        {
            if (!dict.TryGetValue(p.SourceType, out var typedProviders))
            {
                typedProviders = [];
                dict[p.SourceType] = typedProviders;
            }

            if (typedProviders.Count > 0)
            {
                _logger.LogWarning(
                    "Multiple providers registered for {Type}; source-name prefix matching will choose among {ExistingProvider} and {NewProvider}.",
                    p.SourceType, typedProviders[^1].GetType().Name, p.GetType().Name);
            }

            typedProviders.Add(p);
        }

        _providersByType = dict.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<IOpportunityProvider>)kvp.Value);
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

        if (!_providersByType.TryGetValue(source.SourceType, out var providers) || providers.Count == 0)
        {
            throw new InvalidOperationException(
                $"No provider registered for source type {source.SourceType} (source '{source.Name}'). " +
                "Register an IOpportunityProvider implementation in DI.");
        }

        var provider = SelectProvider(source, providers);

        _logger.LogInformation(
            "Dispatching ingestion for {Source} ({Type}) via {Provider}.",
            source.Name, source.SourceType, provider.GetType().Name);

        var result = await _ingestionService.IngestAsync(provider, source, correlationId, ct).ConfigureAwait(false);
        return new DispatchResult(source, result);
    }

    private static IOpportunityProvider SelectProvider(OpportunitySource source, IReadOnlyList<IOpportunityProvider> providers)
    {
        if (providers.Count == 1)
        {
            return providers[0];
        }

        if (source.Name.StartsWith("CA_CEQAnet", StringComparison.OrdinalIgnoreCase))
        {
            var ceqanet = providers.FirstOrDefault(p => p.GetType().Name.StartsWith("Ceqanet", StringComparison.OrdinalIgnoreCase));
            if (ceqanet is not null)
            {
                return ceqanet;
            }
        }

        if (source.Name.StartsWith("CA_Socrata", StringComparison.OrdinalIgnoreCase)
            || source.Name.StartsWith("CA_SanJoseCkan", StringComparison.OrdinalIgnoreCase))
        {
            var caSocrata = providers.FirstOrDefault(p => p.GetType().Name.StartsWith("CaSocrata", StringComparison.OrdinalIgnoreCase));
            if (caSocrata is not null)
            {
                return caSocrata;
            }
        }

        var separator = source.Name.IndexOf('_', StringComparison.Ordinal);
        if (separator > 0)
        {
            var prefix = source.Name[..separator];
            foreach (var provider in providers)
            {
                if (provider.GetType().Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return provider;
                }
            }
        }

        return providers[^1];
    }
}
