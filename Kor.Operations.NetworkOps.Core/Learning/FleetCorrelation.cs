#nullable enable
namespace Kor.Operations.NetworkOps.Core.Learning;

/// <summary>One PC as the fleet comparison sees it: its correlatable facts and the problem families it has open.</summary>
public sealed record FleetMember(string Device, IReadOnlyDictionary<string, string> Facts, IReadOnlySet<string> OpenFamilies);

/// <param name="AffectedWith">PCs with this fact value that have the problem.</param>
/// <param name="TotalWith">PCs with this fact value.</param>
/// <param name="PValue">Fisher's exact test, one-sided: how likely a split this lopsided is by chance.</param>
public sealed record FleetInsight(string Family, string Fact, string Value,
    int AffectedWith, int TotalWith, int AffectedWithout, int TotalWithout, double PValue, IReadOnlyList<string> Devices)
{
    /// <summary>How much more common the problem is with this value than without it, as a difference in rates.</summary>
    public double Strength => (double)AffectedWith / TotalWith - (TotalWithout == 0 ? 0 : (double)AffectedWithout / TotalWithout);

    public string Summary =>
        $"{Family}: {AffectedWith} of {TotalWith} PCs with {Fact} = {Value} have it, against {AffectedWithout} of {TotalWithout} without";

    public string Key => $"{Family}|{Fact}|{Value}";
}

// "Knows about other PCs": for each problem family, which fact the affected machines share far
// more than the rest do. This is the GPU analysis of 2026-09-28 made automatic -- driver
// 32.0.15.8142 hung on 4 of 4 PCs while GeForce cards hung on 0 of 5 -- and it runs every sweep.
//
// Fisher's exact test rather than a chi-square: the fleet is ~30 machines and the cells are small,
// which is exactly where approximations invent patterns. Guards against the classic traps:
//   * at least 2 affected PCs share the value (one machine proves nothing about a value);
//   * the value is on at least 2 PCs (a value unique to one PC "explains" that PC trivially);
//   * the problem is not everywhere (a family on 90% of the fleet has no distinguishing value);
//   * only correlatable facts (serials, dates, OS build numbers would explain everything).
public static class FleetCorrelation
{
    public const double MaxPValue = 0.05;
    public const double MinRateWith = 0.6;
    public const double MinStrength = 0.4;
    public const int MaxPerFamily = 3;

    public static IReadOnlyList<FleetInsight> Find(IReadOnlyList<FleetMember> fleet)
    {
        var result = new List<FleetInsight>();
        var n = fleet.Count;
        if (n < 4) return result;
        var families = fleet.SelectMany(m => m.OpenFamilies).Distinct(StringComparer.Ordinal);

        foreach (var family in families)
        {
            var affected = fleet.Where(m => m.OpenFamilies.Contains(family)).ToList();
            if (affected.Count < 2 || affected.Count > n * 0.9) continue;

            var candidates = new List<FleetInsight>();
            var byFactValue = fleet
                .SelectMany(m => m.Facts.Where(kv => Facts.IsCorrelatable(kv.Key)).Select(kv => (kv.Key, kv.Value, Member: m)))
                .GroupBy(x => (x.Key, x.Value));
            foreach (var g in byFactValue)
            {
                var with = g.Select(x => x.Member).ToList();
                var totalWith = with.Count;
                if (totalWith < 2 || totalWith == n) continue;
                var affWith = with.Where(m => m.OpenFamilies.Contains(family)).ToList();
                if (affWith.Count < 2) continue;

                // "Without" = PCs that REPORT this fact with a different value. A PC that does not report
                // the fact at all (no discrete GPU, WMI broken) is neither evidence for nor against.
                var reporting = fleet.Where(m => m.Facts.ContainsKey(g.Key.Key)).ToList();
                var totalWithout = reporting.Count - totalWith;
                var affWithout = reporting.Count(m => m.OpenFamilies.Contains(family)) - affWith.Count;

                var rateWith = (double)affWith.Count / totalWith;
                var p = FisherOneSided(affWith.Count, totalWith - affWith.Count, affWithout, totalWithout - affWithout);
                var ins = new FleetInsight(family, g.Key.Key, g.Key.Value, affWith.Count, totalWith, affWithout, totalWithout, p,
                    affWith.Select(m => m.Device).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList());
                if (rateWith >= MinRateWith && ins.Strength >= MinStrength && p <= MaxPValue) candidates.Add(ins);
            }
            result.AddRange(candidates.OrderBy(c => c.PValue).ThenByDescending(c => c.Strength).Take(MaxPerFamily));
        }
        return result;
    }

    /// <summary>
    /// One-sided Fisher exact p-value for enrichment in the top-left cell of
    /// [[a, b], [c, d]] = [[affected-with, unaffected-with], [affected-without, unaffected-without]]:
    /// the probability of a table at least this lopsided given the margins.
    /// </summary>
    internal static double FisherOneSided(int a, int b, int c, int d)
    {
        var r1 = a + b; var c1 = a + c; var n = a + b + c + d;
        var maxA = Math.Min(r1, c1);
        var p = 0.0;
        for (var x = a; x <= maxA; x++)
        {
            var y = r1 - x; var z = c1 - x; var w = n - r1 - z;
            if (y < 0 || z < 0 || w < 0) continue;
            p += Math.Exp(LogChoose(r1, x) + LogChoose(n - r1, z) - LogChoose(n, c1));
        }
        return Math.Min(1.0, p);
    }

    private static double LogChoose(int n, int k) => LogFactorial(n) - LogFactorial(k) - LogFactorial(n - k);

    private static double LogFactorial(int n)
    {
        var s = 0.0;
        for (var i = 2; i <= n; i++) s += Math.Log(i);
        return s;
    }
}
