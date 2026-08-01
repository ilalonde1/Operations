#nullable enable
namespace Kor.Opportunities.Core.Scoring;

/// <summary>
/// Caches the persisted <see cref="ScoringOptions"/> so the scorer doesn't hit
/// SQL per opportunity. Refresh window is implementation-defined (CR's port
/// uses 10 s).
/// </summary>
public interface IScoringOptionsAccessor
{
    /// <summary>Returns the currently-cached options. Callers must treat the
    /// result as read-only (mutating the dictionaries leaks into the cache).</summary>
    ScoringOptions GetCurrent();

    /// <summary>Persists the supplied options as the new active profile and
    /// resets the cache so subsequent <see cref="GetCurrent"/> reads see the
    /// new values immediately.</summary>
    ScoringOptions Update(ScoringOptions updated);
}
