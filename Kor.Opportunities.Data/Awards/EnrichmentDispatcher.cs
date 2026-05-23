#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Awards;

/// <summary>
/// Picks the next batch of canonical orgs due for refresh by each registered
/// IEnrichmentProvider, calls RefreshAsync, records the outcome.
/// </summary>
public sealed class EnrichmentDispatcher
{
    private readonly IEnrichmentTrackingStore _tracking;
    private readonly IEnumerable<IEnrichmentProvider> _providers;
    private readonly ILogger<EnrichmentDispatcher> _logger;

    public EnrichmentDispatcher(
        IEnrichmentTrackingStore tracking,
        IEnumerable<IEnrichmentProvider> providers,
        ILogger<EnrichmentDispatcher> logger)
    {
        _tracking = tracking;
        _providers = providers;
        _logger = logger;
    }

    public sealed record DispatchResult(int Attempted, int Ok, int Failed, int Blocked);

    public async Task<DispatchResult> DispatchBatchAsync(int batchSize, CancellationToken ct)
    {
        var totalAttempted = 0;
        var totalOk = 0;
        var totalFailed = 0;
        var totalBlocked = 0;

        foreach (var p in _providers)
        {
            ct.ThrowIfCancellationRequested();
            var due = await _tracking.ListDueAsync(p.Name, batchSize, ct).ConfigureAwait(false);
            if (due.Count == 0) continue;

            _logger.LogInformation("EnrichmentDispatcher: provider={Provider} due={Due}", p.Name, due.Count);

            foreach (var canonicalId in due)
            {
                ct.ThrowIfCancellationRequested();
                EnrichmentResult result;
                try
                {
                    result = await p.RefreshAsync(canonicalId, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Provider {Provider} threw on canonical id {Id}.", p.Name, canonicalId);
                    result = new EnrichmentResult(EnrichmentStatuses.Failed, ex.Message, null, null);
                }

                var ttl = result.Status == EnrichmentStatuses.Ok ? p.TtlOnSuccess : p.TtlOnFailure;
                var nextRefresh = DateTimeOffset.UtcNow.Add(ttl);
                await _tracking.RecordAttemptAsync(canonicalId, p.Name, result, nextRefresh, ct).ConfigureAwait(false);

                totalAttempted++;
                switch (result.Status)
                {
                    case EnrichmentStatuses.Ok:
                        totalOk++;
                        break;
                    case EnrichmentStatuses.Blocked:
                        totalBlocked++;
                        break;
                    case EnrichmentStatuses.NoData:
                        totalOk++;
                        break;
                    default:
                        totalFailed++;
                        break;
                }
            }
        }

        return new DispatchResult(totalAttempted, totalOk, totalFailed, totalBlocked);
    }
}
