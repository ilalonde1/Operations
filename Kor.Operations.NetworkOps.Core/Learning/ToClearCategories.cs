#nullable enable
using System.Linq;
using Kor.Operations.NetworkOps.Core.Health;

namespace Kor.Operations.NetworkOps.Core.Learning;

// The worklist, rolled up into a few broad, scannable categories for the overview -- the Ninja "Device health issues"
// model Ian asked for ("Needs reboot: 13", "Low disk: 3"): one line per kind of problem, the number of MACHINES it is on
// (what a person reads), worst-first. It groups the per-rule ToClear issues so the landing reads in one glance instead of
// a long list of individual rule cards. Clicking a category filters the list to its rules. Pure; shared by app and tests.

/// <summary>One category on the overview: a kind of problem, how many machines have it, how many distinct issues it covers,
/// its worst severity, and the rule keys it gathers (for the click-to-filter).</summary>
public sealed record ToClearCategory(string Name, int Machines, int Issues, Severity Severity, IReadOnlyList<string> RuleKeys);

public static class ToClearCategories
{
    // Display order, most-actionable first. Each category is matched by a set of rule-key predicates; first match wins, so
    // the order here is also the match precedence. A rule no category claims falls to "Other" -- it still shows, never hidden.
    private static readonly (string Name, string[] Keys)[] Catalog =
    [
        ("Offline",          ["device-silent", "rack.unreachable"]),
        ("Needs reboot",     ["reboot-overdue", "server.reboot-pending"]),
        ("Low disk",         ["low-disk", "server.disk-full", "esxi.datastore-full", "volume-full"]),
        ("Failing storage",  ["disk-failing", "disk-missing", "disk-errors", "disk-unmounted", "hardware-errors", "unexpected-shutdowns"]),
        ("Backups",          ["veeam."]),
        ("Antivirus",        ["server.av-", "av-conflict", "av-none"]),
        ("App crashes",      ["crash-loop", "crashes-rising"]),
        ("End of support",   ["version.end-of-support", "version.insecure", "os-unsupported", "baseline."]),
        ("Updates",          ["updates-due", "server.unpatched", "agent-outdated", "agent-silent"]),
        ("Network & SAN",    ["esxi.iscsi", "esxi.vm-nic", "link-fault", "fw.", "server.nic-public", "esxi.vm-off", "esxi.vm-tools"]),
        ("Printers",         ["printer."]),
    ];

    /// <summary>The category a rule key belongs to: the first catalog entry whose key matches it as the whole key, the part
    /// before ':' (e.g. low-disk:C -> low-disk), or a dotted/dashed prefix (server.av- , veeam.). "Other" when none claim it.</summary>
    public static string CategoryOf(string ruleKey)
    {
        var head = ruleKey.Split(':', 2)[0];
        foreach (var (name, keys) in Catalog)
            if (keys.Any(k => ruleKey.Equals(k, System.StringComparison.OrdinalIgnoreCase)
                           || head.Equals(k, System.StringComparison.OrdinalIgnoreCase)
                           || ruleKey.StartsWith(k, System.StringComparison.OrdinalIgnoreCase)))
                return name;
        return "Other";
    }

    /// <summary>Roll the worklist issues up into categories, worst first then most machines. A category's machine count is
    /// the DISTINCT machines across its issues (a PC with two low-disk drives counts once).</summary>
    public static IReadOnlyList<ToClearCategory> Of(IEnumerable<ToClearIssue> issues)
    {
        var order = Catalog.Select((c, i) => (c.Name, i)).ToDictionary(x => x.Name, x => x.i);
        return issues
            .GroupBy(i => CategoryOf(i.RuleKey))
            .Select(g => new ToClearCategory(
                g.Key,
                g.SelectMany(i => i.Machines.Select(m => m.Name)).Distinct(System.StringComparer.OrdinalIgnoreCase).Count(),
                g.Count(),
                g.Max(i => i.Severity),
                g.Select(i => i.RuleKey).ToList()))
            .OrderByDescending(c => c.Severity)
            .ThenBy(c => order.TryGetValue(c.Name, out var o) ? o : int.MaxValue)
            .ThenByDescending(c => c.Machines)
            .ToList();
    }
}
