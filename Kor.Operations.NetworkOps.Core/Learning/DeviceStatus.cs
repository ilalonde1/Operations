#nullable enable
using Kor.Operations.NetworkOps.Core.Health;

namespace Kor.Operations.NetworkOps.Core.Learning;

public enum HealthState { Unknown = 0, Healthy = 1, Watch = 2, Attention = 3, Critical = 4 }

/// <summary>An open finding as the Command Center sees it, including whether Ian has acknowledged or snoozed it.</summary>
public sealed record OpenFindingView(string RuleKey, Severity Severity, DateTime? AcknowledgedUtc, DateTime? SnoozedUntilUtc);

// The one colour per PC on the fleet grid. Acknowledged or snoozed findings stop counting -- a
// known, accepted condition must not keep a PC red, or red stops meaning anything. Info alone is
// "Watch", not "Healthy": it is worth a look but nobody needs to act today.
public static class DeviceStatus
{
    public static HealthState Of(bool everProbed, IEnumerable<OpenFindingView> open, DateTime nowUtc)
    {
        if (!everProbed) return HealthState.Unknown;
        var live = open.Where(f => f.AcknowledgedUtc is null && (f.SnoozedUntilUtc is null || f.SnoozedUntilUtc <= nowUtc)).ToList();
        if (live.Count == 0) return HealthState.Healthy;
        return live.Max(f => f.Severity) switch
        {
            Severity.Critical => HealthState.Critical,
            Severity.Warning => HealthState.Attention,
            _ => HealthState.Watch,
        };
    }
}

public enum InsightChange { New, Unchanged, Cleared }

// Fleet insights are announced like findings: once when a pattern appears, once when it goes.
public static class InsightDiff
{
    public static IReadOnlyList<(InsightChange Kind, string Key, FleetInsight? Current)> Compute(IReadOnlyCollection<string> activeKeys, IReadOnlyCollection<FleetInsight> found)
    {
        var active = new HashSet<string>(activeKeys, StringComparer.Ordinal);
        var list = new List<(InsightChange, string, FleetInsight?)>();
        foreach (var i in found) list.Add((active.Contains(i.Key) ? InsightChange.Unchanged : InsightChange.New, i.Key, i));
        var foundKeys = new HashSet<string>(found.Select(i => i.Key), StringComparer.Ordinal);
        foreach (var k in active.Where(k => !foundKeys.Contains(k))) list.Add((InsightChange.Cleared, k, null));
        return list;
    }
}
