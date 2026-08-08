using System.Globalization;
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// The checks that were being run by hand, made part of the build.
///
/// Every fault in this file was found by someone asking "are you sure?" and then measuring — a
/// model that referenced sections it never declared, plates modelled twice on the same floor,
/// openings cut where there was no slab, headers spanning to nothing, and 37 elements that were
/// read off the drawing, matched no rule, and vanished without appearing in any count. Counts
/// alone never showed any of it, because a member that is silently dropped leaves no trace in the
/// total: it just isn't there.
///
/// A check that lives in someone's head runs when they remember. These run every build.
///
/// Skipped when the project share is unreachable.
/// </summary>
public class ModelIntegrityTests
{
    private const string Residential = @"\\Kor-fs01\Projects\Projects\03 Residential";

    private sealed record Project(string Name, string DxfFolder, string Reference);

    private static readonly Project Langara = new(
        "31168 YMCA Langara",
        $@"{Residential}\31168-01 (YMCA Langara Vancouver)\02 Engineering\02 Lateral Design\01 ETABS Models\_DXF-plans-for-rebuild",
        $@"{Residential}\31168-01 (YMCA Langara Vancouver)\02 Engineering\02 Lateral Design\01 ETABS Models\31168-reference.e2k");

    private static readonly Project WestFirst = new(
        "31138 2170 W 1st",
        $@"{Residential}\31138-01 (2170 W 1st Ave Vancouver BC)\02 Engineering\02 Lateral Design\_DXF-plans-for-rebuild",
        $@"{Residential}\31138-01 (2170 W 1st Ave Vancouver BC)\02 Engineering\02 Lateral Design\01 ETABS Models\31138-reference-from-Andrea-gravity.e2k");

    public static TheoryData<string> Projects => new() { Langara.Name, WestFirst.Name };

    private static Project For(string name) => name == Langara.Name ? Langara : WestFirst;

    private sealed record Built(string[] Lines, DxfToEtabsReport Report);

    private static Built? BuildOrSkip(Project project)
    {
        if (!Directory.Exists(project.DxfFolder) || !File.Exists(project.Reference)) return null;

        string output = Path.Combine(Path.GetTempPath(), $"kor-integrity-{Guid.NewGuid():N}.e2k");
        try
        {
            var report = DxfToEtabsService.Run(new DxfToEtabsRequest
            {
                DxfFolder = project.DxfFolder,
                ReferenceE2k = project.Reference,
                OutputE2k = output,
            });
            return new Built(File.ReadAllLines(output), report);
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    private static Dictionary<string, (double X, double Y)> Joints(string[] lines)
    {
        var joints = new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase);
        string section = string.Empty;
        foreach (string line in lines)
        {
            if (line.StartsWith('$')) { section = line; continue; }
            if (!section.Contains("POINT COORD", StringComparison.OrdinalIgnoreCase)) continue;

            var m = Regex.Match(line.Trim(), @"^POINT\s+""([^""]+)""\s+(\S+)\s+(\S+)");
            if (m.Success &&
                double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) &&
                double.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
                joints[m.Groups[1].Value] = (x, y);
        }
        return joints;
    }

    private static Dictionary<string, string> FirstStoreyOf(string[] lines)
    {
        var storey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines)
        {
            var m = Regex.Match(line.Trim(), @"^(?:AREA|LINE)ASSIGN\s+""([^""]+)""\s+""([^""]+)""");
            if (m.Success && !storey.ContainsKey(m.Groups[1].Value)) storey[m.Groups[1].Value] = m.Groups[2].Value;
        }
        return storey;
    }

    private static List<string> Refs(string body) =>
        Regex.Matches(body, @"""([^""]+)""").Select(m => m.Groups[1].Value).ToList();

    /// <summary>
    /// Every name an assign refers to must be declared. ETABS drops what it cannot resolve, so a
    /// model can import "successfully" and quietly be missing the thing that was misspelled.
    /// </summary>
    [Theory]
    [MemberData(nameof(Projects))]
    public void EveryNameTheModelUsesIsDeclared(string name)
    {
        var built = BuildOrSkip(For(name));
        if (built is null) return;

        var joints = Joints(built.Lines);
        var storeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "None" };
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var shapes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string section = string.Empty;
        foreach (string raw in built.Lines)
        {
            if (raw.StartsWith('$')) { section = raw; continue; }
            string line = raw.Trim();
            if (line.Length == 0) continue;

            if (section.Contains("STORIES", StringComparison.OrdinalIgnoreCase) &&
                Regex.Match(line, @"^STORY\s+""([^""]+)""") is { Success: true } st) storeys.Add(st.Groups[1].Value);
            else if (section.Contains("PIER/SPANDREL", StringComparison.OrdinalIgnoreCase) &&
                Regex.Match(line, @"^(?:PIERNAME|SPANDRELNAME)\s+""([^""]+)""") is { Success: true } lb) labels.Add(lb.Groups[1].Value);
            else if (Regex.Match(line, @"^(?:SHELLPROP|FRAMESECTION)\s+""([^""]+)""") is { Success: true } sc) sections.Add(sc.Groups[1].Value);

            if (Regex.Match(line, @"^(?:AREA|LINE)\s+""([^""]+)""\s+\w") is { Success: true } sh) shapes.Add(sh.Groups[1].Value);
        }

        var broken = new List<string>();
        foreach (string raw in built.Lines)
        {
            string line = raw.Trim();

            var geometry = Regex.Match(line, @"^(?:AREA|LINE)\s+""[^""]+""\s+\w+\s+(.*)$");
            if (geometry.Success)
                foreach (string j in Refs(geometry.Groups[1].Value))
                    if (!joints.ContainsKey(j)) broken.Add($"joint {j}");

            var assign = Regex.Match(line, @"^(?:AREA|LINE)ASSIGN\s+""([^""]+)""\s+""([^""]+)""");
            if (!assign.Success) continue;

            if (!shapes.Contains(assign.Groups[1].Value)) broken.Add($"assign for {assign.Groups[1].Value}, which has no shape");
            if (!storeys.Contains(assign.Groups[2].Value)) broken.Add($"storey {assign.Groups[2].Value}");
            if (Regex.Match(line, @"SECTION\s+""([^""]+)""") is { Success: true } s && !sections.Contains(s.Groups[1].Value))
                broken.Add($"section {s.Groups[1].Value}");
            if (Regex.Match(line, @"\b(?:PIER|SPANDREL)\s+""([^""]+)""") is { Success: true } p && !labels.Contains(p.Groups[1].Value))
                broken.Add($"label {p.Groups[1].Value}");
        }

        if (broken.Count == 0) return;
        Assert.Fail($"{name}: {broken.Count} dangling reference(s): {string.Join(", ", broken.Distinct().Take(6))}");
    }

    /// <summary>
    /// Nothing read off a drawing may be silently discarded. Drafting issues both a range sheet and
    /// a sheet per level, so a floor arrives twice and was modelled twice; and an element too short
    /// to be a wall and too slender to be a stubby column satisfied no rule and disappeared. Both
    /// were invisible in the totals — a plate counted twice looks like two plates, and a dropped
    /// member looks like nothing at all.
    /// </summary>
    [Theory]
    [MemberData(nameof(Projects))]
    public void NothingIsModelledTwiceAndNothingIsDroppedInSilence(string name)
    {
        var built = BuildOrSkip(For(name));
        if (built is null) return;

        var joints = Joints(built.Lines);
        var storeyOf = FirstStoreyOf(built.Lines);

        // No two plates on one storey may share a place.
        var seen = new HashSet<(string, long, long)>();
        var doubled = new List<string>();
        foreach (string raw in built.Lines)
        {
            var m = Regex.Match(raw.Trim(), @"^AREA\s+""(KF\d+)""\s+FLOOR\s+\d+\s+(.+)$");
            if (!m.Success) continue;

            var ids = Refs(m.Groups[2].Value).Where(joints.ContainsKey).ToList();
            if (ids.Count < 3 || !storeyOf.TryGetValue(m.Groups[1].Value, out string? storey)) continue;

            var key = (storey,
                (long)Math.Round(ids.Average(i => joints[i].X) / 12.0),
                (long)Math.Round(ids.Average(i => joints[i].Y) / 12.0));
            if (!seen.Add(key)) doubled.Add($"{m.Groups[1].Value} on {storey}");
        }

        Assert.True(doubled.Count == 0,
            $"{name}: {doubled.Count} plate(s) modelled on top of another: {string.Join(", ", doubled.Take(5))}");

        // An outline the classifier could not place at all is a member that left no trace. This is
        // a ratchet, not a target: the number may only ever come down. Each figure is what the
        // project measured once the drop was found and the easy causes fixed, and lowering it is
        // the point — raising it means members started disappearing again.
        int allowed = name == Langara.Name ? 25 : 4;

        var unresolved = built.Report.Summary.Flags
            .Count(f => f.Contains("could not be resolved", StringComparison.OrdinalIgnoreCase));

        Assert.True(unresolved <= allowed,
            $"{name}: {unresolved} outline(s) were read and then modelled as nothing, against {allowed} " +
            "recorded. A dropped member shows up in no count, so this is the only place it is visible.");
    }

    /// <summary>
    /// A member has to be attached to something. An opening cut where there is no slab removes
    /// nothing, and a header spanning to thin air couples nothing — both look right in a count and
    /// wrong in the model.
    /// </summary>
    [Theory]
    [MemberData(nameof(Projects))]
    public void OpeningsAndHeadersAreAttachedToWhatTheyBelongTo(string name)
    {
        var built = BuildOrSkip(For(name));
        if (built is null) return;

        var joints = Joints(built.Lines);
        var storeyOf = FirstStoreyOf(built.Lines);

        var plates = new Dictionary<string, List<List<string>>>(StringComparer.OrdinalIgnoreCase);
        var wallEnds = new HashSet<(string, long, long)>();

        foreach (string raw in built.Lines)
        {
            string line = raw.Trim();

            var plate = Regex.Match(line, @"^AREA\s+""(KF\d+)""\s+FLOOR\s+\d+\s+(.+)$");
            if (plate.Success && storeyOf.TryGetValue(plate.Groups[1].Value, out string? ps))
            {
                var ids = Refs(plate.Groups[2].Value).Where(joints.ContainsKey).ToList();
                if (ids.Count >= 3)
                {
                    if (!plates.TryGetValue(ps, out var list)) plates[ps] = list = new List<List<string>>();
                    list.Add(ids);
                }
            }

            // Any wall, not only a generated one. Where the engineer already has the wall we do not
            // model it again, so a header can legitimately land on hers.
            var wall = Regex.Match(line, @"^AREA\s+""(\w+)""\s+PANEL\s+4\s+""([^""]+)""\s+""([^""]+)""");
            if (wall.Success && storeyOf.TryGetValue(wall.Groups[1].Value, out string? ws))
                foreach (string id in new[] { wall.Groups[2].Value, wall.Groups[3].Value })
                    if (joints.TryGetValue(id, out var p))
                        wallEnds.Add((ws, (long)Math.Round(p.X * 10), (long)Math.Round(p.Y * 10)));
        }

        bool Inside(double x, double y, List<string> ring)
        {
            bool inside = false;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                var a = joints[ring[i]];
                var b = joints[ring[j]];
                if (a.Y > y != b.Y > y && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }

        var floating = new List<string>();
        var loose = new List<string>();

        foreach (string raw in built.Lines)
        {
            string line = raw.Trim();

            var opening = Regex.Match(line, @"^AREA\s+""(KO\d+)""\s+AREA\s+\d+\s+(.+)$");
            if (opening.Success && storeyOf.TryGetValue(opening.Groups[1].Value, out string? os))
            {
                var ids = Refs(opening.Groups[2].Value).Where(joints.ContainsKey).ToList();
                if (ids.Count >= 3)
                {
                    double cx = ids.Average(i => joints[i].X), cy = ids.Average(i => joints[i].Y);
                    if (!plates.TryGetValue(os, out var onStorey) || !onStorey.Any(r => Inside(cx, cy, r)))
                        floating.Add(opening.Groups[1].Value);
                }
            }

            var header = Regex.Match(line, @"^AREA\s+""(KS\d+)""\s+PANEL\s+4\s+""[^""]+""\s+""[^""]+""\s+""([^""]+)""\s+""([^""]+)""");
            if (header.Success && storeyOf.TryGetValue(header.Groups[1].Value, out string? hs))
            {
                int attached = new[] { header.Groups[2].Value, header.Groups[3].Value }
                    .Count(id => joints.TryGetValue(id, out var p) &&
                                 wallEnds.Contains((hs, (long)Math.Round(p.X * 10), (long)Math.Round(p.Y * 10))));
                if (attached < 2) loose.Add(header.Groups[1].Value);
            }
        }

        Assert.True(floating.Count == 0,
            $"{name}: {floating.Count} opening(s) cut where the storey has no plate: {string.Join(", ", floating.Take(5))}");
        Assert.True(loose.Count == 0,
            $"{name}: {loose.Count} header(s) with an end on no wall: {string.Join(", ", loose.Take(5))}");
    }

    /// <summary>
    /// Reads the shipped file as ETABS reads it — as text — and holds the storey list to what ETABS
    /// will build from it. Nothing here goes through this project's own parser.
    ///
    /// That distinction is the whole point. The base storey has been wrong five times: ignored, so
    /// the geometry sat a thousand feet high; honoured, so the lowest walls became 1,113ft spikes;
    /// capped, so the parkade came out four storeys tall; and then corrected inside the reader
    /// while the file still said HEIGHT 13366, which is what ETABS obeyed — the parkade imported as
    /// a solid block half the height of the building. Each fix was to the code that reads the
    /// storey list, and each was checked by that same code, so reader and writer agreed with one
    /// another and both were wrong about what ETABS would do.
    ///
    /// A test that shares the assumption it is testing proves nothing. This one parses the raw
    /// section and applies the only rule that matters: a storey is a storey.
    /// </summary>
    [Theory]
    [MemberData(nameof(Projects))]
    public void TheStoreyListAsWrittenBuildsRealStoreys(string name)
    {
        var built = BuildOrSkip(For(name));
        if (built is null) return;

        var heights = new List<(string Name, double Height)>();
        double? baseElevation = null;

        string section = string.Empty;
        foreach (string raw in built.Lines)
        {
            if (raw.StartsWith('$')) { section = raw; continue; }
            if (!section.Contains("STORIES", StringComparison.OrdinalIgnoreCase)) continue;

            string line = raw.Trim();
            var withHeight = Regex.Match(line, @"^STORY\s+""([^""]+)""\s+HEIGHT\s+(\S+)", RegexOptions.IgnoreCase);
            if (withHeight.Success &&
                double.TryParse(withHeight.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double h))
            {
                heights.Add((withHeight.Groups[1].Value, h));
                continue;
            }

            var withElev = Regex.Match(line, @"^STORY\s+""[^""]+""\s+ELEV\s+(\S+)", RegexOptions.IgnoreCase);
            if (withElev.Success &&
                double.TryParse(withElev.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double e))
                baseElevation = e;
        }

        Assert.NotEmpty(heights);

        // No storey taller than a double-height lobby. ETABS extrudes every member on a storey by
        // this number, so one bad value is a whole level of the building drawn as a solid mass.
        var absurd = heights.Where(s => s.Height > 480).ToList();
        Assert.True(absurd.Count == 0,
            $"{name}: the storey list as written contains {absurd.Count} storey taller than 40ft — " +
            string.Join(", ", absurd.Select(s => $"{s.Name} at {s.Height / 12:0}ft")) +
            ". ETABS extrudes members by this, whatever the reader believes.");

        // And the building has to start somewhere believable rather than a thousand feet down.
        if (baseElevation is { } b)
            Assert.True(Math.Abs(b) < 12 * 500,
                $"{name}: the base is written at {b / 12:0}ft, which is not where this building starts.");
    }

    /// <summary>Generated sections must not collide with the project's own, or one silently wins.</summary>
    [Theory]
    [MemberData(nameof(Projects))]
    public void GeneratedSectionsDoNotCollideWithTheProjectsOwn(string name)
    {
        var built = BuildOrSkip(For(name));
        if (built is null) return;

        var mine = new List<string>();
        var theirs = new List<string>();
        foreach (string raw in built.Lines)
        {
            var m = Regex.Match(raw.Trim(), @"^(?:SHELLPROP|FRAMESECTION)\s+""([^""]+)""");
            if (!m.Success) continue;
            (m.Groups[1].Value.StartsWith("KOR-", StringComparison.OrdinalIgnoreCase) ? mine : theirs).Add(m.Groups[1].Value);
        }

        var clash = mine.Intersect(theirs, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.True(clash.Count == 0, $"{name}: generated sections reuse project names: {string.Join(", ", clash)}");
    }
}
