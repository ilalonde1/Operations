#nullable enable
namespace Kor.Operations.NetworkOps.Core.Health;

/// <summary>A finding already open for a device, as the store holds it.</summary>
public sealed record OpenFinding(long FindingId, string RuleKey, Severity Severity, DateTime FirstSeenUtc, Severity? NotifiedSeverity);

public enum ChangeKind { New, Escalated, Unchanged, Cleared }

public sealed record FindingChange(ChangeKind Kind, string RuleKey, Finding? Current, OpenFinding? Previous);

// What changed on one device between the findings already open and the ones a fresh sweep raised.
// This decides what Ian gets emailed, so it is pure and tested: a finding is announced when it
// appears and when it gets worse, never again just because it is still there, and its clearing
// is announced once. That is what "deduplicated" means in the roadmap (§6, "Alert").
public static class FindingDiff
{
    public static IReadOnlyList<FindingChange> Compute(IReadOnlyCollection<OpenFinding> open, IReadOnlyCollection<Finding> raised)
    {
        var byKey = open.ToDictionary(o => o.RuleKey, StringComparer.OrdinalIgnoreCase);
        var changes = new List<FindingChange>();

        foreach (var f in raised)
        {
            if (!byKey.TryGetValue(f.RuleKey, out var prev))
                changes.Add(new(ChangeKind.New, f.RuleKey, f, null));
            else if (f.Severity > prev.Severity)
                changes.Add(new(ChangeKind.Escalated, f.RuleKey, f, prev));
            else
                changes.Add(new(ChangeKind.Unchanged, f.RuleKey, f, prev));
        }

        var raisedKeys = new HashSet<string>(raised.Select(r => r.RuleKey), StringComparer.OrdinalIgnoreCase);
        foreach (var o in open.Where(o => !raisedKeys.Contains(o.RuleKey)))
            changes.Add(new(ChangeKind.Cleared, o.RuleKey, null, o));

        return changes;
    }

    /// <summary>
    /// Whether a change deserves an email. Info findings are recorded but never mailed on their own
    /// (they are the command center's job); a cleared finding is mailed only if its raising was.
    /// </summary>
    public static bool IsNotifiable(FindingChange c) => c.Kind switch
    {
        ChangeKind.New or ChangeKind.Escalated => c.Current!.Severity >= Severity.Warning,
        ChangeKind.Cleared => c.Previous!.NotifiedSeverity is not null,
        _ => false,
    };
}
