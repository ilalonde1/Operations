#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Sources;

/// <summary>
/// Read/write access to <c>opportunities.OpportunitySources</c> and the
/// per-source key/value config in <c>opportunities.OpportunitySourceMappings</c>.
/// </summary>
public interface IOpportunitySourceStore
{
    Task<IReadOnlyList<OpportunitySource>> ListEnabledAsync(CancellationToken ct);

    Task<OpportunitySource?> GetByNameAsync(string name, CancellationToken ct);

    /// <summary>Inserts the source if no row matches <paramref name="name"/>;
    /// returns the persisted row either way. Used by the Worker to ensure the
    /// canonical sources (CanadaBuys, etc.) exist on first run without requiring
    /// a manual SQL seed.</summary>
    Task<OpportunitySource> EnsureAsync(OpportunitySource desired, CancellationToken ct);

    /// <summary>Loads all key/value mappings for a source. Empty dictionary
    /// when the source has none yet.</summary>
    Task<IReadOnlyDictionary<string, string>> GetMappingsAsync(Guid opportunitySourceId, CancellationToken ct);

    /// <summary>Upserts one mapping row.</summary>
    Task SetMappingAsync(Guid opportunitySourceId, string key, string valueJson, CancellationToken ct);
}
