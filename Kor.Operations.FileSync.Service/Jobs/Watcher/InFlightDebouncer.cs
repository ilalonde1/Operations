#nullable enable
using System.Collections.Concurrent;

namespace Kor.Operations.FileSync.Service.Jobs.Watcher;

// In-flight + recent-debounce gate. Verbatim from watcher.ps1 Try-Run §120-213:
//
//   * If a (root|bucket) key is already running, drop the call (coalesce).
//   * Else if the same key fired within DebounceSeconds, drop unless caller
//     passes `force` (covers the "control-file removed" forced re-runs).
//   * Else mark as in-flight, register Now in the recent map, and return
//     true so the caller can run the work. Caller MUST call Release(key)
//     in a finally so a stuck run never blocks future events.
internal sealed class InFlightDebouncer
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _recent = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _inflight = new(StringComparer.OrdinalIgnoreCase);

    public bool TryStart(string key, TimeSpan debounce, bool force, out string skipReason)
    {
        skipReason = string.Empty;

        if (_inflight.ContainsKey(key))
        {
            skipReason = "in-flight";
            return false;
        }

        if (!force && _recent.TryGetValue(key, out var last))
        {
            var age = DateTimeOffset.Now - last;
            if (age < debounce)
            {
                skipReason = $"debounce ({age.TotalSeconds:0.0}s < {debounce.TotalSeconds:0}s)";
                return false;
            }
        }

        if (!_inflight.TryAdd(key, 0))
        {
            // Lost the race -- another thread just started the same key.
            skipReason = "in-flight (raced)";
            return false;
        }

        _recent[key] = DateTimeOffset.Now;
        return true;
    }

    public void Release(string key)
    {
        _inflight.TryRemove(key, out _);
    }

    public int InFlightCount => _inflight.Count;
}
