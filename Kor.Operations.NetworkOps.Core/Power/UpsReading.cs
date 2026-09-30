#nullable enable
using System.Globalization;

namespace Kor.Operations.NetworkOps.Core.Power;

public enum PowerSource { Unknown, Mains, Battery, Bypass, Off }

/// <summary>One read of one UPS card. A card that did not answer is still a reading (Reachable = false):
/// silence is a finding, and the trigger policy has to reason about it.</summary>
public sealed record UpsReading(
    string Ups,
    DateTime AtUtc,
    bool Reachable,
    PowerSource Source,
    int? SecondsOnBattery,
    int? MinutesRemaining,
    int? ChargePercent,
    int? LoadPercent,
    bool BatteryLow,
    bool ReplaceBattery,
    string? Error)
{
    public static UpsReading Unreachable(string ups, DateTime atUtc, string error)
        => new(ups, atUtc, false, PowerSource.Unknown, null, null, null, null, false, false, error);
}

/// <summary>
/// The two MIBs our cards speak. The Eaton Network-M3 answers the standard UPS-MIB (RFC 1628); the APC
/// NMC3 is read through APC's PowerNet-MIB, whose status codes are richer and differ. Values arrive as
/// decimal strings, TimeTicks as hundredths of a second (the transport normalises them), and an OID the
/// card does not have is simply absent from the map.
/// </summary>
public static class UpsMibs
{
    public const string UpsMib = "UpsMib";
    public const string PowerNet = "PowerNet";

    // RFC 1628 UPS-MIB
    public const string UpsBatteryStatus = "1.3.6.1.2.1.33.1.2.1.0";       // 1 unknown, 2 normal, 3 low, 4 depleted
    public const string UpsSecondsOnBattery = "1.3.6.1.2.1.33.1.2.2.0";    // 0 when on mains
    public const string UpsMinutesRemaining = "1.3.6.1.2.1.33.1.2.3.0";
    public const string UpsChargeRemaining = "1.3.6.1.2.1.33.1.2.4.0";
    public const string UpsOutputSource = "1.3.6.1.2.1.33.1.4.1.0";        // 2 none, 3 normal, 4 bypass, 5 battery, 6 booster, 7 reducer
    public const string UpsOutputLoad = "1.3.6.1.2.1.33.1.4.4.1.5.1";

    // APC PowerNet-MIB
    public const string ApcOutputStatus = "1.3.6.1.4.1.318.1.1.1.4.1.1.0";      // upsBasicOutputStatus
    public const string ApcBatteryStatus = "1.3.6.1.4.1.318.1.1.1.2.1.1.0";     // 2 normal, 3 low, 4 in fault
    public const string ApcTimeOnBattery = "1.3.6.1.4.1.318.1.1.1.2.1.2.0";     // TimeTicks
    public const string ApcRuntimeRemaining = "1.3.6.1.4.1.318.1.1.1.2.2.3.0";  // TimeTicks
    public const string ApcCapacity = "1.3.6.1.4.1.318.1.1.1.2.2.1.0";
    public const string ApcReplaceIndicator = "1.3.6.1.4.1.318.1.1.1.2.2.4.0";  // 1 no, 2 replace
    public const string ApcOutputLoad = "1.3.6.1.4.1.318.1.1.1.4.2.3.0";

    public static IReadOnlyList<string> OidsFor(string mib) => mib == PowerNet
        ? [ApcOutputStatus, ApcBatteryStatus, ApcTimeOnBattery, ApcRuntimeRemaining, ApcCapacity, ApcReplaceIndicator, ApcOutputLoad]
        : [UpsOutputSource, UpsBatteryStatus, UpsSecondsOnBattery, UpsMinutesRemaining, UpsChargeRemaining, UpsOutputLoad];

    public static UpsReading Parse(string ups, string mib, DateTime atUtc, IReadOnlyDictionary<string, string> v)
        => mib == PowerNet ? ParsePowerNet(ups, atUtc, v) : ParseUpsMib(ups, atUtc, v);

    private static UpsReading ParseUpsMib(string ups, DateTime at, IReadOnlyDictionary<string, string> v)
    {
        var source = Int(v, UpsOutputSource) switch
        {
            3 or 6 or 7 => PowerSource.Mains,   // booster / reducer are mains with voltage correction
            5 => PowerSource.Battery,
            4 => PowerSource.Bypass,
            2 => PowerSource.Off,
            _ => PowerSource.Unknown,
        };
        var battery = Int(v, UpsBatteryStatus);
        return new UpsReading(ups, at, true, source,
            Int(v, UpsSecondsOnBattery), Int(v, UpsMinutesRemaining), Int(v, UpsChargeRemaining), Int(v, UpsOutputLoad),
            BatteryLow: battery is 3 or 4, ReplaceBattery: false, Error: MissingError(v, OidsFor(UpsMib)));
    }

    private static UpsReading ParsePowerNet(string ups, DateTime at, IReadOnlyDictionary<string, string> v)
    {
        var source = Int(v, ApcOutputStatus) switch
        {
            2 or 4 or 12 or 13 or 14 => PowerSource.Mains,   // online, smart boost, smart trim, eco, hot standby
            3 or 15 => PowerSource.Battery,                  // on battery, on battery test
            6 or 9 or 10 => PowerSource.Bypass,
            5 or 7 or 8 or 11 => PowerSource.Off,            // timed sleeping, off, rebooting, sleeping until power returns
            _ => PowerSource.Unknown,
        };
        var onBattery = Ticks(v, ApcTimeOnBattery);
        var remaining = Ticks(v, ApcRuntimeRemaining);
        return new UpsReading(ups, at, true, source,
            onBattery is { } s ? (int)(s / 100) : null,
            remaining is { } r ? (int)(r / 6000) : null,
            Int(v, ApcCapacity), Int(v, ApcOutputLoad),
            BatteryLow: Int(v, ApcBatteryStatus) == 3,
            ReplaceBattery: Int(v, ApcReplaceIndicator) == 2,
            Error: MissingError(v, OidsFor(PowerNet)));
    }

    private static string? MissingError(IReadOnlyDictionary<string, string> v, IReadOnlyList<string> oids)
    {
        var missing = oids.Count(o => !v.ContainsKey(o));
        return missing == 0 ? null : $"{missing} of {oids.Count} values missing";
    }

    private static int? Int(IReadOnlyDictionary<string, string> v, string oid)
        => v.TryGetValue(oid, out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static long? Ticks(IReadOnlyDictionary<string, string> v, string oid)
        => v.TryGetValue(oid, out var s) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
}
