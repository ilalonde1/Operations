#nullable enable
namespace Kor.Operations.NetworkOps.Core.Learning;

/// <summary>What changed on a PC between a finding's last open sighting and the sweep that saw it gone.</summary>
public sealed record Resolution(string RuleKey, DateTime ClearedUtc, bool Rebooted, IReadOnlyList<FactChange> ChangedFacts, IReadOnlyList<string> Actions)
{
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            parts.AddRange(ChangedFacts.Select(Describe));
            parts.AddRange(Actions.Select(a => $"action: {a}"));
            if (Rebooted) parts.Add("a restart");
            return parts.Count == 0 ? "cleared with no recorded change" : "cleared after " + string.Join(", ", parts);
        }
    }

    internal static string Describe(FactChange c) => (c.OldValue, c.NewValue) switch
    {
        (null, { } nv) => $"{c.Fact} added ({nv})",
        ({ } ov, null) => $"{c.Fact} removed (was {ov})",
        var (ov, nv) => $"{c.Fact} {ov} → {nv}",
    };
}

/// <summary>A candidate fix and how often it coincided with a problem going away.</summary>
public sealed record LearnedFix(string Family, string Change, int Times, int OfResolutions)
{
    public string Summary => $"{Change}: cleared it {Times} of {OfResolutions} times";
}

// "Learns what fixes things." Every clearing is a small experiment: what changed on that PC just
// before the problem went away. One coincidence proves nothing; the same change clearing the same
// problem on several PCs is evidence. Changes are generalised to their KIND (the GPU driver changed,
// an app was removed, a restart) so different machines' clearings can agree.
public static class FixLearning
{
    /// <summary>The finding family: "crash-loop:revit.exe" → "crash-loop"; family is what fixes generalise over.</summary>
    public static string FamilyOf(string ruleKey) { var i = ruleKey.IndexOf(':'); return i < 0 ? ruleKey : ruleKey[..i]; }

    /// <summary>The kinds of change in one resolution, e.g. "gpu.driver changed", "app.bluebeam removed", "restart".</summary>
    public static IReadOnlyList<string> ChangeKinds(Resolution r)
    {
        var kinds = new List<string>();
        foreach (var c in r.ChangedFacts)
            kinds.Add(c switch
            {
                { OldValue: null } => $"{c.Fact} added",
                { NewValue: null } => $"{c.Fact} removed",
                _ => $"{c.Fact} changed",
            });
        kinds.AddRange(r.Actions.Select(a => $"action: {a}"));
        if (r.Rebooted) kinds.Add("restart");
        return kinds.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Ranks changes by how often they accompanied a family's resolutions. A change needs to have
    /// coincided at least twice to be offered: once is an anecdote. "restart" is kept but ranked
    /// under specific changes when tied, because nearly every clearing involves one.
    /// </summary>
    public static IReadOnlyList<LearnedFix> Rank(IEnumerable<Resolution> resolutions)
    {
        return resolutions.GroupBy(r => FamilyOf(r.RuleKey))
            .SelectMany(g =>
            {
                var total = g.Count();
                return g.SelectMany(ChangeKinds)
                    .GroupBy(k => k)
                    .Where(k => k.Count() >= 2)
                    .Select(k => new LearnedFix(g.Key, k.Key, k.Count(), total));
            })
            .OrderBy(x => x.Family, StringComparer.Ordinal)
            .ThenByDescending(x => x.Times)
            .ThenBy(x => x.Change == "restart" ? 1 : 0)
            .ToList();
    }
}
