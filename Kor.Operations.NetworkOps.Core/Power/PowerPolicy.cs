#nullable enable
namespace Kor.Operations.NetworkOps.Core.Power;

public enum PowerLevel { Normal, Degraded, Trigger }

public sealed record PowerVerdict(PowerLevel Level, string Reason);

/// <summary>What the watcher knows about one UPS: its newest reading, and since when (by our own clock) it
/// has been seen on battery -- the fallback when the card's own on-battery counter is missing.</summary>
public sealed record UpsView(UpsReading Latest, DateTime? ObservedOnBatterySinceUtc);

public sealed record PowerPolicySettings(
    int OnBatteryMinutes = 5,
    int RuntimeFloorMinutes = 15,
    int BlindAfterSeconds = 120);

/// <summary>
/// When the rack must be shut down. Every server and the SAN has one cord on EACH UPS, so a device
/// loses power only when BOTH UPSes stop giving it mains. The rule follows from that:
///
///   while any UPS is confirmed on mains, nothing is shut down -- one UPS on battery (its circuit
///   tripped, its input cord out) is an alert, never a shutdown, because the other is carrying the
///   rack and shutting down would cause the very outage it is meant to prevent.
///
///   with no UPS confirmed on mains and at least one on battery: shut down when every UPS on battery
///   has been on it for OnBatteryMinutes, or any of them reports a low battery, or any has less than
///   RuntimeFloorMinutes left.
///
/// A card that has not answered for BlindAfterSeconds is BLIND: it confirms nothing, in either
/// direction. Two blind cards trigger nothing (there is no evidence of an outage, only of a network
/// fault), so a watcher that cannot see never shuts the rack down; one blind card beside one on
/// battery triggers only on concrete danger (low battery, short runtime), never on time alone.
/// A single missed poll is not blindness: the caller keeps the last good reading until it is stale.
/// </summary>
public static class PowerPolicy
{
    public static PowerVerdict Evaluate(IReadOnlyList<UpsView> upses, DateTime nowUtc, PowerPolicySettings s)
    {
        if (upses.Count == 0) return new(PowerLevel.Degraded, "no UPS is configured");

        var blindAfter = TimeSpan.FromSeconds(s.BlindAfterSeconds);
        bool Blind(UpsView u) => !u.Latest.Reachable || nowUtc - u.Latest.AtUtc > blindAfter || u.Latest.Source == PowerSource.Unknown;

        var onMains = upses.Where(u => !Blind(u) && u.Latest.Source is PowerSource.Mains or PowerSource.Bypass).ToList();
        var onBattery = upses.Where(u => !Blind(u) && u.Latest.Source == PowerSource.Battery).ToList();
        var blind = upses.Where(Blind).ToList();
        var off = upses.Where(u => !Blind(u) && u.Latest.Source == PowerSource.Off).ToList();

        if (onMains.Count > 0)
        {
            var notes = new List<string>();
            notes.AddRange(onBattery.Select(u => $"{u.Latest.Ups} ON BATTERY for {Minutes(OnBatteryFor(u, nowUtc))} min, {u.Latest.MinutesRemaining?.ToString() ?? "?"} min left -- {string.Join(", ", onMains.Select(m => m.Latest.Ups))} is carrying the rack on mains, so nothing is shut down"));
            notes.AddRange(off.Select(u => $"{u.Latest.Ups} output is OFF"));
            notes.AddRange(blind.Select(u => $"{u.Latest.Ups} not answering ({u.Latest.Error ?? "stale"})"));
            notes.AddRange(upses.Where(u => !Blind(u) && u.Latest.ReplaceBattery).Select(u => $"{u.Latest.Ups} asks for a battery replacement"));
            return notes.Count == 0 ? new(PowerLevel.Normal, "all UPSes on mains") : new(PowerLevel.Degraded, string.Join("; ", notes));
        }

        if (onBattery.Count == 0)
            return new(PowerLevel.Degraded, blind.Count == upses.Count
                ? "no UPS card is answering: power state unknown, nothing will be shut down on no evidence"
                : $"no UPS on mains or battery ({string.Join(", ", upses.Select(u => $"{u.Latest.Ups} {(Blind(u) ? "not answering" : u.Latest.Source.ToString())}"))})");

        var low = onBattery.FirstOrDefault(u => u.Latest.BatteryLow);
        if (low is not null)
            return new(PowerLevel.Trigger, $"no UPS on mains and {low.Latest.Ups} reports LOW BATTERY");

        var shortRun = onBattery.FirstOrDefault(u => u.Latest.MinutesRemaining is { } m && m < s.RuntimeFloorMinutes);
        if (shortRun is not null)
            return new(PowerLevel.Trigger, $"no UPS on mains and {shortRun.Latest.Ups} has {shortRun.Latest.MinutesRemaining} min left (floor {s.RuntimeFloorMinutes})");

        var shortest = onBattery.Min(u => OnBatteryFor(u, nowUtc));
        var described = string.Join(", ", onBattery.Select(u => $"{u.Latest.Ups} {Minutes(OnBatteryFor(u, nowUtc))} min on battery, {u.Latest.MinutesRemaining?.ToString() ?? "?"} min left"))
                        + (blind.Count > 0 ? $"; {string.Join(", ", blind.Select(b => b.Latest.Ups))} not answering" : "");
        // A blind card might be the one carrying the rack on mains (a card reboots in a minute or two while
        // its UPS carries on). So with any card blind, time on battery alone never triggers: only concrete
        // danger does -- a low battery or a short runtime, both handled above.
        if (blind.Count > 0)
            return new(PowerLevel.Degraded, $"on battery with a card not answering: shutdown only at low battery or under {s.RuntimeFloorMinutes} min left: {described}");
        return shortest >= TimeSpan.FromMinutes(s.OnBatteryMinutes)
            ? new(PowerLevel.Trigger, $"no UPS on mains for {s.OnBatteryMinutes}+ min: {described}")
            : new(PowerLevel.Degraded, $"on battery, shutdown at {s.OnBatteryMinutes} min: {described}");
    }

    /// <summary>The card's own counter when it has one (it survives a watcher restart), else our observation.</summary>
    public static TimeSpan OnBatteryFor(UpsView u, DateTime nowUtc)
    {
        var card = u.Latest.SecondsOnBattery is { } sec and > 0 ? TimeSpan.FromSeconds(sec) + (nowUtc - u.Latest.AtUtc) : TimeSpan.Zero;
        var seen = u.ObservedOnBatterySinceUtc is { } since ? nowUtc - since : TimeSpan.Zero;
        return card > seen ? card : seen;
    }

    private static string Minutes(TimeSpan t) => ((int)t.TotalMinutes).ToString();
}
