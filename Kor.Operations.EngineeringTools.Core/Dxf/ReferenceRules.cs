using System.Globalization;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>A value taken from the model in front of us, or the fallback used when it could not be.</summary>
public sealed record DerivedRule(string Name, double Value, bool FromReference, string Because);

/// <summary>
/// Rules read off the reference model rather than baked in.
///
/// Several numbers in this tool were measured once, from one engineer's one model, and then became
/// constants: an 88" opening height, a 3:1 slenderness limit. They are right for the job they came
/// from and there is no reason to think they travel.
///
/// DERIVING THEM IS NOT AUTOMATICALLY BETTER. Asked for an opening height, 31138's model gives 88"
/// from 29 spandrels — clean, and exactly what was hard-coded. The same question put to 31168's
/// reference gives 37", because the partial-height panels in it are not door spandrels at all.
/// Taken at face value that would size every header in the building off a number that describes
/// nothing. So a derived value is accepted only when there is enough of it AND it lands inside the
/// range the quantity can physically occupy; otherwise the fallback stands and the report says so.
/// </summary>
public static class ReferenceRules
{
    /// <summary>
    /// The opening a header spans, implied by the reference model's own spandrels: each one's depth
    /// subtracted from the height of the storey it sits on.
    ///
    /// A door or window head sits between about 5 and 10 feet. Outside that the panels being
    /// measured are not spandrels over openings, whatever their form says.
    /// </summary>
    public static DerivedRule OpeningHeight(E2kDocument doc, double fallback)
    {
        const int enoughSamples = 8;
        const double lowestCredible = 60.0, highestCredible = 120.0;

        var storeyHeight = doc.ReadStories()
            .ToDictionary(s => s.Name, s => s.Elevation - s.ElevationBelow, StringComparer.OrdinalIgnoreCase);

        var spandrels = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in doc.LinesOf("AREA CONNECTIVITIES"))
        {
            var m = Regex.Match(raw.Trim(),
                @"^AREA\s+""([^""]+)""\s+PANEL\s+(\d+)\s+((?:""[^""]+""\s+)+)([\d\s.]+)$", RegexOptions.IgnoreCase);
            if (!m.Success) continue;

            var offsets = m.Groups[4].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(v => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : -1)
                .ToList();

            // A spandrel is the partial-height form: every joint at the storey, none a storey up.
            if (offsets.Count == 0 || offsets.Any(v => v < 0) || offsets.Any(v => v >= 1)) continue;
            spandrels[m.Groups[1].Value] = 0;
        }

        // Depth is carried by the raised joints, which the POINT lines hold as a third value.
        var depthOf = doc.PanelJointOffsets();

        // One observation per SPANDREL, not per assignment. A spandrel repeated up fifteen storeys
        // is one decision the engineer made, and letting it vote fifteen times hands the answer to
        // whichever storey height happens to recur most.
        var perPanel = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in doc.LinesOf("AREA ASSIGNS"))
        {
            var m = Regex.Match(raw.Trim(), @"^AREAASSIGN\s+""([^""]+)""\s+""([^""]+)""", RegexOptions.IgnoreCase);
            if (!m.Success || !spandrels.ContainsKey(m.Groups[1].Value)) continue;
            if (!storeyHeight.TryGetValue(m.Groups[2].Value, out double height) || height <= 0) continue;
            if (!depthOf.TryGetValue(m.Groups[1].Value, out double depth) || depth <= 0) continue;

            if (!perPanel.TryGetValue(m.Groups[1].Value, out var seen)) perPanel[m.Groups[1].Value] = seen = new List<double>();
            seen.Add(Math.Round(height - depth));
        }

        var implied = perPanel.Values
            .Select(v => v.OrderBy(x => x).ElementAt(v.Count / 2))   // that spandrel's typical opening
            .ToList();

        if (implied.Count < enoughSamples)
            return new DerivedRule("opening height", fallback, false,
                $"the reference model has only {implied.Count} spandrel(s) to measure, so the standing value stands");

        // The commonest, not the mean: openings come in a few sizes and a mean between them is a
        // size nothing in the building actually is.
        double commonest = implied.GroupBy(v => v).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;

        if (commonest < lowestCredible || commonest > highestCredible)
            return new DerivedRule("opening height", fallback, false,
                $"its {implied.Count} spandrels imply {commonest:0}\", which is not the height of a door or a " +
                $"window, so those panels are not spandrels over openings and the standing value stands");

        return new DerivedRule("opening height", commonest, true,
            $"measured off the reference model's own {implied.Count} spandrels, each one's depth taken from " +
            $"the height of the storey it sits on");
    }

    /// <summary>
    /// How slender a column may be, taken from the most slender one the reference model actually
    /// uses. Below 2:1 nothing would ever be a wall; past 6:1 the "column" is a wall by any
    /// reading, and a reference containing one is not evidence that the next drawing should follow.
    /// </summary>
    public static DerivedRule MaxColumnAspect(E2kDocument doc, double fallback)
    {
        const int enoughSections = 3;
        const double lowestCredible = 2.0, highestCredible = 6.0;

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in doc.LinesOf("LINE ASSIGNS"))
        {
            var m = Regex.Match(raw.Trim(), @"^LINEASSIGN\s+""[^""]+""\s+""[^""]+""\s+SECTION\s+""([^""]+)""", RegexOptions.IgnoreCase);
            if (m.Success) used.Add(m.Groups[1].Value);
        }

        var aspects = new List<double>();
        foreach (string raw in doc.LinesOf("FRAME SECTIONS"))
        {
            var m = Regex.Match(raw.Trim(), @"^FRAMESECTION\s+""(.+?)""\s+.*?SHAPE\s+""Concrete Rectangular""", RegexOptions.IgnoreCase);
            if (!m.Success || !used.Contains(m.Groups[1].Value)) continue;

            var d = Regex.Match(raw, @"\sD\s+([\d.]+)");
            var b = Regex.Match(raw, @"\sB\s+([\d.]+)");
            if (!d.Success || !b.Success) continue;

            double dd = double.Parse(d.Groups[1].Value, CultureInfo.InvariantCulture);
            double bb = double.Parse(b.Groups[1].Value, CultureInfo.InvariantCulture);
            if (dd > 0 && bb > 0) aspects.Add(Math.Max(dd, bb) / Math.Min(dd, bb));
        }

        if (aspects.Count < enoughSections)
            return new DerivedRule("column slenderness", fallback, false,
                $"the reference model uses only {aspects.Count} rectangular concrete column section(s), " +
                "which is too few to read a limit from");

        double slenderest = aspects.Max();
        if (slenderest < lowestCredible || slenderest > highestCredible)
            return new DerivedRule("column slenderness", fallback, false,
                $"its most slender column is {slenderest:0.0}:1, outside the range a limit can sensibly be " +
                "drawn from, so the standing value stands");

        return new DerivedRule("column slenderness", slenderest, true,
            $"the most slender of the {aspects.Count} column sections the reference model uses is {slenderest:0.0}:1");
    }
}
