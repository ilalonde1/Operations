#nullable enable
using System.Globalization;
using System.Text.RegularExpressions;
using Kor.Operations.NetworkOps.Core.Health;

namespace Kor.Operations.NetworkOps.Core.Rack;

/// <summary>
/// A printer or plotter, from its standard SNMP answers (Printer-MIB RFC 3805, HOST-RESOURCES-MIB): model, firmware, page
/// count, every supply's level, and the printer's own error flags. Ian, 2026-10-02: "You got all the Phones and printers
/// accounted for by ports too? Firmware on those?" -- read that night with no password: the plotter's inks at 0, a Brother
/// drum at end of life.
///
/// A supply's level is the Printer-MIB's: units remaining of a maximum; -3 = "some remains, amount not reported", -2 =
/// "unknown" (the Brothers and the HP report toner that way) -- neither is a finding. A waste receptacle's level is the room
/// LEFT in it, so 0 is full.
/// </summary>
public static class PrinterRules
{
    public const string SysDescr = "1.3.6.1.2.1.1.1.0";
    public const string DeviceDescr = "1.3.6.1.2.1.25.3.2.1.3.1";
    public const string PrinterStatus = "1.3.6.1.2.1.25.3.5.1.1.1";
    public const string ErrorState = "1.3.6.1.2.1.25.3.5.1.2.1";     // a bit string: read as hex
    public const string PageCount = "1.3.6.1.2.1.43.10.2.1.4.1.1";
    private const string SupplyDescr = "1.3.6.1.2.1.43.11.1.1.6.1.";
    private const string SupplyMax = "1.3.6.1.2.1.43.11.1.1.8.1.";
    private const string SupplyLevel = "1.3.6.1.2.1.43.11.1.1.9.1.";
    public const int MaxSupplies = 12;

    /// <summary>Every OID read: the GET list (a printer answers a plain GET; not all of them walk well).</summary>
    public static readonly IReadOnlyList<string> Oids =
    [
        SysDescr, DeviceDescr, PrinterStatus, ErrorState, PageCount,
        .. Enumerable.Range(1, MaxSupplies).SelectMany(i => new[] { SupplyDescr + i, SupplyMax + i, SupplyLevel + i }),
    ];

    // hrPrinterDetectedErrorState, first octet then second (RFC 3805 / RFC 2790 bit order: the high bit is bit 0).
    // Keys are whole rule keys (each has a Knowledge entry: PrinterRulesTests holds every one of them to it, since the
    // rack rules' source scan sees only raises written with a literal key).
    public static readonly (int Byte, int Mask, string Key, Severity Severity, string Title)[] Flags =
    [
        (0, 0x04, "printer.jammed", Severity.Warning, "Paper jam"),
        (0, 0x08, "printer.door-open", Severity.Warning, "A door or cover is open"),
        (0, 0x40, "printer.no-paper", Severity.Warning, "Out of paper"),
        (0, 0x10, "printer.no-toner", Severity.Warning, "Out of toner or ink"),
        (0, 0x02, "printer.offline", Severity.Warning, "The printer is offline"),
        (0, 0x01, "printer.service", Severity.Warning, "The printer is asking for service"),
        (0, 0x20, "printer.low-toner", Severity.Info, "The printer flags low toner or ink"),
        (0, 0x80, "printer.low-paper", Severity.Info, "Paper is low"),
        (1, 0x08, "printer.output-full", Severity.Warning, "The output tray is full"),
        (1, 0x02, "printer.maintenance", Severity.Info, "Preventive maintenance is overdue"),
    ];

    public static RackResult Evaluate(IReadOnlyDictionary<string, string> v)
    {
        string? S(string oid) => v.TryGetValue(oid, out var x) && x.Trim().Length > 0 ? x.Trim() : null;
        int? I(string oid) => int.TryParse(S(oid), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
        if (S(SysDescr) is null && S(DeviceDescr) is null) return RackResult.Unreachable("no SNMP answer (community public, SNMP v1)");

        var b = new RackBuilder();
        var model = S(DeviceDescr) ?? S(SysDescr)!;
        b.Fact("hw.model", Regex.Replace(model, @"\s+\d+(\.\d+)+$", "").Trim());
        b.Fact("fw.version", FirmwareOf(S(SysDescr), S(DeviceDescr)));
        if (I(PageCount) is { } pages and >= 0) b.Metric("pages.total", pages);

        var worst = new List<string>();
        for (var i = 1; i <= MaxSupplies; i++)
        {
            if (S(SupplyDescr + i) is not { } name) break;
            var max = I(SupplyMax + i);
            var level = I(SupplyLevel + i);
            if (max is not > 0 || level is not >= 0) continue;          // -3 some remains, -2 unknown: no amount to judge
            var pct = Math.Round(100.0 * level.Value / max.Value, 1);
            b.Metric("supply.pct", pct, name);
            var receptacle = name.Contains("Waste", StringComparison.OrdinalIgnoreCase);
            var part = Regex.IsMatch(name, @"Drum|Fuser|Kit|Belt|Unit", RegexOptions.IgnoreCase) && !Regex.IsMatch(name, "Toner|Ink", RegexOptions.IgnoreCase);
            if (level == 0)
                b.Raise($"printer.supply:{name}", Severity.Warning,
                    receptacle ? $"{name} is full" : part ? $"{name} is at end of life" : $"{name} is empty",
                    $"{name}: {level} of {max} {(receptacle ? "room left" : "left")}" + (receptacle ? " -- empty or replace it" : " -- replace it"));
            else if (pct < 10 && !receptacle)
                b.Raise($"printer.supply:{name}", Severity.Info, $"{name} is low ({pct:0}% left)", $"{name}: {level} of {max} left");
        }

        if (HexBytes(S(ErrorState)) is { Length: > 0 } bits)
            foreach (var f in Flags)
                if (f.Byte < bits.Length && (bits[f.Byte] & f.Mask) != 0)
                    b.Raise(f.Key, f.Severity, f.Title, $"the printer's own error flags (hrPrinterDetectedErrorState {S(ErrorState)})");

        var status = I(PrinterStatus) switch { 3 => "idle", 4 => "printing", 5 => "warming up", 1 => "other (asleep or busy)", _ => "unknown" };
        return b.Done($"{b.Facts.GetValueOrDefault("hw.model")}, {status}" + (b.Findings.Count > 0 ? $", {b.Findings.Count} to look at" : ""));
    }

    /// <summary>The firmware as each maker reports it: Brother "Firmware Ver.1.17 (15.12.01)", HP Jetdirect "EEPROM V.33.30",
    /// Canon imageRUNNER the number after the model ("Canon iR-ADV C5840 39.13").</summary>
    public static string? FirmwareOf(string? sysDescr, string? deviceDescr)
    {
        if (sysDescr is not null && Regex.Match(sysDescr, @"Firmware Ver\.?\s*([^,]+?)\s*(,|$)") is { Success: true } brother)
            return Regex.Replace(brother.Groups[1].Value.Trim(), @"\s+", " ");   // "1.17  (15.12.01)": Brother pads it
        if (sysDescr is not null && Regex.Match(sysDescr, @"EEPROM V\.?\s*([\w.]+)") is { Success: true } hp) return hp.Groups[1].Value;
        if (deviceDescr is not null && Regex.Match(deviceDescr, @"\s(\d+(\.\d+)+)$") is { Success: true } canon) return canon.Groups[1].Value;
        return null;
    }

    /// <summary>A hex string ("20", "0400") to bytes; null when it is not one.</summary>
    public static byte[]? HexBytes(string? hex)
        => hex is { Length: > 0 } h && h.Length % 2 == 0 && Regex.IsMatch(h, "^[0-9a-fA-F]+$") ? Convert.FromHexString(h) : null;
}
