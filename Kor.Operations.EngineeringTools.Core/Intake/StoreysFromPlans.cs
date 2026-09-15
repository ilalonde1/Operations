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
        bool roof = false, topFloor = false;
        // the set's own order of its floor words (step 60): GROUND, MAIN, UPPER as its framing-over clauses chain them
        var names = planFileNames.ToList();
        var vocabulary = PlanSheetNaming.Vocabulary.WithFloorWordsRankedBy(names.Select(PlanSheetNaming.TitleOf));
        // A BUILDING'S ROOF PLAN NAMES THAT BUILDING'S ROOF (step 61, 2026-09-13): "ROOF PLAN CONCRETE OUTLINE BLDG C"
        // on 31168 is the storey above C's highest plan (C-L9), not a storey above the whole set's (B-L40). The
        // global rule below put ROOF at the top of tower B the moment a missed L40 view was read (before it, the
        // chain reached above the plans and no ROOF was added, which hid the class). A tagged roof plan names
        // <TAG>-ROOF after that building's highest level; only an untagged roof plan names the set's ROOF.
        var roofOfBuilding = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var elevatorRoofOfBuilding = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);   // above the building's roof (the second audit's B6)
        var highestOfBuilding = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in names)
        {
            var sheet = PlanSheetNaming.Parse(file, vocabulary);
            foreach (int p in sheet.ParkadeLevels) parkade.Add(p);
            foreach (int n in sheet.Levels) numbered.Add(n);
            foreach (string tag in sheet.BuildingTags)
                if (sheet.Levels.Count > 0 && !sheet.IsRoof) highestOfBuilding[tag] = Math.Max(highestOfBuilding.GetValueOrDefault(tag, int.MinValue), sheet.Levels.Max());
            if (sheet.IsRoof && sheet.Levels.Count == 0 && sheet.ParkadeLevels.Count == 0)
            {
                if (sheet.BuildingTags.Count == 0) roof = true;
                else foreach (string tag in sheet.BuildingTags) (sheet.IsElevatorRoof ? elevatorRoofOfBuilding : roofOfBuilding).Add(tag);
            }
            if (sheet.IsTopFloor && sheet.Levels.Count == 0 && sheet.ParkadeLevels.Count == 0) topFloor = true;
        }
        // A LOFT IS THE STOREY ABOVE THE HIGHEST NUMBERED PLAN (step 47): "2ND FLOOR PLAN SHOWING LOFT FRAMING
        // OVER" then "LOFT PLAN SHOWING ROOF FRAMING OVER" - the loft is level 3 in that set and level 2 in a
        // set whose main floor plan shows the loft over. Named as the number it ranks at, so the composer's
        // storey names stay "L3"; its file name says LOFT.
        if (topFloor) numbered.Add(numbered.Count == 0 ? 2 : numbered.Max + 1);

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
        // each tagged roof after its building's highest level, unless the chain already reaches above that building's plans
        // an elevator roof above the building's roof (B6: two storeys, not one)
        foreach (var (tags, suffix) in new[] { (roofOfBuilding, "ROOF"), (elevatorRoofOfBuilding, "ELEVATOR ROOF") })
            foreach (string tag in tags)
            {
                if (!highestOfBuilding.TryGetValue(tag, out int highest)) continue;
                string roofName = $"{tag}-{suffix}";
                bool chainAbove = chain is not null && chain.Levels.Any(l => string.Equals(ModelYardstick.Building(l.Name), tag, StringComparison.OrdinalIgnoreCase)
                    && ((NumberOf(l.Name) is int n && n > highest) || Stripped(l.Name).Contains(suffix, StringComparison.OrdinalIgnoreCase)));
                if (chainAbove || covered.Contains(roofName) || order.Contains(roofName, StringComparer.OrdinalIgnoreCase)) continue;
                int at = -1;
                for (int i = 0; i < order.Count; i++)
                    if (string.Equals(ModelYardstick.Building(order[i]), tag, StringComparison.OrdinalIgnoreCase) || (ModelYardstick.Building(order[i]) is null && NumberOf(order[i]) == highest)) at = i;
                order.Insert(at < 0 ? order.Count : at + 1, roofName);
            }
        // ONE PLAN NAMING NO STOREY IS A ONE-STOREY BUILDING (step 66, 2026-09-14): the small jobs - a
        // tenant improvement, a garage, a sales centre - draw the whole structure on one plan titled
        // PLAN, PLANS, PLAN AND DETAILS, GENERAL NOTES AND PLAN, and nine of run 11's 21 "no storeys" sets
        // are exactly that. The plan is the building's storey, L1. Two or more unnamed plans stay
        // unnamed: a foundation plan and a framing plan of one storey are not two storeys. And a
        // FOUNDATION plan alone names no storey (the standing rule below it): footings are not a floor.
        if (order.Count == 0 && names.Count == 1 && !PlanSheetNaming.Parse(names[0], vocabulary).IsFoundation) order.Add("L1");
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
                // BELOW THE FIRST STATED LEVEL, THE PLANS STEP DOWN FROM IT. A parkade the plans name and the
                // elevations do not cover walked up from zero and met the first stated level at zero again -
                // P1 = L1 = 0, and with two of them P2 = 0, P1 = 3,000, L1 = 0, the ladder folded (Codex audit
                // 2026-09-13, F3). The storeys before the first stated one are its elevation less a storey
                // height each, counting down.
                if (between > 0 && lastStated < 0)
                    for (int k = 1; k <= between; k++)
                        storeys[i - k] = storeys[i - k] with { ElevationMm = elevation - height * k, From = $"a plan names it; {height:0} mm below {(k == 1 ? name : order[i - k + 1])} ({heightSource})", Assumed = true };
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
