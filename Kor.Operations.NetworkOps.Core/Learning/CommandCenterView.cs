#nullable enable
using Kor.Operations.NetworkOps.Core.Health;

namespace Kor.Operations.NetworkOps.Core.Learning;

/// <summary>An open finding as the Command Center reads it back from the store.</summary>
public sealed record FleetFinding(long FindingId, string Device, string RuleKey, Severity Severity, string Title, string Evidence,
    DateTime FirstSeenUtc, DateTime LastSeenUtc, DateTime? AcknowledgedUtc, string? AcknowledgedBy, DateTime? SnoozedUntilUtc, string? AckNote)
{
    /// <summary>Acknowledged, or snoozed until a time still in the future: known, and not counted against the PC.</summary>
    public bool IsQuiet(DateTime nowUtc) => AcknowledgedUtc is not null || SnoozedUntilUtc > nowUtc;

    public OpenFindingView AsView() => new(RuleKey, Severity, AcknowledgedUtc, SnoozedUntilUtc);
}

/// <summary>A fleet pattern the service has found and still sees ("crash-loop:opushutil.exe" goes with access.engine.2016).</summary>
public sealed record ActivePattern(string Problem, string Fact, string Value, int AffectedWith, int TotalWith, int AffectedWithout, int TotalWithout,
    string Summary, DateTime FirstSeenUtc);

public enum Freshness { NeverChecked, Current, Stale }

/// <summary>One change to what a PC is: "gpu.driver 32.0.15.8142 → 32.0.15.8180 on 30 Sep".</summary>
public sealed record FactChangeAt(DateTime AtUtc, string Fact, string? OldValue, string? NewValue)
{
    public string Description => (OldValue, NewValue) switch
    {
        (null, { } nv) => $"{Fact} added ({nv})",
        ({ } ov, null) => $"{Fact} removed (was {ov})",
        var (ov, nv) => $"{Fact} {ov} → {nv}",
    };
}

// The questions the Command Center answers about one PC, as pure functions over what the store
// holds, so each can be tested against a fixture and the page only draws:
//   - is the same problem on other PCs right now?
//   - is this PC part of a fleet pattern that explains the problem?
//   - how old is what we know about it?
// "Same problem" is FleetCorrelation.ProblemOf, the key the service correlates on, so the page and
// the pattern finder can never disagree about what counts as the same fault.
public static class CommandCenterView
{
    /// <summary>
    /// Older than this and the page says so. Sweeps run hourly on weekdays, so a Friday-evening
    /// check is still the latest one on Monday morning; three days covers a long weekend.
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(3);

    /// <summary>The other PCs where the same problem is open now, by name.</summary>
    public static IReadOnlyList<string> SameProblemElsewhere(FleetFinding finding, IEnumerable<FleetFinding> fleetOpen)
    {
        var problem = FleetCorrelation.ProblemOf(finding.RuleKey);
        return fleetOpen
            .Where(f => !f.Device.Equals(finding.Device, StringComparison.OrdinalIgnoreCase) && FleetCorrelation.ProblemOf(f.RuleKey) == problem)
            .Select(f => f.Device)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The active fleet patterns that explain this finding on this PC: same problem, and the PC has the fact.</summary>
    public static IReadOnlyList<ActivePattern> PatternsFor(string ruleKey, IReadOnlyDictionary<string, string> deviceFacts, IEnumerable<ActivePattern> patterns)
    {
        var problem = FleetCorrelation.ProblemOf(ruleKey);
        return patterns
            .Where(p => p.Problem == problem && deviceFacts.TryGetValue(p.Fact, out var v) && v == p.Value)
            .OrderByDescending(p => p.AffectedWith)
            .ToList();
    }

    /// <summary>The PCs a pattern is about: they have the fact, and the problem is open on them now.</summary>
    public static IReadOnlyList<string> MembersOf(ActivePattern pattern, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> factsByDevice, IEnumerable<FleetFinding> fleetOpen)
    {
        var withProblem = fleetOpen.Where(f => FleetCorrelation.ProblemOf(f.RuleKey) == pattern.Problem)
            .Select(f => f.Device).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return factsByDevice
            .Where(kv => withProblem.Contains(kv.Key) && kv.Value.TryGetValue(pattern.Fact, out var v) && v == pattern.Value)
            .Select(kv => kv.Key)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static Freshness FreshnessOf(DateTime? lastCheckedUtc, DateTime nowUtc)
        => lastCheckedUtc is null ? Freshness.NeverChecked
         : nowUtc - lastCheckedUtc.Value > StaleAfter ? Freshness.Stale
         : Freshness.Current;

    /// <summary>"3 min ago", "5 h ago", "2 d ago" -- how long, never a timestamp to decode.</summary>
    public static string Ago(DateTime? thenUtc, DateTime nowUtc)
    {
        if (thenUtc is null) return "never";
        var d = nowUtc - thenUtc.Value;
        if (d < TimeSpan.FromMinutes(1)) return "just now";
        if (d < TimeSpan.FromHours(1)) return $"{(int)d.TotalMinutes} min ago";
        if (d < TimeSpan.FromDays(2)) return $"{(int)d.TotalHours} h ago";
        return $"{(int)d.TotalDays} d ago";
    }

    /// <summary>
    /// A PC's changes, newest first, from its fact history (every value each fact has held, and when
    /// it stopped holding it). The first inventory is the baseline, not a change -- otherwise the first
    /// sweep would report every PC as having changed everything. After it, a fact appearing is an
    /// addition (an app installed), a value replaced is a change, and a value superseded with nothing
    /// after it is a removal (an app uninstalled).
    /// </summary>
    public static IReadOnlyList<FactChangeAt> ChangesFrom(IReadOnlyCollection<(string Fact, string Value, DateTime FirstSeenUtc, DateTime? SupersededUtc)> history)
    {
        if (history.Count == 0) return [];
        var baseline = history.Min(h => h.FirstSeenUtc);
        var changes = new List<FactChangeAt>();
        foreach (var g in history.GroupBy(h => h.Fact, StringComparer.Ordinal))
        {
            var ordered = g.OrderBy(h => h.FirstSeenUtc).ToList();
            if (ordered[0].FirstSeenUtc > baseline) changes.Add(new FactChangeAt(ordered[0].FirstSeenUtc, g.Key, null, ordered[0].Value));
            for (var i = 1; i < ordered.Count; i++) changes.Add(new FactChangeAt(ordered[i].FirstSeenUtc, g.Key, ordered[i - 1].Value, ordered[i].Value));
            if (ordered[^1].SupersededUtc is { } gone) changes.Add(new FactChangeAt(gone, g.Key, ordered[^1].Value, null));
        }
        return changes.OrderByDescending(c => c.AtUtc).ThenBy(c => c.Fact, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The learned fixes for one finding's family, rebuilt from stored resolutions. Only changes seen
    /// at least twice are offered (FixLearning.Rank): once is an anecdote.
    /// </summary>
    public static IReadOnlyList<LearnedFix> LearnedFixesFor(string ruleKey, IEnumerable<Resolution> fleetResolutions)
    {
        var family = FixLearning.FamilyOf(ruleKey);
        return FixLearning.Rank(fleetResolutions.Where(r => FixLearning.FamilyOf(r.RuleKey) == family));
    }
}
