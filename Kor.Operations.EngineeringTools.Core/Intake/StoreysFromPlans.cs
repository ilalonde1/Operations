#nullable enable
using System.Globalization;
using Kor.Operations.EngineeringTools.Dxf;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// A SET'S STOREYS ARE WHAT ITS PLANS NAME; the elevations give their heights when the set has
/// them, and the heights it does not state are assumed and said (intake step 45, 2026-09-11).
///
/// The storey ladder had one source: the shear-wall elevation sheets (<see cref="SetStoreys"/>,
/// steps 25 and 41). The corpus analyzer's first run over every structural stick file on the share
/// put the cost of that in one number: 225 of 292 sets read their plans (2,462 plan views) and built
/// no model, because "0 of 0 elevation sheets stated storeys" — wood, steel and small concrete sets
/// have no shear-wall elevations at all. Every one of them names its storeys on its plans: LEVEL 2
/// PLAN, LEVEL P1 PLAN, ROOF PLAN. The names and their order are certain; only the heights are not.
/// </summary>
/// <remarks>
/// THE RULE: the storey list is the union of what the elevations chained and what the written plan
/// views are named for (parsed by the same <see cref="PlanSheetNaming"/> the composer places sheets
/// with, so the names agree by construction): parkade levels below, numbered levels up, the roof on
/// top. Walking up from the lowest at 0, a level the elevations stated keeps its stated elevation;
/// a level they did not is set the typical storey height above the one below — the set's own typical
/// where the elevations gave one, else the assumed height — and is listed as assumed, in the levels
/// file and in the report. A foundation plan names no storey (it is the base every storey stands
/// on); a mezzanine plan is left to the elevations (not banked here).
/// WHAT THIS DOES NOT DO: read heights off sections or the architect's set (a later step; the
/// elevations remain the only stated source); order a storey the plans name by a word alone with no
/// number and no roof word ("GROUND", "MAIN") — those need the vocabulary (the corpus's plan titles
/// list them; step 46).
/// </remarks>
public static class StoreysFromPlans
{
    /// <summary>A storey in the merged ladder: where it came from says whether its elevation was stated or assumed.</summary>
    public sealed record Storey(string Name, double ElevationMm, string From, bool Assumed);

    public sealed record Ladder(IReadOnlyList<Storey> Storeys, int FromElevations, int FromPlansOnly, int Assumed, double HeightUsedMm, string HeightSource)
    {
        public bool IsEmpty => Storeys.Count == 0;
    }

    /// <summary>The compiled default for a storey whose height nothing states, in millimetres (KorStandards row dxf.pdf.assumed-storey-height-mm).</summary>
    public const double DefaultAssumedStoreyHeightMm = 3000.0;

    /// <summary>
    /// The merged ladder. <paramref name="chain"/> may be null or empty (no elevations read);
    /// <paramref name="planFileNames"/> are the written views' file names.
    /// </summary>
    public static Ladder Merge(SetStoreys.Chain? chain, IEnumerable<string> planFileNames, double assumedHeightMm = DefaultAssumedStoreyHeightMm)
    {
        ArgumentNullException.ThrowIfNull(planFileNames);
        var stated = new Dictionary<string, SetStoreys.Level>(StringComparer.OrdinalIgnoreCase);
        if (chain is not null)
            foreach (var l in chain.Levels)
                stated.TryAdd(l.Name, l);

        // what the plans name: parkade P{n}, numbered L{n}, the roof
        var parkade = new SortedSet<int>();
        var numbered = new SortedSet<int>();
        bool roof = false;
        foreach (var file in planFileNames)
        {
            var sheet = PlanSheetNaming.Parse(file);
            foreach (int p in sheet.ParkadeLevels) parkade.Add(p);
            foreach (int n in sheet.Levels) numbered.Add(n);
            if (sheet.IsRoof && sheet.Levels.Count == 0 && sheet.ParkadeLevels.Count == 0) roof = true;
        }

        // WHAT THE ELEVATIONS ALREADY COVER. A ladder names a storey its own way — "A-L27" for a tower's
        // 27th floor the plan calls LEVEL 27, "L0/P1" for one level two sheets name differently — and a
        // plan-named storey the ladder covers under another spelling is that storey, not a second one
        // (the first cut put L27 beside A-L27 and B-L27 on 31168 and lost 50 columns, and L0 beside L0/P1
        // on 31130, 2026-09-11). Covered: the stripped name (building prefix off) of any part of any
        // ladder name split on "/".
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (chain is not null)
            foreach (var l in chain.Levels)
                foreach (var part in l.Name.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    covered.Add(Stripped(part));

        // AND THE ROOF PLAN NAMES A STOREY ONLY WHERE THE ELEVATIONS PUT NONE ABOVE THE PLANS. A ladder that
        // reaches above the highest numbered plan already has the roof plan's storey (step 36: the roof plan
        // draws the storey above the highest numbered plan — 31170's L7, where L8 is the elevator overrun);
        // a ROOF added over that would carry the roof plan two storeys up.
        int highestPlan = numbered.Count == 0 ? int.MinValue : numbered.Max;
        bool ladderReachesAboveThePlans = chain is not null
            && (chain.Levels.Any(l => NumberOf(l.Name) is int n && n > highestPlan)
                || chain.Levels.Any(l => Stripped(l.Name).Contains("ROOF", StringComparison.OrdinalIgnoreCase)));

        // THE ORDER: the elevations' levels first, in their own elevation order - that order is stated
        // fact - and each plan-only storey slotted in by its RANK among them: a parkade level below the
        // numbered ones, deeper first; a numbered level after the last level numbered below it; the roof
        // on top. A ladder name is ranked under any spelling ("L0/P1" is level 0, "A-L27" is 27, "L1M"
        // is a mezzanine over 1). The first cut ordered the plan names first and inserted the ladder's
        // among them by elevation, and 31130 came out P3 P2 L2 ... L20 L0/P1 L1 (2026-09-11).
        var order = chain is null ? new List<string>() : chain.Levels.OrderBy(l => l.ElevationMm).Select(l => l.Name).ToList();
        var toAdd = new List<string>();
        foreach (int p in parkade.Reverse()) if (!covered.Contains($"P{p}")) toAdd.Add($"P{p}");
        foreach (int n in numbered) if (!covered.Contains($"L{n}")) toAdd.Add($"L{n}");
        if (roof && !covered.Contains("ROOF") && !ladderReachesAboveThePlans) toAdd.Add("ROOF");
        foreach (var name in toAdd)
        {
            var rank = RankOf(name)!.Value;
            int at = 0;
            for (int i = 0; i < order.Count; i++)
                if (RankOf(order[i]) is { } r && r.CompareTo(rank) < 0) at = i + 1;
            order.Insert(at, name);
        }
        if (order.Count == 0) return new Ladder([], 0, 0, 0, assumedHeightMm, "none");

        double height = chain?.TypicalMm is double t && t > 0 ? t : assumedHeightMm;
        string heightSource = chain?.TypicalMm is double t2 && t2 > 0 ? $"the set's typical storey, {t2:0} mm" : $"the assumed storey height, {assumedHeightMm:0} mm";

        var storeys = new List<Storey>();
        double elevation = 0;
        int fromElev = 0, fromPlans = 0, assumed = 0;
        int lastStated = -1;                                           // index of the last storey whose elevation the drawings stated
        for (int i = 0; i < order.Count; i++)
        {
            string name = order[i];
            if (stated.TryGetValue(name, out var l))
            {
                // A STATED ELEVATION STANDS. The assumed storeys walked up since the last stated one are then
                // re-spaced evenly between the two stated levels, so an assumption never moves a fact.
                elevation = l.ElevationMm;
                int between = i - lastStated - 1;
                if (between > 0 && lastStated >= 0)
                {
                    double from = storeys[lastStated].ElevationMm, step = (elevation - from) / (between + 1);
                    for (int k = 1; k <= between; k++)
                        storeys[lastStated + k] = storeys[lastStated + k] with { ElevationMm = from + step * k, From = $"a plan names it; spaced evenly between {order[lastStated]} and {name} ({step:0} mm a storey)" };
                }
                storeys.Add(new Storey(name, elevation, l.From, Assumed: false));
                fromElev++;
                lastStated = i;
            }
            else
            {
                if (i > 0) elevation += height;
                storeys.Add(new Storey(name, elevation, i == 0 ? "the lowest plan, at 0" : $"a plan names it; {height:0} mm over {order[i - 1]} ({heightSource})", Assumed: i > 0));
                fromPlans++;
                if (i > 0) assumed++;
            }
        }
        // the lowest storey is the datum: everything is relative to it
        double datum = storeys[0].ElevationMm;
        if (datum != 0) storeys = storeys.Select(s => s with { ElevationMm = s.ElevationMm - datum }).ToList();
        return new Ladder(storeys, fromElev, fromPlans, assumed, height, heightSource);
    }

    /// <summary>"A-LEVEL 27", "A-L27", "LEVEL 27" and "L27" are one name here; "P1" and "LEVEL P1" too (the yardstick's rule, ModelYardstick.Stripped).</summary>
    internal static string Stripped(string storey) => ModelYardstick.Stripped(storey);

    /// <summary>The number of a numbered level under any spelling ("A-LEVEL 27" → 27), or null.</summary>
    private static int? NumberOf(string storey)
    {
        string s = Stripped(storey);
        return s.Length > 1 && s[0] == 'L' && int.TryParse(s[1..], NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : null;
    }

    /// <summary>
    /// A storey's rank under any spelling: parkade levels first (deeper first), then numbered levels (a
    /// mezzanine just over its level), then the roof; null for a name this cannot rank (PENTHOUSE, an
    /// elevator roof), which keeps the place its elevation gave it.
    /// </summary>
    internal static (int Kind, double N)? RankOf(string storey)
    {
        string s = Stripped(storey.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? storey);
        if (s.Contains("ROOF", StringComparison.OrdinalIgnoreCase)) return (2, 0);
        if (s.Length > 1 && s[0] == 'P' && int.TryParse(s[1..], NumberStyles.None, CultureInfo.InvariantCulture, out int p)) return (0, -p);
        if (s.Length > 1 && s[0] == 'L')
        {
            string rest = s[1..];
            bool mezz = rest.EndsWith('M');
            if (mezz) rest = rest[..^1];
            if (int.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out int n)) return (1, n + (mezz ? 0.5 : 0));
        }
        return null;
    }

    /// <summary>The levels file dxf-to-etabs reads, with every assumption written in it.</summary>
    public static IReadOnlyList<string> LevelsFileLines(Ladder ladder)
    {
        var lines = new List<string>
        {
            "# unit: mm",
            "# level,elevation mm — the storeys the set's plans name and its elevations state; the lowest is 0",
        };
        if (ladder.Assumed > 0)
            lines.Add($"# ASSUMED: {ladder.Assumed} storey height(s) not stated by the drawings, taken as {ladder.HeightSource}: " +
                      string.Join(", ", ladder.Storeys.Where(s => s.Assumed).Select(s => s.Name)));
        foreach (var s in ladder.Storeys) lines.Add($"{s.Name},{s.ElevationMm.ToString("0", CultureInfo.InvariantCulture)}");
        return lines;
    }

    /// <summary>One line for the report.</summary>
    public static string Summary(Ladder ladder) => ladder.IsEmpty
        ? "no storeys: the elevations chained none and no plan names one"
        : $"{ladder.Storeys.Count} storeys ({string.Join(" ", ladder.Storeys.Select(s => s.Name))}): {ladder.FromElevations} with a stated elevation, {ladder.FromPlansOnly} named by plans only" +
          (ladder.Assumed > 0 ? $", {ladder.Assumed} height(s) ASSUMED at {ladder.HeightSource}" : "");
}
