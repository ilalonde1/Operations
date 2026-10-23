#nullable enable
using System.Globalization;
using System.Text.RegularExpressions;

namespace Kor.Operations.NetworkOps.Core.Power;

/// <summary>
/// What a UPS card says about itself and its battery: the card's firmware, the UPS's firmware, when the battery went in,
/// when the card recommends replacing it, and the part to order. Read from <see cref="UpsMibs.CardOidsFor"/> by the rack
/// sweep. 2026-10-21 the APC card mailed "battery near end of life" for days while NetworkOps showed the UPS green: it read
/// only the replace-NOW flag (upsAdvBatteryReplaceIndicator), which stays "no" until the battery has actually failed. The
/// card's own recommended date is what its warning is about, so that is what the finding reads.
/// </summary>
public sealed record UpsCardInfo(string? CardModel, string? CardFirmware, string? UpsFirmware, DateTime? BatteryInstalled, DateTime? BatteryReplaceBy, string? BatteryPart)
{
    /// <summary>Within this long of the card's recommended replace date: order the battery (Info).</summary>
    public static readonly TimeSpan OrderWindow = TimeSpan.FromDays(182);
    /// <summary>Within this long, or past it: replace it (Warning).</summary>
    public static readonly TimeSpan ReplaceWindow = TimeSpan.FromDays(30);

    public static UpsCardInfo Parse(string mib, IReadOnlyDictionary<string, string> v)
    {
        var descr = Get(v, UpsMibs.SysDescr);
        if (mib == UpsMibs.PowerNet)
            return new UpsCardInfo(
                Field(descr, "MN") is { } mn ? "APC " + mn : null,
                Field(descr, "PF")?.TrimStart('v', 'V'),
                Get(v, UpsMibs.ApcUpsFirmware),
                Date(Get(v, UpsMibs.ApcBatteryLastReplaced)),
                Date(Get(v, UpsMibs.ApcBatteryReplaceBy)),
                Get(v, UpsMibs.ApcBatteryPart));
        return new UpsCardInfo(
            descr is { Length: > 0 } ? "Eaton " + descr : null,
            Get(v, UpsMibs.UpsMibAgentFirmware),
            Get(v, UpsMibs.UpsMibUpsFirmware),
            Date(Get(v, UpsMibs.EatonBatteryLastReplaced)),
            null,   // the Eaton card publishes no recommended date; its battery age shows as a fact only
            null);
    }

    /// <summary>The device facts this read records (shown on the UPS's page; the firmware ones feed the version baseline).</summary>
    public IReadOnlyDictionary<string, string> Facts()
    {
        var f = new Dictionary<string, string>(StringComparer.Ordinal);
        void Put(string k, string? s) { if (!string.IsNullOrWhiteSpace(s)) f[k] = s.Trim(); }
        Put("ups.card", CardModel);
        Put("ups.card-firmware", CardFirmware);
        Put("ups.firmware", UpsFirmware);
        Put("ups.battery-installed", BatteryInstalled is { } i ? Health.Dates.Day(i) : null);
        Put("ups.battery-replace-by", BatteryReplaceBy is { } r ? Health.Dates.Day(r) : null);
        Put("ups.battery-part", BatteryPart);
        return f;
    }

    private static string? Get(IReadOnlyDictionary<string, string> v, string oid) => v.TryGetValue(oid, out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;

    // APC's sysDescr carries "KEY:value" pairs separated by spaces: MN (model), PF (the card's AOS firmware), MB, HR, ...
    private static string? Field(string? descr, string key) =>
        descr is null ? null : Regex.Match(descr, $@"\b{key}:(\S+)") is { Success: true } m ? m.Groups[1].Value : null;

    // Both cards write dates as MM/dd/yyyy.
    private static DateTime? Date(string? s) =>
        s is not null && DateTime.TryParseExact(s, ["MM/dd/yyyy", "M/d/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
            ? DateTime.SpecifyKind(d.Date, DateTimeKind.Utc) : null;
}
