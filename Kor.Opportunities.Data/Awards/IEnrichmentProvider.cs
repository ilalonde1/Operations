#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Awards;

/// <summary>
/// One source of canonical-org enrichment. Each implementation pulls from
/// a single external system (APEGBC, BC Registry, LinkedIn, news, etc.)
/// and updates either CanonicalOrg-domain columns directly or its own
/// provider-specific tables.
/// </summary>
public interface IEnrichmentProvider
{
    /// <summary>Stable name used as the key in CanonicalOrgEnrichment.ProviderName.</summary>
    string Name { get; }

    /// <summary>How often this provider's data is worth re-fetching once we have ok results.</summary>
    TimeSpan TtlOnSuccess { get; }

    /// <summary>How long to wait before retrying after a failure.</summary>
    TimeSpan TtlOnFailure { get; }

    /// <summary>
    /// Refresh the given canonical org from this provider's source.
    /// Implementations must not throw, return a Failed/Blocked status with an
    /// ErrorMessage instead. Domain-specific result writes happen inside this method.
    /// The orchestrator only records the tracking row from the returned result.
    /// </summary>
    Task<EnrichmentResult> RefreshAsync(long canonicalOrgId, CancellationToken ct);
}
