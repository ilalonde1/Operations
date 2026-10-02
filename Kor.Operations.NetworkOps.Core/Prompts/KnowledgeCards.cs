#nullable enable
using System.Text.RegularExpressions;
using Kor.Operations.NetworkOps.Core.Learning;

namespace Kor.Operations.NetworkOps.Core.Prompts;

// Which banked knowledge a prompt carries. A card says which machines it concerns in a small, closed vocabulary, so
// "what Andrea's ETABS crash taught us" reaches every later prompt about a PC with ETABS on it -- and nothing else.
//
//   any                 every machine
//   app:<text>          an installed app whose name contains <text>     app:etabs
//   model:<text>        the hardware model contains <text>              model:P340
//   gpu:<text>          the graphics card contains <text>               gpu:Quadro P1000
//   kind:<kind>         Workstation, Server, ...                        kind:Server
//   device:<name>       that one machine                                device:KOR-217
//   finding:<rule>      an open finding of that kind                    finding:crash-loop
//
// Terms are comma-separated; a card applies when ANY of them matches. Matching ignores case.
public static class KnowledgeCards
{
    public static readonly IReadOnlyList<string> Kinds = ["any", "app", "model", "gpu", "kind", "device", "finding"];

    /// <summary>Why an AppliesTo is refused, or null. Checked when a session proposes a card, so a typo cannot bank a card no prompt ever carries.</summary>
    public static string? Invalid(string? appliesTo)
    {
        var terms = Terms(appliesTo);
        if (terms.Count == 0) return "appliesTo is empty: say which machines it concerns (any, app:<name>, model:<text>, gpu:<text>, kind:<kind>, device:<name>, finding:<rule>)";
        foreach (var (kind, value) in terms)
        {
            if (!Kinds.Contains(kind)) return $"appliesTo term '{kind}' is not one of: {string.Join(", ", Kinds)}";
            if (kind != "any" && value.Length == 0) return $"appliesTo term '{kind}:' needs a value";
        }
        return null;
    }

    /// <summary>Whether a card concerns this machine.</summary>
    public static bool AppliesTo(string appliesTo, string device, string deviceKind, IReadOnlyDictionary<string, string> facts, IEnumerable<string> openRuleKeys)
    {
        var families = openRuleKeys.Select(FixLearning.FamilyOf).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Terms(appliesTo).Any(t => t.Kind switch
        {
            "any" => true,
            "app" => facts.Keys.Any(k => k.StartsWith(Facts.AppPrefix, StringComparison.Ordinal) && Has(k[Facts.AppPrefix.Length..], t.Value)),
            "model" => facts.TryGetValue(Facts.Model, out var m) && Has(m, t.Value),
            "gpu" => facts.TryGetValue(Facts.GpuName, out var g) && Has(g, t.Value),
            "kind" => deviceKind.Equals(t.Value, StringComparison.OrdinalIgnoreCase),
            "device" => device.Equals(t.Value, StringComparison.OrdinalIgnoreCase),
            "finding" => families.Contains(FixLearning.FamilyOf(t.Value)),
            _ => false,
        });
    }

    internal static IReadOnlyList<(string Kind, string Value)> Terms(string? appliesTo)
        => (appliesTo ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.IndexOf(':') is var i and > 0 ? (t[..i].Trim().ToLowerInvariant(), t[(i + 1)..].Trim()) : (t.ToLowerInvariant(), ""))
            .ToList();

    // App fact keys are lower-case and dotted (app.revit.2025); a card says "ETABS" or "revit 2025".
    private static bool Has(string haystack, string needle)
        => Norm(haystack).Contains(Norm(needle), StringComparison.Ordinal);

    private static string Norm(string s) => Regex.Replace(s.ToLowerInvariant(), @"[\s._\-]+", " ").Trim();
}
