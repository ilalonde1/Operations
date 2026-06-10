#nullable enable
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.People;

/// <summary>
/// Chokepoint for person-research refreshes. Parallels
/// <c>IEnrichmentTrackingStore.RecordAttemptAsync</c> (orgs) and
/// <c>IMajorProjectEnrichmentTrackingStore.RecordAttemptAsync</c> (projects).
///
/// Implementation contract:
///   1. Resolve the person's current employer canonical org id (or no-op if
///      the person has no current affiliation).
///   2. Archive the raw JSON in a per-person CanonicalOrgEnrichment row keyed
///      to (currentEmployerOrgId, $"PersonBrief-{intelPersonId}").
///   3. Decompose the JSON via PersonBriefExtractor and persist the
///      drafts (IntelPerson update + IntelPersonAffiliation + IntelSignal +
///      IntelAction) through the existing IntelPersistenceService.
///   4. Retire superseded rows for the synthetic ProviderName, mirroring the
///      org-side R89 retirement pattern.
/// </summary>
public interface IPersonRefreshChokepoint
{
    /// <param name="providerPrefix">
    /// Synthetic-provider prefix for the archive row + intel tagging.
    /// Default "PersonBrief" (first-pass). The drain honing path passes
    /// "PersonBriefHoning" — BD-Audit-2026-06-09 M11: with the prefix
    /// hardcoded, honing output could never write PersonBriefHoning-{id},
    /// so the batch generator re-selected the same people forever and the
    /// honing payload overwrote the first-pass brief.
    /// </param>
    Task RecordAttemptAsync(
        long intelPersonId,
        EnrichmentResult result,
        DateTimeOffset nextRefreshAtUtc,
        CancellationToken ct,
        string providerPrefix = "PersonBrief");
}
