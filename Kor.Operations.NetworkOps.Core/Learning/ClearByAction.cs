#nullable enable
using System.Linq;
using Kor.Operations.NetworkOps.Core.Health;

namespace Kor.Operations.NetworkOps.Core.Learning;

// The worklist grouped not by PROBLEM but by the ACTION that clears it -- because that is what you actually DO. One restart
// clears EVERY reboot reason on a machine, so "13 not-restarted + 9 reboot-overdue + 6 crash-loop" is not 28 restarts: it is
// the UNION of the machines, and a machine carried by several of those reasons is cleared by ONE restart. The per-issue list
// hides that overlap (Ian, 2026-10-07: "how many of those 6 apply to all 3 instances?").
//
// Each ToClearIssue carries one fix (ToClear.Build picks it from FixCatalog), so issues whose fix has the same id gather
// into one action. This computes, per action: the DISTINCT machines it would run on (the union), how many findings that
// clears, how many of those machines carry 2+ of the reasons (the saving), and the reasons themselves. Issues with no
// catalog fix (Ask Claude / hands-on) have no single action and are left to the per-issue list.

/// <summary>One action that clears a set of issues: the fix, the DISTINCT machines it runs on (the union across every reason
/// it covers), the findings it clears, how many of those machines have 2+ of the reasons, and the reasons (worst first).</summary>
public sealed record ClearAction(FixOption Fix, int Machines, int Findings, int OverlapMachines, Severity Severity, IReadOnlyList<ToClearIssue> Reasons)
{
    /// <summary>Clears with a single click, on every machine at once: no input needed, nothing restarts.</summary>
    public bool OneClick => !Fix.Disruptive && Fix.ParamLabel is null;
    /// <summary>More than one reason, so grouping by action actually consolidates work (vs a single-reason fix).</summary>
    public bool Consolidates => Reasons.Count > 1;
}

public static class ClearByAction
{
    /// <summary>Group the open issues by the fix that clears them, most findings cleared first. Machines are de-duplicated
    /// across the reasons: a PC with three reboot findings counts once in Machines, and once toward OverlapMachines.</summary>
    public static IReadOnlyList<ClearAction> Of(IEnumerable<ToClearIssue> issues)
        => issues
            .Where(i => i.Fix is not null)
            .GroupBy(i => i.Fix!.Id, System.StringComparer.Ordinal)
            .Select(g =>
            {
                var reasons = g.OrderByDescending(i => i.Severity).ThenByDescending(i => i.Count).ThenBy(i => i.RuleKey, System.StringComparer.Ordinal).ToList();
                // Every machine this action would touch, counted once however many reasons put it here.
                var perMachine = reasons
                    .SelectMany(i => i.Machines.Select(m => m.DeviceId).Where(x => x > 0))
                    .GroupBy(x => x)
                    .ToDictionary(x => x.Key, x => x.Count());
                return new ClearAction(
                    reasons[0].Fix!,
                    perMachine.Count,                              // union of distinct machines
                    reasons.Sum(i => i.Count),                     // findings cleared (placements)
                    perMachine.Count(kv => kv.Value >= 2),         // machines carrying 2+ of the reasons
                    reasons.Max(i => i.Severity),
                    reasons);
            })
            .OrderByDescending(a => a.Severity).ThenByDescending(a => a.Findings).ThenByDescending(a => a.Machines)
            .ToList();
}
