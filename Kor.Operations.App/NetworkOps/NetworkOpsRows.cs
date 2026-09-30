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

    public string StateLabel => NetworkOpsText.Label(State);

    /// <summary>Most urgent first: worst state, then most live findings, then name.</summary>
    internal static IEnumerable<FleetRow> Ordered(IEnumerable<FleetRow> rows)
        => rows.OrderByDescending(r => r.State).ThenByDescending(r => r.LiveCount).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase);

    internal static FleetRow From(DeviceRow d, FleetSnapshot s, DateTime nowUtc)
    {
        var open = s.OpenOn(d.Name).ToList();
        var live = open.Where(f => !f.IsQuiet(nowUtc)).OrderByDescending(f => f.Severity).ThenBy(f => f.FirstSeenUtc).ToList();
        var quiet = open.Count - live.Count;
        var state = DeviceStatus.Of(d.LastCheckedUtc is not null, open.Select(f => f.AsView()), nowUtc);
        var headline = state switch
        {
            HealthState.Unknown => d.LastReachableUtc is null ? "Never reached" : "Not checked yet",
            _ when live.Count == 0 => quiet == 0 ? "No problems found" : $"Nothing new ({quiet} acknowledged or snoozed)",
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
            IsStale = CommandCenterView.FreshnessOf(d.LastCheckedUtc, nowUtc) == Freshness.Stale,
            Device = d,
        };
    }

    internal bool Matches(string filter)
        => Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
           || Model.Contains(filter, StringComparison.OrdinalIgnoreCase)
           || Headline.Contains(filter, StringComparison.OrdinalIgnoreCase);
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
        parts.Add($"read {CommandCenterView.Ago(u.AtUtc, nowUtc)}");
        var brush = !u.Reachable ? NetworkOpsBrushes.Unknown
            : u.Source is "Battery" or "Off" || u.BatteryLow ? NetworkOpsBrushes.Critical
            : u.ReplaceBattery || u.Source == "Bypass" ? NetworkOpsBrushes.Attention
            : u.Source == "Mains" ? NetworkOpsBrushes.Healthy : NetworkOpsBrushes.Unknown;
        return new UpsLine { Name = $"{u.Name}  ({u.Address})", Detail = string.Join(" · ", parts), Brush = brush };
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
