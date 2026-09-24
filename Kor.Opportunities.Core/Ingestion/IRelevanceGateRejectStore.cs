#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Core.Ingestion;

/// <summary>
/// Persists relevance-gate rejections so systematic false negatives (a
/// vocabulary gap, a word-trap like "Coal Harbour" vs \bcoal\b) can be
/// reviewed periodically instead of evaporating with the log files.
/// One row per (source, title); repeat rejections bump a counter.
///
/// ⚠ It takes the whole CANDIDATE, not a handful of strings. The provider has
///   already parsed the file number, the filing date, the address and the
///   description by this point, and throwing them away here makes the log
///   unreviewable: a reject with no date cannot be told from a stale one, and
///   a reject with no reference cannot be found again at the source.
/// </summary>
public interface IRelevanceGateRejectStore
{
    /// <summary>
    /// Records one gate rejection. Implementations MUST NOT throw — a reject
    /// bookkeeping failure must never fail the ingestion run it decorates.
    /// </summary>
    Task RecordAsync(
        string sourceName,
        OpportunityCandidate candidate,
        string rejectReason,
        CancellationToken ct);
}
