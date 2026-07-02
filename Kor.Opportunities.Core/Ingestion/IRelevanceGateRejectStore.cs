#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Core.Ingestion;

/// <summary>
/// Persists relevance-gate rejections so systematic false negatives (a
/// vocabulary gap, a word-trap like "Coal Harbour" vs \bcoal\b) can be
/// reviewed periodically instead of evaporating with the log files.
/// One row per (source, title); repeat rejections bump a counter.
/// </summary>
public interface IRelevanceGateRejectStore
{
    /// <summary>
    /// Records one gate rejection. Implementations MUST NOT throw — a reject
    /// bookkeeping failure must never fail the ingestion run it decorates.
    /// </summary>
    Task RecordAsync(
        string sourceName,
        string title,
        string? buyer,
        string? url,
        string rejectReason,
        CancellationToken ct);
}
