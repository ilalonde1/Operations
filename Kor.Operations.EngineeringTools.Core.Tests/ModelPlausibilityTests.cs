using System.Globalization;
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// The checks an engineer makes by opening the model and looking at it.
///
/// Counts and coordinates are blind to the faults that make a model look wrong: a wall two
/// inches tall, a scrap of slab hanging in space, a floor with nothing under it. Every one of
/// those passed the count-based baseline while 31168 looked, in the engineer's words, like
/// amateur work. These rules state what a building must look like, so the next fault of that
/// kind fails here instead of in front of the person we send the model to.
///
/// Skipped when the project share is unreachable.
/// </summary>
public class ModelPlausibilityTests
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

    /// <summary>A generated model, read back as the shapes it will draw as.</summary>
    private sealed record Rendered(
        IReadOnlyList<double> WallHeights,
        IReadOnlyList<double> PlateAreas,
        IReadOnlyList<double> ColumnHeights,
        IReadOnlyList<double> SpandrelDepths);

    private static Rendered? BuildOrSkip(Project project)
    {
        if (!Directory.Exists(project.DxfFolder) || !File.Exists(project.Reference)) return null;

        string output = Path.Combine(Path.GetTempPath(), $"kor-plausible-{Guid.NewGuid():N}.e2k");
        try
        {
            DxfToEtabsService.Run(new DxfToEtabsRequest
            {
                DxfFolder = project.DxfFolder,
                ReferenceE2k = project.Reference,
                OutputE2k = output,
            });

            return Read(E2kDocument.Load(output));
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    /// <summary>
    /// Measures a member the way ETABS builds it: joints carry plan position only, and the
    /// vertical extent comes from the storeys the member is assigned to. Reading an elevation
    /// off a POINT line measures nothing — that number is a storey offset.
    /// </summary>
    private static Rendered Read(E2kDocument doc)
    {
        var ordered = doc.ReadStories().OrderBy(s => s.Elevation).ToList();
        var stories = ordered.ToDictionary(s => s.Name, s => s, StringComparer.OrdinalIgnoreCase);

        // ETABS builds one instance of a member per storey it is assigned to, and each instance
        // runs from the storey immediately below it in the model's own list — not from the floor
        // of its tower. A member assigned to several storeys therefore stacks into one continuous
        // run, and its true extent is measured from the global storey under the lowest instance.
        var globalFloor = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < ordered.Count; i++)
            globalFloor[ordered[i].Name] = i == 0 ? ordered[i].ElevationBelow : ordered[i - 1].Elevation;

        var geometry = E2kGeometryReader.Read(doc);

        // A member also carries the number of storeys it reaches down through, so that a tower's
        // wall can pass the other tower's floor levels in one piece. Measuring from the storey
        // immediately below the assignment then reads that whole wall as the two-inch sliver it
        // starts in — the wafer fault inverted, and the reason this had to learn the span.
        var indexOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < ordered.Count; i++) indexOf[ordered[i].Name] = i;

        var spanOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in doc.LinesOf("AREA CONNECTIVITIES").Concat(doc.LinesOf("LINE CONNECTIVITIES")))
        {
            string t = raw.Trim();
            var wall = Regex.Match(t, @"^AREA\s+""(K\w+)""\s+PANEL\s+4\s+(?:""[^""]+""\s+){4}(\d+)");
            if (wall.Success) { spanOf[wall.Groups[1].Value] = int.Parse(wall.Groups[2].Value); continue; }
            var column = Regex.Match(t, @"^LINE\s+""(K\w+)""\s+COLUMN\s+""[^""]+""\s+""[^""]+""\s+(\d+)");
            if (column.Success) spanOf[column.Groups[1].Value] = int.Parse(column.Groups[2].Value);
        }

        double SpanOf(string member, IEnumerable<string> storeyNames)
        {
            var known = storeyNames.Where(stories.ContainsKey).ToList();
            if (known.Count == 0) return double.NaN;

            double top = known.Max(n => stories[n].Elevation);
            string lowest = known.OrderBy(n => stories[n].Elevation).First();

            int n0 = Math.Max(spanOf.GetValueOrDefault(member, 1), 1);
            int i = indexOf.GetValueOrDefault(lowest, -1);
            double floor = i - n0 >= 0 ? ordered[i - n0].Elevation
                         : i >= 0      ? ordered[0].ElevationBelow
                         : globalFloor[lowest];

            return top - floor;
        }

        // Spandrels are wall panels too, but a header over a door is meant to be shallow — it is
        // sized as the storey height less the opening height. Only full-height walls are held to
        // the storey-height rules.
        var wallHeights = geometry.Walls
            .Where(w => w.Name.StartsWith("KW", StringComparison.OrdinalIgnoreCase))
            .GroupBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => SpanOf(g.Key, g.Select(w => w.Story)))
            .Where(h => !double.IsNaN(h))
            .ToList();

        // Every header must be deep enough to be a beam and shallow enough not to be a wall. The
        // depth is carried as a joint offset, which is how ETABS defines a partial-height panel.
        var spandrelDepths = new List<double>();
        foreach (string raw in doc.LinesOf("POINT COORDINATES"))
        {
            var m = Regex.Match(raw.Trim(), @"^POINT\s+""K\w+""\s+\S+\s+\S+\s+(\S+)");
            if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double z))
                spandrelDepths.Add(z);
        }

        var columnHeights = geometry.Columns
            .Where(c => c.Name.StartsWith("K", StringComparison.OrdinalIgnoreCase))
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => SpanOf(g.Key, g.Select(c => c.Story)))
            .Where(h => !double.IsNaN(h))
            .ToList();

        // Plates are horizontal, so plan position is the whole story for area.
        var plan = new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in doc.LinesOf("POINT COORDINATES"))
        {
            var m = Regex.Match(raw.Trim(), @"^POINT\s+""([^""]+)""\s+(\S+)\s+(\S+)");
            if (m.Success &&
                double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double px) &&
                double.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double py))
                plan[m.Groups[1].Value] = (px, py);
        }

        var plateAreas = new List<double>();
        foreach (string raw in doc.LinesOf("AREA CONNECTIVITIES"))
        {
            var m = Regex.Match(raw.Trim(), @"^AREA\s+""(K\w+)""\s+FLOOR\s+\d+\s+(.+)$", RegexOptions.IgnoreCase);
            if (!m.Success) continue;

            var ids = Regex.Matches(m.Groups[2].Value, @"""([^""]+)""")
                .Select(q => q.Groups[1].Value)
                .Where(plan.ContainsKey)
                .ToList();
            if (ids.Count < 3) continue;

            double sum = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                var a = plan[ids[i]];
                var b = plan[ids[(i + 1) % ids.Count]];
                sum += a.X * b.Y - b.X * a.Y;
            }
            plateAreas.Add(Math.Abs(sum / 2));
        }

        return new Rendered(wallHeights, plateAreas, columnHeights, spandrelDepths);
    }

    /// <summary>
    /// A header must be deep enough to act as one and shallow enough not to be a wall. Its depth
    /// is the storey height less the opening height — the engineer's rule — and on a double-height
    /// storey that arithmetic produced a 396" header before it was bounded.
    ///
    /// The bound is hers, given as an answer: "Bounding can be 18"-60"". This used to allow
    /// 12"-72", which is looser than what she said and would have passed a model that ignored her.
    /// A rule she has ruled on is the rule the build holds us to.
    /// </summary>
    [Theory]
    [MemberData(nameof(Projects))]
    public void EveryHeaderIsHeaderSized(string name)
    {
        var model = BuildOrSkip(For(name));
        if (model is null || model.SpandrelDepths.Count == 0) return;

        var wrong = model.SpandrelDepths.Where(d => d < 18 - 0.01 || d > 60 + 0.01).ToList();
        if (wrong.Count == 0) return;
        Assert.Fail($"{name}: {wrong.Count} header(s) outside 12-72in, extremes " +
                    $"{wrong.Min():0} and {wrong.Max():0}in.");
    }

    /// <summary>
    /// A wall shorter than a person is not a wall. 31168's site model interleaves three towers on
    /// one storey list, so storeys exist that are 2" tall; taking a storey's own height as its wall
    /// height turned 78 panels into wafers floating a full storey above the floor below.
    /// </summary>
    [Theory]
    [MemberData(nameof(Projects))]
    public void NoWallIsShorterThanAPerson(string name)
    {
        var model = BuildOrSkip(For(name));
        if (model is null || model.WallHeights.Count == 0) return;

        var wafers = model.WallHeights.Where(h => h < 72).ToList();
        if (wafers.Count == 0) return;
        Assert.Fail($"{name}: {wafers.Count} wall panel(s) under 6ft tall, shortest {wafers.Min() / 12:0.00}ft. " +
                    "A panel that short is a storey-height fault, not a wall.");
    }

    /// <summary>The same fault seen from the other side: a column standing on nothing.</summary>
    [Theory]
    [MemberData(nameof(Projects))]
    public void NoColumnIsShorterThanAPerson(string name)
    {
        var model = BuildOrSkip(For(name));
        if (model is null || model.ColumnHeights.Count == 0) return;

        var stubs = model.ColumnHeights.Where(h => h < 72).ToList();
        if (stubs.Count == 0) return;
        Assert.Fail($"{name}: {stubs.Count} column(s) under 6ft, shortest {stubs.Min() / 12:0.00}ft.");
    }

    /// <summary>
    /// No storey is taller than a double-height lobby. Catches the opposite error — a wall drawn
    /// from the roof down to a base parked a thousand feet below the building.
    /// </summary>
    [Theory]
    [MemberData(nameof(Projects))]
    public void NoWallIsTallerThanADoubleHeightStorey(string name)
    {
        var model = BuildOrSkip(For(name));
        if (model is null || model.WallHeights.Count == 0) return;

        var spikes = model.WallHeights.Where(h => h > 480).ToList();
        if (spikes.Count == 0) return;
        Assert.Fail($"{name}: {spikes.Count} wall panel(s) over 40ft tall, tallest {spikes.Max() / 12:0}ft.");
    }

    /// <summary>
    /// A floor plate smaller than a room is slab-edge linework that happened to close. Modelled,
    /// each one draws as a scrap of concrete hanging in space — 7 of 31138's 14 plates were
    /// 52-68 sq ft against a real tower floor of 9,666.
    /// </summary>
    [Theory]
    [MemberData(nameof(Projects))]
    public void NoFloorPlateIsSmallerThanARoom(string name)
    {
        var model = BuildOrSkip(For(name));
        if (model is null || model.PlateAreas.Count == 0) return;

        var scraps = model.PlateAreas.Where(a => a / 144 < 200).ToList();
        if (scraps.Count == 0) return;
        Assert.Fail($"{name}: {scraps.Count} floor plate(s) under 200 sq ft, smallest {scraps.Min() / 144:0} sq ft.");
    }

    /// <summary>
    /// Storey heights come out of the reference model's own storey list, so every project's
    /// storeys must survive the rule that derives them — before any geometry is placed on them.
    /// </summary>
    [Theory]
    [MemberData(nameof(Projects))]
    public void EveryStoreyInTheReferenceIsAPlausibleStorey(string name)
    {
        var project = For(name);
        if (!File.Exists(project.Reference)) return;

        var stories = E2kDocument.Load(project.Reference).ReadStories().OrderBy(s => s.Elevation).ToList();

        // The topmost storey is exempt: a roof or parapet level genuinely is a few feet tall.
        var bad = stories
            .Take(stories.Count - 1)
            .Select(s => (s.Name, Span: s.Elevation - s.ElevationBelow))
            .Where(s => s.Span < 60 || s.Span > 480)
            .ToList();

        if (bad.Count == 0) return;
        Assert.Fail($"{name}: storeys that are not storey-height: " +
                    string.Join(", ", bad.Select(b => $"{b.Name} {b.Span / 12:0.0}ft")));
    }
}
