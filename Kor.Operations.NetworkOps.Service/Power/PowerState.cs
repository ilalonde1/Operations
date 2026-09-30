#nullable enable
using Kor.Operations.NetworkOps.Core.Learning;
using Kor.Operations.NetworkOps.Core.Power;

namespace Kor.Operations.NetworkOps.Service.Power;

// What the watcher knows right now, shared with the API in the same process: the newest reading of
// each UPS, since when each has been seen on battery, and the current verdict. In memory only -- the
// history is in NetworkOps.PowerReadings / PowerEvents.
internal sealed class PowerState
{
    private readonly object _gate = new();
    private readonly Dictionary<string, UpsView> _views = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _addresses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _lastError = new(StringComparer.OrdinalIgnoreCase);

    public PowerVerdict Verdict { get; private set; } = new(PowerLevel.Degraded, "no reading yet");
    public DateTime? VerdictSinceUtc { get; private set; }
    public bool Configured { get; set; }

    /// <summary>Records a poll; true when it changed what matters (source, or answering vs not) since the last one.</summary>
    /// <remarks>
    /// A failed poll does NOT replace the last good reading: the view keeps it, and the policy calls the card
    /// blind only once that reading is BlindAfterSeconds old. One lost UDP packet from the UPS that is carrying
    /// the rack on mains must never read as "no UPS on mains" -- that is how a watcher causes the outage.
    /// </remarks>
    public bool Update(UpsReading r, string address, DateTime nowUtc)
    {
        lock (_gate)
        {
            _addresses[r.Ups] = address;
            var prev = _views.GetValueOrDefault(r.Ups);
            var prevError = _lastError.GetValueOrDefault(r.Ups);
            _lastError[r.Ups] = r.Reachable ? null : r.Error ?? "no answer";
            if (!r.Reachable && prev is not null)
                return prevError is null;   // answering -> silent is a change; the good reading stands

            // "Seen on battery since" survives mains -> battery -> (card silent) -> battery: only a reading
            // that says mains resets it.
            DateTime? since = r.Reachable && r.Source == PowerSource.Battery ? prev?.ObservedOnBatterySinceUtc ?? nowUtc : null;
            _views[r.Ups] = new UpsView(r, since);
            return prev is null || prevError is not null || prev.Latest.Source != r.Source;
        }
    }

    public IReadOnlyList<UpsView> Views()
    {
        lock (_gate) return _views.Values.ToList();
    }

    /// <summary>True when the level changed.</summary>
    public bool SetVerdict(PowerVerdict v, DateTime nowUtc)
    {
        lock (_gate)
        {
            var changed = v.Level != Verdict.Level;
            if (changed || VerdictSinceUtc is null) VerdictSinceUtc = nowUtc;
            Verdict = v;
            return changed;
        }
    }

    public PowerSnapshot Snapshot(bool armed, IReadOnlyList<PowerEventRow> events)
    {
        lock (_gate)
        {
            var ups = _views.Values.OrderBy(v => v.Latest.Ups).Select(v =>
            {
                var error = _lastError.GetValueOrDefault(v.Latest.Ups);
                return new UpsRow(v.Latest.Ups, _addresses.GetValueOrDefault(v.Latest.Ups, ""), v.Latest.AtUtc, error is null && v.Latest.Reachable,
                    v.Latest.Source.ToString(), v.Latest.SecondsOnBattery, v.Latest.MinutesRemaining, v.Latest.ChargePercent, v.Latest.LoadPercent,
                    v.Latest.BatteryLow, v.Latest.ReplaceBattery, error ?? v.Latest.Error);
            }).ToList();
            return Configured
                ? new PowerSnapshot(ups, Verdict.Level.ToString(), Verdict.Reason, VerdictSinceUtc, armed, events)
                : new PowerSnapshot(ups, "Off", "the UPS watcher is not configured on this service", null, armed, events);
        }
    }
}
