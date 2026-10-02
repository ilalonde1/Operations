#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Learning;

namespace Kor.Operations.App.NetworkOps;

/// <summary>One PC on the fleet grid: one colour, one line saying what matters most.</summary>
public sealed class FleetRow
{
    public required int DeviceId { get; init; }
    public required string Name { get; init; }
    public required HealthState State { get; init; }
    public required string Model { get; init; }
    public required string Headline { get; init; }
    public required int LiveCount { get; init; }
    public required string LastCheckedText { get; init; }
    public required string LastSeenText { get; init; }
    public required bool IsStale { get; init; }
    public required DeviceRow Device { get; init; }

    /// <summary>Workstation, or what the rack device is (Host, Storage, UPS, Backup, Network, Internet).</summary>
    public string Kind => Device.Kind;
    /// <summary>Who was on the PC at the last check ("kevinw · active", "nobody signed in").</summary>
    public string Presence => Device.Presence ?? "";

    /// <summary>The agent in a word or two: "connected", "quiet 3 h ago", or blank for a PC without one (the version is in the PC's window).</summary>
    public string Agent => Device.AgentVersion is null ? ""
        : Device.AgentConnected ? "connected"
        : $"quiet {CommandCenterView.Ago(Device.AgentLastContactUtc, DateTime.UtcNow)}";
    public bool IsRack => Kor.Operations.NetworkOps.Core.Rack.RackKinds.IsRack(Device.Kind);

    public string StateLabel => NetworkOpsText.Label(State);

    // ---- the card: the name, what it is, what matters, and one line of who/when ----

    /// <summary>The name without the role a rack device carries in brackets: "KOR-DC01 (domain controller, DNS, DHCP)" -> "KOR-DC01".</summary>
    public string Title => IsRack && Name.IndexOf(" (", StringComparison.Ordinal) is var i and > 0 ? Name[..i] : Name;

    /// <summary>What it is: a rack device's role ("domain controller, DNS, DHCP"), else its kind; a PC's model.</summary>
    public string Subtitle
    {
        get
        {
            if (IsRack)
                return Name.IndexOf(" (", StringComparison.Ordinal) is var i and > 0 && Name.EndsWith(')') ? Name[(i + 2)..^1] : Kind;
            return Model.Length > 0 ? Model : "PC";
        }
    }

    /// <summary>One line under the card: who is on a PC, its agent only when something is off with it ("connected" is the
    /// normal case and would push the rest off the card), when it was checked; a rack device's kind and last read.</summary>
    public string Footer => IsRack
        ? $"{Kind} · read {LastCheckedText}"
        : string.Join(" · ", new[] { Presence, Agent == "connected" ? "" : Agent.Length > 0 ? $"agent {Agent}" : "no agent", $"checked {LastCheckedText}" }.Where(s => s.Length > 0));

    /// <summary>Most urgent first: worst state, then most live findings, then name.</summary>
    internal static IEnumerable<FleetRow> Ordered(IEnumerable<FleetRow> rows)
        => rows.OrderByDescending(r => r.State).ThenByDescending(r => r.LiveCount).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase);

    internal static FleetRow From(DeviceRow d, FleetSnapshot s, DateTime nowUtc)
    {
        var open = s.OpenOn(d.Name).ToList();
        var live = open.Where(f => !f.IsQuiet(nowUtc)).OrderByDescending(f => f.Severity).ThenBy(f => f.FirstSeenUtc).ToList();
        var quiet = open.Count - live.Count;
        var state = DeviceStatus.Of(d.LastCheckedUtc is not null, open.Select(f => f.AsView()), nowUtc);
        var rack = Kor.Operations.NetworkOps.Core.Rack.RackKinds.IsRack(d.Kind);
        var headline = state switch
        {
            HealthState.Unknown => d.LastReachableUtc is null ? "Never reached" : "Not checked yet",
            // A healthy rack device says what it is doing ("5 of 6 VMs running ..."), not just "no problems".
            _ when live.Count == 0 => rack && d.Summary is { Length: > 0 } sum ? sum
                                    : quiet == 0 ? "No problems found" : $"Nothing new ({quiet} acknowledged or snoozed)",
            _ => live.Count == 1 ? live[0].Title : $"{live[0].Title}  (+{live.Count - 1} more)",
        };
        var facts = s.FactsOf(d.Name);
        return new FleetRow
        {
            DeviceId = d.DeviceId,
            Name = d.Name,
            State = state,
            Model = facts.TryGetValue(Facts.Model, out var m) ? m : "",
            Headline = headline,
            LiveCount = live.Count,
            LastCheckedText = CommandCenterView.Ago(d.LastCheckedUtc, nowUtc),
            LastSeenText = CommandCenterView.Ago(d.LastReachableUtc, nowUtc),
            // The rack is read every 5 minutes: 15 minutes without a good read is stale. PCs are checked hourly
            // in business hours, so theirs is days.
            IsStale = rack ? d.LastCheckedUtc is not { } lc || nowUtc - lc > TimeSpan.FromMinutes(15)
                           : CommandCenterView.FreshnessOf(d.LastCheckedUtc, nowUtc) == Freshness.Stale,
            Device = d,
        };
    }

    internal bool Matches(string filter)
        => Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
           || Model.Contains(filter, StringComparison.OrdinalIgnoreCase)
           || Headline.Contains(filter, StringComparison.OrdinalIgnoreCase);
}

/// <summary>One part of a PC as a tile in its window's "This PC" strip (Core/Health/PcComponents decides what and which
/// findings are about it; this only draws it).</summary>
public sealed class ComponentTile
{
    private static readonly Brush Clear = FreezeHex(0xCB, 0xD2, 0xD9);
    private static readonly Brush Bar = FreezeHex(0x5B, 0x7A, 0x99);

    public required PcComponent Part { get; init; }
    public string Title => Part.Title;
    public string Line1 => Part.Line1;
    public string Line2 => Part.Line2;
    public bool HasLine2 => Part.Line2.Length > 0;
    public bool HasFill => Part.FillPct is not null;
    /// <summary>The used-space bar's width, out of the tile's 132 px.</summary>
    public double FillWidth => Math.Clamp(Part.FillPct ?? 0, 0, 100) * 1.32;
    public string FillText => Part.FillPct is { } p ? $"{p:0}% used" : "";
    /// <summary>The colour down the tile's edge: the worst open finding about this part, else quiet grey.</summary>
    public Brush Brush => Part.Worst is { } w ? NetworkOpsBrushes.For(w) : Clear;
    /// <summary>The bar turns amber past 90% used, red past 95%: the space itself, whatever the findings say.</summary>
    public Brush FillBrush => Part.FillPct switch { >= 95 => NetworkOpsBrushes.Critical, >= 90 => NetworkOpsBrushes.Attention, _ => Bar };
    public bool HasProblem => Part.Worst is not null;
    public string Glyph => Part.Kind switch
    {
        "cpu" => "", "memory" => "", "gpu" => "", "drive" => "",
        "missing-drive" => "", "windows" => "", "bios" => "", _ => "",
    };
    public string ToolTip { get; init; } = "";

    private static Brush FreezeHex(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }
}

/// <summary>A fleet pattern: what the PCs with a problem have in common that the others do not.</summary>
public sealed class PatternRow
{
    public required string Summary { get; init; }
    public required string MembersText { get; init; }
    public required string SinceText { get; init; }
}

/// <summary>One UPS on the Rack power card: one colour and one line.</summary>
public sealed class UpsLine
{
    public required string Name { get; init; }
    public string Address { get; init; } = "";
    public required string Detail { get; init; }
    public required Brush Brush { get; init; }

    /// <summary>Mains = healthy; battery or a low/replace battery = critical/attention; not answering = grey.</summary>
    internal static UpsLine From(UpsRow u, DateTime nowUtc)
    {
        var source = !u.Reachable ? "NOT ANSWERING" : u.Source switch
        {
            "Mains" => "On mains",
            "Battery" => $"ON BATTERY {(u.SecondsOnBattery ?? 0) / 60} min",
            "Bypass" => "Bypass (mains, unprotected)",
            "Off" => "OUTPUT OFF",
            _ => "state unknown",
        };
        var parts = new List<string> { source };
        if (u.MinutesRemaining is { } m) parts.Add($"{m} min runtime");
        if (u.ChargePercent is { } c) parts.Add($"{c}% charged");
        if (u.LoadPercent is { } l) parts.Add($"load {l}%");
        if (u.BatteryLow) parts.Add("LOW BATTERY");
        if (u.ReplaceBattery) parts.Add("replace battery");
        if (nowUtc - u.AtUtc > TimeSpan.FromMinutes(2)) parts.Add($"read {CommandCenterView.Ago(u.AtUtc, nowUtc)}");   // fresh is the normal case: only say when it is not
        var brush = !u.Reachable ? NetworkOpsBrushes.Unknown
            : u.Source is "Battery" or "Off" || u.BatteryLow ? NetworkOpsBrushes.Critical
            : u.ReplaceBattery || u.Source == "Bypass" ? NetworkOpsBrushes.Attention
            : u.Source == "Mains" ? NetworkOpsBrushes.Healthy : NetworkOpsBrushes.Unknown;
        return new UpsLine { Name = u.Name, Address = u.Address, Detail = string.Join(" · ", parts), Brush = brush };
    }
}

/// <summary>An open finding on one PC.</summary>
public sealed class FindingRow
{
    public required FleetFinding Finding { get; init; }
    public required string SinceText { get; init; }
    public required string QuietText { get; init; }

    public Severity Severity => Finding.Severity;
    public string Title => Finding.Title;
    public string Evidence => Finding.Evidence;
}

/// <summary>A finding that has cleared on this PC, and what cleared it if the service could tell.</summary>
public sealed class ClearedRow
{
    public required string Title { get; init; }
    public required string WhenText { get; init; }
    public required string How { get; init; }
}

public sealed class ChangeRow
{
    public required string WhenText { get; init; }
    public required string Description { get; init; }
}

/// <summary>A fix run on this machine: when, what, who, and what it said.</summary>
public sealed class ActionLine
{
    public required string WhenText { get; init; }
    public required string Fix { get; init; }
    public required string By { get; init; }
    public required string Status { get; init; }
    public required string Result { get; init; }

    internal static ActionLine From(ActionRow a) => new()
    {
        WhenText = NetworkOpsText.When(a.RequestedUtc),
        Fix = Kor.Operations.NetworkOps.Core.Actions.FixCatalog.Get(a.Kind)?.Title ?? a.Kind,
        By = a.RequestedBy,
        Status = a.Status,
        Result = a.Detail ?? "",
    };
}

public sealed class NoteLine
{
    public required string Header { get; init; }
    public required string Body { get; init; }
}

internal static class NetworkOpsText
{
    public static string Label(HealthState s) => s switch
    {
        HealthState.Critical => "Critical",
        HealthState.Attention => "Needs attention",
        HealthState.Watch => "Watch",
        HealthState.Healthy => "Healthy",
        _ => "Unknown",
    };

    /// <summary>
    /// A fleet pattern as a person says it: "opushutil.exe keeps crashing: 2 of 2 PCs with Access engine 2016 16.0.5044.1000,
    /// against 0 of 3 without" -- the problem's own title and the fact in words, not "crash-loop:" and "access.engine.2016 =".
    /// </summary>
    public static string Pattern(ActivePattern p, IEnumerable<FleetFinding> open)
    {
        var title = open.FirstOrDefault(f => FixLearning.FamilyOf(f.RuleKey) == FixLearning.FamilyOf(p.Problem))?.Title ?? p.Problem;
        return $"{title}: {p.AffectedWith} of {p.TotalWith} PCs with {Fact(p.Fact)} {p.Value}, against {p.AffectedWithout} of {p.TotalWithout} without";
    }

    /// <summary>A fact key in words: hw.model → "model", app.revit.2025 → "Revit 2025", access.engine.2016 → "Access engine 2016".</summary>
    public static string Fact(string key) => key switch
    {
        Facts.Model => "model",
        Facts.Board => "board",
        Facts.Bios => "BIOS",
        Facts.RamGb => "RAM (GB)",
        Facts.OsRelease => "Windows",
        Facts.OfficeBuild => "Office build",
        Facts.OfficeChannel => "Office channel",
        Facts.GpuName => "graphics card",
        Facts.GpuDriver => "graphics driver",
        _ when key.StartsWith(Facts.AccessEngine, StringComparison.Ordinal) => "Access engine" + key[Facts.AccessEngine.Length..].Replace('.', ' '),
        _ when key.StartsWith(Facts.AppPrefix, StringComparison.Ordinal) => Words(key[Facts.AppPrefix.Length..]),
        _ => key,
    };

    private static readonly HashSet<string> Acronyms = new(StringComparer.OrdinalIgnoreCase) { "etabs", "safe", "csi" };

    private static string Words(string dotted)
        => string.Join(" ", dotted.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => Acronyms.Contains(w) ? w.ToUpperInvariant() : char.ToUpperInvariant(w[0]) + w[1..]));

    /// <summary>A local date a person reads: "Mon 29 Sep 14:05".</summary>
    public static string When(DateTime utc) => utc.ToLocalTime().ToString("ddd d MMM HH:mm", CultureInfo.CurrentCulture);
}

// The palette FileSync's Command Center already uses, so the two pages mean the same thing by a colour.
internal static class NetworkOpsBrushes
{
    public static readonly Brush Critical = Freeze(Color.FromRgb(0xC1, 0x1E, 0x1E));
    public static readonly Brush Attention = Freeze(Color.FromRgb(0xE5, 0xA8, 0x00));
    public static readonly Brush Watch = Freeze(Color.FromRgb(0x60, 0x9B, 0xD1));
    public static readonly Brush Healthy = Freeze(Color.FromRgb(0x22, 0x8B, 0x22));
    public static readonly Brush Unknown = Freeze(Color.FromRgb(0x80, 0x80, 0x80));

    public static Brush For(HealthState s) => s switch
    {
        HealthState.Critical => Critical,
        HealthState.Attention => Attention,
        HealthState.Watch => Watch,
        HealthState.Healthy => Healthy,
        _ => Unknown,
    };

    public static Brush For(Severity s) => s switch
    {
        Severity.Critical => Critical,
        Severity.Warning => Attention,
        _ => Watch,
    };

    private static Brush Freeze(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
}

public sealed class HealthStateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            HealthState h => NetworkOpsBrushes.For(h),
            Severity s => NetworkOpsBrushes.For(s),
            _ => NetworkOpsBrushes.Unknown,
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
