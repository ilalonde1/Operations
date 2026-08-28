#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.QuantityTakeoff;

namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>One storey, answered the way an engineer asks about one: what stands on it.</summary>
public sealed record StoreySummary(
    string Name,
    double Elevation,
    double RiseInches,
    int Walls,
    int Columns,
    int Slabs,
    int Openings,
    double SlabAreaSqFt,
    IReadOnlyList<string> SlabThicknesses,
    IReadOnlyList<string> WallThicknesses,
    double ConcreteYd3);

/// <summary>A storey that is missing something the storeys around it have.</summary>
public sealed record StoreyConcern(string Storey, string What, string Why);

/// <summary>
/// The questions people actually ask about a generated model, answered from the model itself.
///
/// This is the query surface, written once. The CLI drives it today; an /ask tool on the MCP
/// server wraps the same methods later, the way ProjectDetailTool wraps ProjectAnalyticsService —
/// so the answer a person gets in a terminal and the answer they get from /ask are the same answer,
/// not two implementations that drift.
///
/// Every method reads the SHIPPED .e2k. "Does this look right?" is a question about the file the
/// engineer opens, and it has to be answered from that file.
/// </summary>
public static class E2kModelQuery
{
    /// <summary>Every storey, one line each: what stands on it and what it is made of. The
    /// question behind "is it missing a floor?" — an empty storey between full ones is visible
    /// here without opening ETABS.</summary>
    public static IReadOnlyList<StoreySummary> Storeys(E2kDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        double u = doc.LengthUnitInInches() ?? 1.0;
        var plan = doc.PlanPointsOfObjects();
        var storeysOf = doc.StoreysByObject();
        var shells = ShellThickness(doc);
        var kinds = ConnectivityKinds(doc);
        var assigns = AreaSections(doc);
        var lineAssigns = LineStoreys(doc);

        var takeoff = E2kQuantityTakeoff.Read(doc);
        var concreteOf = takeoff.Inputs
            .GroupBy(i => i.Level, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.ConcreteVolume), StringComparer.OrdinalIgnoreCase);

        var rise = RiseByStorey(doc);
        var result = new List<StoreySummary>();

        foreach (var story in doc.ReadStories().OrderByDescending(s => s.Elevation))
        {
            int walls = 0, columns = 0, slabs = 0, openings = 0;
            double area = 0;
            var slabT = new SortedSet<string>();
            var wallT = new SortedSet<string>();

            foreach (var (obj, section, isOpening) in assigns)
            {
                if (!OnStorey(storeysOf, obj, story.Name)) continue;
                string kind = kinds.TryGetValue(obj, out var k) ? k : "";

                if (isOpening) { openings++; continue; }

                if (kind.Equals("FLOOR", StringComparison.OrdinalIgnoreCase))
                {
                    slabs++;
                    if (plan.TryGetValue(obj, out var pts) && pts.Count >= 3)
                        area += PolygonAreaSqFt(pts, u);
                    if (section is not null && shells.TryGetValue(section, out double t)) slabT.Add(Inches(t));
                }
                else if (kind.Equals("PANEL", StringComparison.OrdinalIgnoreCase))
                {
                    walls++;
                    if (section is not null && shells.TryGetValue(section, out double t)) wallT.Add(Inches(t));
                }
            }

            foreach (string obj in lineAssigns)
                if (OnStorey(storeysOf, obj, story.Name)) columns++;

            result.Add(new StoreySummary(
                story.Name, story.Elevation,
                rise.TryGetValue(story.Name, out double r) ? r : 0,
                walls, columns, slabs, openings, area,
                slabT.ToList(), wallT.ToList(),
                concreteOf.TryGetValue(story.Name, out double c) ? c : 0));
        }

        return result;
    }

    /// <summary>
    /// What is worth a second look, said in the engineer's terms rather than the tool's.
    ///
    /// Not a validity check — the publish-blocking invariants are that, and they already passed if
    /// the file shipped. These are the things that are legal in ETABS and still worth a sentence:
    /// a storey holding nothing, a floor with no slab, a slab standing on nothing.
    /// </summary>
    public static IReadOnlyList<StoreyConcern> WorthALook(E2kDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var concerns = new List<StoreyConcern>();
        var summaries = Storeys(doc);

        // A storey with nothing on it, between two that have something. The base carries nothing by
        // design, so only interior storeys count.
        for (int i = 1; i < summaries.Count - 1; i++)
        {
            var s = summaries[i];
            if (s.Walls + s.Columns + s.Slabs > 0) continue;
            if (summaries[i - 1].Walls + summaries[i - 1].Columns + summaries[i - 1].Slabs == 0) continue;
            if (summaries[i + 1].Walls + summaries[i + 1].Columns + summaries[i + 1].Slabs == 0) continue;

            concerns.Add(new StoreyConcern(s.Name, "holds nothing",
                $"the storeys directly above and below it both carry structure, so a floor that was drawn may not have been read."));
        }

        // FloorGaps measures COVERAGE, not existence. A storey here may well carry a slab object —
        // LEVEL 1 of the published 31168 model carries an 11,026 sq ft one — and still be reported,
        // because most of the structure standing on that storey is not underneath it. Saying "has
        // no slab" of a storey that visibly has one in the table above is the tool contradicting
        // itself, so it says what it actually measured.
        var (mostlyUncovered, unsupported) = doc.FloorGaps();

        foreach (string floor in mostlyUncovered)
            concerns.Add(new StoreyConcern(floor, "most of its structure is not under the slab modelled on it",
                "more than half the walls and columns on this storey stand outside every plate on its floor — usually a slab edge the drawing leaves open, so only part of the floor could be closed."));

        foreach (string plate in unsupported)
            concerns.Add(new StoreyConcern(plate, "carries a slab with nothing under it",
                "no wall or column on that storey falls within or near the plate's outline."));

        return concerns;
    }

    /// <summary>Where the holes are, and how big — the question behind "is that opening real?".</summary>
    public static IReadOnlyList<(string Storey, string Object, double AreaSqFt)> Openings(E2kDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        double u = doc.LengthUnitInInches() ?? 1.0;
        var plan = doc.PlanPointsOfObjects();
        var storeysOf = doc.StoreysByObject();
        var result = new List<(string, string, double)>();

        foreach (var (obj, _, isOpening) in AreaSections(doc))
        {
            if (!isOpening) continue;
            string storey = storeysOf.TryGetValue(obj, out var st) && st.Count > 0 ? st[0] : "(none)";
            double area = plan.TryGetValue(obj, out var pts) && pts.Count >= 3 ? PolygonAreaSqFt(pts, u) : 0;
            result.Add((storey, obj, area));
        }

        return result.OrderByDescending(x => x.Item3).ToList();
    }

    /// <summary>Every section the model uses, what it is, and where — the question behind
    /// "what thickness did you give that wall?".</summary>
    public static IReadOnlyList<(string Section, string Kind, string Size, int Used, string Storeys)> Sections(E2kDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var storeysOf = doc.StoreysByObject();
        var shells = ShellThickness(doc);
        var shellKind = ShellKind(doc);
        var frames = FrameSize(doc);
        var used = new Dictionary<string, (int Count, SortedSet<string> On)>(StringComparer.Ordinal);

        void Note(string? section, string obj)
        {
            if (section is null) return;
            if (!used.TryGetValue(section, out var e))
                used[section] = e = (0, new SortedSet<string>(StringComparer.OrdinalIgnoreCase));
            used[section] = (e.Count + 1, e.On);
            if (storeysOf.TryGetValue(obj, out var st) && st.Count > 0) e.On.Add(st[0]);
        }

        foreach (var (obj, section, isOpening) in AreaSections(doc)) { if (!isOpening) Note(section, obj); }
        foreach (var (obj, section) in LineSections(doc)) Note(section, obj);

        return used
            .Select(kv =>
            {
                string kind = shellKind.TryGetValue(kv.Key, out var pk) ? pk
                    : frames.ContainsKey(kv.Key) ? "Column" : "?";
                string size = shells.TryGetValue(kv.Key, out double t) ? Inches(t)
                    : frames.TryGetValue(kv.Key, out var f) ? f : "";
                return (kv.Key, kind, size, kv.Value.Count, string.Join(", ", kv.Value.On));
            })
            .OrderBy(x => x.kind, StringComparer.Ordinal).ThenByDescending(x => x.Count)
            .ToList();
    }

    // ---- shared reading -------------------------------------------------------------------

    /// <summary>The rise to each storey, measured the same way the takeoff measures it: from the
    /// nearest storey more than half a storey below. See <see cref="E2kQuantityTakeoff"/> for why
    /// the model's own HEIGHT field cannot be used on a site model.</summary>
    private static Dictionary<string, double> RiseByStorey(E2kDocument doc)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var storeys = doc.ReadStories().OrderBy(s => s.Elevation).ToList();
        if (storeys.Count == 0) return result;

        double sameFloor = doc.SameFloorTolerance();
        for (int i = 0; i < storeys.Count; i++)
        {
            for (int j = i - 1; j >= 0; j--)
            {
                if (storeys[i].Elevation - storeys[j].Elevation <= sameFloor) continue;
                result[storeys[i].Name] = storeys[i].Elevation - storeys[j].Elevation;
                break;
            }

            // The lowest storey stands on nothing in the model; its own ElevationBelow is the rise.
            if (!result.ContainsKey(storeys[i].Name) && storeys[i].Elevation > storeys[i].ElevationBelow)
                result[storeys[i].Name] = storeys[i].Elevation - storeys[i].ElevationBelow;
        }
        return result;
    }

    private static bool OnStorey(IReadOnlyDictionary<string, IReadOnlyList<string>> storeysOf, string obj, string storey) =>
        storeysOf.TryGetValue(obj, out var st) && st.Contains(storey, StringComparer.OrdinalIgnoreCase);

    private static List<(string Obj, string? Section, bool IsOpening)> AreaSections(E2kDocument doc)
    {
        var result = new List<(string, string?, bool)>();
        foreach (string line in doc.LinesOf("AREA ASSIGNS"))
        {
            var m = Regex.Match(line.Trim(), @"^AREAASSIGN\s+""([^""]+)""", RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            var sec = Regex.Match(line, @"SECTION\s+""([^""]+)""", RegexOptions.IgnoreCase);
            result.Add((m.Groups[1].Value, sec.Success ? sec.Groups[1].Value : null,
                Regex.IsMatch(line, @"OPENING\s+""Yes""", RegexOptions.IgnoreCase)));
        }
        return result;
    }

    private static List<(string Obj, string? Section)> LineSections(E2kDocument doc)
    {
        var result = new List<(string, string?)>();
        foreach (string line in doc.LinesOf("LINE ASSIGNS"))
        {
            var m = Regex.Match(line.Trim(), @"^LINEASSIGN\s+""([^""]+)""", RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            var sec = Regex.Match(line, @"SECTION\s+""((?:[^""]|"""")+)""", RegexOptions.IgnoreCase);
            result.Add((m.Groups[1].Value, sec.Success ? sec.Groups[1].Value : null));
        }
        return result;
    }

    private static List<string> LineStoreys(E2kDocument doc) => LineSections(doc).Select(x => x.Obj).ToList();

    private static Dictionary<string, string> ConnectivityKinds(E2kDocument doc)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string header in new[] { "AREA CONNECTIVITIES", "LINE CONNECTIVITIES" })
            foreach (string line in doc.LinesOf(header))
            {
                var m = Regex.Match(line.Trim(), @"^(?:AREA|LINE)\s+""([^""]+)""\s+(\w+)", RegexOptions.IgnoreCase);
                if (m.Success) result[m.Groups[1].Value] = m.Groups[2].Value;
            }
        return result;
    }

    private static Dictionary<string, double> ShellThickness(E2kDocument doc)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (string header in new[] { "SLAB PROPERTIES", "WALL PROPERTIES", "DECK PROPERTIES" })
            foreach (string line in doc.LinesOf(header))
            {
                var m = Regex.Match(line.Trim(), @"^SHELLPROP\s+""([^""]+)""", RegexOptions.IgnoreCase);
                var t = Regex.Match(line, @"(?:SLABTHICKNESS|WALLTHICKNESS|DECKSLABDEPTH)\s+(-?[\d.eE+]+)", RegexOptions.IgnoreCase);
                if (m.Success && t.Success && double.TryParse(t.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                    result[m.Groups[1].Value] = v;
            }
        return result;
    }

    private static Dictionary<string, string> ShellKind(E2kDocument doc)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string header in new[] { "SLAB PROPERTIES", "WALL PROPERTIES", "DECK PROPERTIES" })
            foreach (string line in doc.LinesOf(header))
            {
                var m = Regex.Match(line.Trim(), @"^SHELLPROP\s+""([^""]+)""", RegexOptions.IgnoreCase);
                var p = Regex.Match(line, @"PROPTYPE\s+""([^""]+)""", RegexOptions.IgnoreCase);
                if (m.Success && p.Success) result[m.Groups[1].Value] = p.Groups[1].Value;
            }
        return result;
    }

    private static Dictionary<string, string> FrameSize(E2kDocument doc)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in doc.LinesOf("FRAME SECTIONS"))
        {
            var m = Regex.Match(line.Trim(), @"^FRAMESECTION\s+""((?:[^""]|"""")+)""", RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            var shape = Regex.Match(line, @"SHAPE\s+""([^""]+)""", RegexOptions.IgnoreCase);
            var d = Regex.Match(line, @"(?<![A-Z])D\s+(-?[\d.eE+]+)");
            var b = Regex.Match(line, @"(?<![A-Z])B\s+(-?[\d.eE+]+)");
            string size =
                shape.Success && shape.Groups[1].Value.Contains("Circle", StringComparison.OrdinalIgnoreCase) && d.Success
                    ? $"{d.Groups[1].Value}\" round"
                    : d.Success && b.Success ? $"{b.Groups[1].Value}\" x {d.Groups[1].Value}\""
                    : shape.Success ? shape.Groups[1].Value : "";
            result[m.Groups[1].Value] = size;
        }
        return result;
    }

    private static double PolygonAreaSqFt(IReadOnlyList<(double X, double Y)> pts, double inchesPerUnit)
    {
        double sum = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return Math.Abs(sum) / 2.0 * inchesPerUnit * inchesPerUnit / 144.0;
    }

    private static string Inches(double v) =>
        v % 1 == 0 ? $"{v:0}\"" : v.ToString("0.##", CultureInfo.InvariantCulture) + "\"";
}
