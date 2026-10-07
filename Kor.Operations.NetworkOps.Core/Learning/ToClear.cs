#nullable enable
using System.Linq;
using Kor.Operations.NetworkOps.Core.Actions;
using Kor.Operations.NetworkOps.Core.Health;

namespace Kor.Operations.NetworkOps.Core.Learning;

// "To clear": the one surface whose job is to drive the fleet to all-green. Every LIVE finding (not acknowledged, not
// snoozed) across the PCs and the rack, grouped BY ISSUE (rule key) rather than by machine -- so the same problem on 12
// PCs is one row with "fix on all 12", not twelve trips. Each issue carries the one fix that clears it (from FixCatalog),
// and the view counts the distance to green. Composed from the fleet snapshot the Command Center already reads; no new
// data pipeline. Built in Core so it is shared by the service (fills /api/to-clear) and tested without a database.

/// <summary>One machine an issue is open on.</summary>
public sealed record ToClearMachine(int DeviceId, string Name, long FindingId, string? Presence, string? PresenceState);

/// <summary>One issue to clear: a rule, every machine it is open on (worst/most first), and the fix that clears it (null
/// when nothing in the catalog does -- then it is Ask Claude or a person).</summary>
public sealed record ToClearIssue(string RuleKey, string Title, Severity Severity, string Evidence, DateTime FirstSeenUtc,
    IReadOnlyList<ToClearMachine> Machines, FixOption? Fix)
{
    public int Count => Machines.Count;
    /// <summary>Clears with a single click: a fix exists, it needs no input, and it does not interrupt anyone.</summary>
    public bool OneClick => Fix is { Disruptive: false, ParamLabel: null };
    /// <summary>The fix is already in flight (queued or running) on at least one affected machine -- so the view shows it
    /// as "fixing" rather than offering to queue it again. True only when a catalog fix exists for the issue.</summary>
    public bool Running { get; init; }

    /// <summary>Plain-English "what this finding means", and "what happens if it is left", from the knowledge entry for the
    /// rule's family (Knowledge.For). Null when no entry covers the family. Let the worklist say what an issue MEANS inline,
    /// instead of only a rule-shaped title and a raw number -- the explanation that otherwise lives a click away on the PC.</summary>
    public string? Meaning { get; init; }
    public string? IfIgnored { get; init; }
}

/// <summary>The worklist and the distance to green: how many issues are open, across how many machines, how many clear in
/// one click, and how many are parked (acknowledged/snoozed, so they do NOT count against green).</summary>
public sealed record ToClearView(int Issues, int Machines, int OneClickIssues, int Parked, IReadOnlyList<ToClearIssue> Open);

public static class ToClear
{
    /// <summary>Build the worklist from one or more fleet snapshots (PCs and the rack). <paramref name="nowUtc"/> decides
    /// which snoozes are still in the future (parked) vs expired (live again). <paramref name="runningTargets"/> is the set
    /// of "&lt;deviceId&gt;|&lt;fixKind&gt;" pairs with an action already in flight, so an issue being fixed shows as "fixing".</summary>
    public static ToClearView Build(IEnumerable<FleetSnapshot> snapshots, IReadOnlySet<string> runningTargets, DateTime nowUtc)
    {
        var deviceByName = new Dictionary<string, DeviceRow>(StringComparer.OrdinalIgnoreCase);
        var live = new List<FleetFinding>();
        var parked = 0;
        foreach (var s in snapshots)
        {
            foreach (var d in s.Devices) deviceByName[d.Name] = d;
            foreach (var f in s.OpenFindings)
                if (f.IsQuiet(nowUtc)) parked++;
                else live.Add(f);
        }

        var issues = live
            .GroupBy(f => f.RuleKey, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var worst = g.OrderByDescending(f => f.Severity).ThenBy(f => f.FirstSeenUtc).First();
                var machines = g
                    .OrderBy(f => f.Device, StringComparer.OrdinalIgnoreCase)
                    .Select(f => deviceByName.TryGetValue(f.Device, out var d)
                        ? new ToClearMachine(d.DeviceId, f.Device, f.FindingId, d.Presence, d.PresenceState)
                        : new ToClearMachine(0, f.Device, f.FindingId, null, null))
                    .ToList();
                var fix = FixCatalog.For(g.Key).FirstOrDefault(a => a.Id != FixCatalog.RunCommand);   // the primary fix; the escape hatch (run-command) is not "the fix"
                var option = fix is null ? null
                    : new FixOption(fix.Id, fix.Title, fix.Explain, fix.Disruptive, fix.ParamLabel, FixCatalog.ParamFromFinding(fix, g.Key));
                // Already being fixed when that fix is in flight on any machine the issue is open on.
                var running = fix is not null && machines.Any(m => runningTargets.Contains($"{m.DeviceId}|{fix.Id}"));
                // The plain-English meaning for this rule, so the worklist can say what the issue IS, not just its title.
                var know = Knowledge.For(g.Key);
                return new ToClearIssue(g.Key, worst.Title, worst.Severity, worst.Evidence, g.Min(f => f.FirstSeenUtc), machines, option)
                    { Running = running, Meaning = know?.Meaning, IfIgnored = know?.IfIgnored };
            })
            .OrderByDescending(i => i.Severity).ThenByDescending(i => i.Count).ThenBy(i => i.FirstSeenUtc)
            .ToList();

        return new ToClearView(issues.Count, issues.Sum(i => i.Count), issues.Count(i => i.OneClick), parked, issues);
    }
}
