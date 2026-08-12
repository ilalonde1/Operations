using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// The audit that closes the class of fault counts cannot see.
///
/// Everything caught so far has been one of four things: geometry DROPPED, geometry DOUBLED,
/// geometry MISPLACED, geometry MISCLASSIFIED. Totals catch the first two and are blind to the
/// last two — a member built on the wrong storey leaves every count healthy. That is how 31168
/// shipped with the ground floor of both towers empty and its 45 walls and 67 columns standing on
/// the mezzanine: nothing in any number was wrong.
///
/// So this checks the model against the DRAWINGS rather than against itself, in both directions:
///
///   drawn -> modelled   every wall and column the classifier reads off a sheet is either built,
///                       or already in the engineer's model at that place, or unexplained.
///   modelled -> drawn   every generated member stands on linework from a sheet placed on a storey
///                       it is assigned to. A member with nothing drawn beneath it was invented.
///
/// and one structural check that needs no geometry at all: a storey between two populated storeys
/// may not be empty. That single assertion would have caught the mezzanine fault on day one.
///
/// Skipped when the project share is unreachable.
/// </summary>
public class ModelCoverageTests
{
    private readonly ITestOutputHelper _out;

    public ModelCoverageTests(ITestOutputHelper output) => _out = output;

    public static TheoryData<string> Projects => GeneratedModel.Projects;

    /// <summary>How close a modelled member must be to drawn linework to count as the same (inches).</summary>
    private const double Tolerance = 18.0;

    // ---------------------------------------------------------------------------------------
    // 1. No hole in the building
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A storey lying between two storeys that carry structure must carry some itself. Buildings do
    /// not have gaps: if the drawings fill level 2 and the parkade but not level 1, either a sheet
    /// was placed on the wrong storey or one was never placed at all, and both are faults.
    ///
    /// This is the cheapest check in the file and the one that would have caught the most
    /// expensive fault — no geometry, no tolerance, no judgement.
    /// </summary>
    [Theory]
    [MemberData(nameof(Projects))]
    public void NoStoreyBetweenPopulatedStoreysIsEmpty(string name)
    {
        var built = GeneratedModel.BuildOrSkip(GeneratedModel.For(name));
        if (built is null) return;

        var order = GeneratedModel.StoreysTopToBottom(built.Lines);
        var generated = GeneratedModel.MembersByStorey(built.Lines);

        // Structure from ANY source counts. On 31138 the tool is a gap-fill on top of a model the
        // engineer had already built, and levels 9, 12 and 18 have no drawing at all — she had
        // already modelled them, so generating nothing there is correct, not a hole.
        var existing = GeneratedModel.ExistingByStorey(GeneratedModel.For(name));
        bool Populated(string storey) => generated.ContainsKey(storey) || existing.ContainsKey(storey);

        int first = order.FindIndex(Populated);
        int last = order.FindLastIndex(Populated);
        if (first < 0) return;

        var holes = new List<string>();
        for (int i = first; i <= last; i++)
            if (!Populated(order[i]))
                holes.Add(order[i]);

        _out.WriteLine($"{name}: {order.Count} storeys, structure from '{order[first]}' down to '{order[last]}'.");

        Assert.True(holes.Count == 0,
            $"{name}: {holes.Count} storey(s) sit between populated storeys with nothing on them — " +
            $"{string.Join(", ", holes)}. Either a sheet was placed on the wrong storey, or none was placed at all.");
    }

    // ---------------------------------------------------------------------------------------
    // 2. modelled -> drawn.  Nothing was invented.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Every generated wall and column must stand on RAW linework — segments off the wall or column
    /// layers of a sheet placed on a storey it is assigned to.
    ///
    /// Raw, because the first version of this compared generated members against the classifier's
    /// own output, and the model is built from that output: the two could only ever agree. It
    /// reported nothing invented while Building C carried a closed ring of wall the drawings do not
    /// contain. A member with no drawing under it came from somewhere — the wrong storey, or a rule
    /// that fired where it should not have.
    /// </summary>
    [Theory]
    [MemberData(nameof(Projects))]
    public void EveryGeneratedMemberStandsOnLineworkFromItsOwnStorey(string name)
    {
        var project = GeneratedModel.For(name);
        var built = GeneratedModel.BuildOrSkip(project);
        if (built is null) return;

        var wallLines = GeneratedModel.LineworkByStorey(project, built.Report, "WALL");
        var colLines = GeneratedModel.LineworkByStorey(project, built.Report, "_COL");
        var joints = GeneratedModel.Joints(built.Lines);
        var assigns = GeneratedModel.AssignedStoreys(built.Lines);

        // A member is measured from its CENTRELINE, and its own linework is half its width away —
        // a 42" wall's centreline sits 21" from either face, a 65x82 column's centre 32" from its
        // nearest one. Reaching only the bare tolerance calls those inventions when they are
        // nothing of the sort, so the reach carries the member's own half-width.
        var sections = GeneratedModel.Sections(built.Lines);
        var wallThickness = GeneratedModel.WallThicknesses(built.Lines);
        var sectionOf = GeneratedModel.SectionOfMember(built.Lines);

        double ReachFor(string member, bool isWall)
        {
            if (!sectionOf.TryGetValue(member, out string? section)) return Tolerance;
            if (isWall)
                return wallThickness.TryGetValue(section, out double t) ? t / 2.0 + Tolerance : Tolerance;
            return sections.TryGetValue(section, out var box)
                ? Math.Max(box.D, box.B) / 2.0 + Tolerance
                : Tolerance;
        }

        var unfounded = new List<string>();
        int walls = 0, columns = 0;

        foreach (string line in built.Lines)
        {
            string t = line.Trim();

            var wall = Regex.Match(t, @"^AREA\s+""(KW\d+)""\s+PANEL\s+4\s+""([^""]+)""\s+""([^""]+)""");
            if (wall.Success && joints.TryGetValue(wall.Groups[2].Value, out var a) && joints.TryGetValue(wall.Groups[3].Value, out var b))
            {
                walls++;
                if (!StandsOnLinework(wall.Groups[1].Value, Mid(a, b), wallLines, colLines, assigns, ReachFor(wall.Groups[1].Value, true)))
                    unfounded.Add($"{wall.Groups[1].Value} at ({Mid(a, b).X:F0},{Mid(a, b).Y:F0}) on {Where(wall.Groups[1].Value, assigns)}");
                continue;
            }

            var column = Regex.Match(t, @"^LINE\s+""(KC\d+)""\s+COLUMN\s+""([^""]+)""");
            if (column.Success && joints.TryGetValue(column.Groups[2].Value, out var at))
            {
                columns++;
                if (!StandsOnLinework(column.Groups[1].Value, at, colLines, wallLines, assigns, ReachFor(column.Groups[1].Value, false)))
                    unfounded.Add($"{column.Groups[1].Value} at ({at.X:F0},{at.Y:F0}) on {Where(column.Groups[1].Value, assigns)}");
            }
        }

        _out.WriteLine($"{name}: {walls} walls and {columns} columns checked against the sheets placed on their storeys; " +
                       $"{unfounded.Count} stand on nothing.");

        Assert.True(unfounded.Count == 0,
            $"{name}: {unfounded.Count} generated member(s) have no linework beneath them on any sheet placed on a " +
            $"storey they are assigned to — they were built somewhere the drawings do not show them: " +
            $"{string.Join("; ", unfounded.Take(6))}");
    }

    // ---------------------------------------------------------------------------------------
    // 3. drawn -> modelled.  Nothing was lost.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Every wall and column the classifier reads off a placed sheet must end up either as a
    /// generated member on one of that sheet's storeys, or already present in the engineer's own
    /// model at that place. What is left is geometry that was read and then lost, which appears in
    /// no count anywhere.
    ///
    /// A ratchet, not a target: the number may only ever come down. It stands at what the drawings
    /// genuinely cannot resolve — outlines that will not close, and linework the classifier reads
    /// as neither wall nor column.
    /// </summary>
    [Theory]
    [MemberData(nameof(Projects))]
    public void EveryDrawnMemberIsModelledOrAlreadyThere(string name)
    {
        var project = GeneratedModel.For(name);
        var built = GeneratedModel.BuildOrSkip(project);
        if (built is null) return;

        var joints = GeneratedModel.Joints(built.Lines);
        var assigns = GeneratedModel.AssignedStoreys(built.Lines);

        // Where the generated members ended up, by storey, as SEGMENTS. A wall is matched by
        // distance to its run rather than to its midpoint: the wall network carries a wall out to
        // the corner it crosses and splits it where another runs into it, so its midpoint moves
        // even though the wall the drawing shows is plainly modelled. Comparing midpoints reports
        // that as lost geometry.
        var modelled = new Dictionary<string, List<(DxfPoint A, DxfPoint B)>>(StringComparer.OrdinalIgnoreCase);
        void Put(string member, DxfPoint a, DxfPoint b)
        {
            if (!assigns.TryGetValue(member, out var storeys)) return;
            foreach (string s in storeys)
            {
                if (!modelled.TryGetValue(s, out var list)) modelled[s] = list = new List<(DxfPoint, DxfPoint)>();
                list.Add((a, b));
            }
        }

        foreach (string line in built.Lines)
        {
            string t = line.Trim();
            var wall = Regex.Match(t, @"^AREA\s+""(KW\d+)""\s+PANEL\s+4\s+""([^""]+)""\s+""([^""]+)""");
            if (wall.Success && joints.TryGetValue(wall.Groups[2].Value, out var a) && joints.TryGetValue(wall.Groups[3].Value, out var b))
            { Put(wall.Groups[1].Value, a, b); continue; }

            var column = Regex.Match(t, @"^LINE\s+""(KC\d+)""\s+COLUMN\s+""([^""]+)""");
            if (column.Success && joints.TryGetValue(column.Groups[2].Value, out var at))
                Put(column.Groups[1].Value, at, at);
        }

        // What the engineer's own model already carries, so a skipped member counts as accounted for.
        var existing = GeneratedModel.ExistingByStorey(project);

        var lost = new List<string>();
        int drawnTotal = 0;
        var (ox, oy) = built.Report.AppliedOffset;

        foreach (var sheet in built.Report.Sheets)
        {
            if (sheet.Stories.Count == 0) continue;
            var geometry = GeneratedModel.Classify(project, sheet.File);
            if (geometry is null) continue;

            var runs = new List<(DxfPoint A, DxfPoint B)>();
            foreach (string storey in sheet.Stories)
            {
                if (modelled.TryGetValue(storey, out var m)) runs.AddRange(m);
                if (existing.TryGetValue(storey, out var e)) runs.AddRange(e.Select(p => (p, p)));
            }

            bool Covered(DxfPoint p) => runs.Any(r => DistanceToSegment(p, r.A, r.B) <= Tolerance);

            foreach (var axis in geometry.Walls)
            {
                drawnTotal++;
                var mid = new DxfPoint((axis.Start.X + axis.End.X) / 2 + ox, (axis.Start.Y + axis.End.Y) / 2 + oy);
                if (!Covered(mid))
                    lost.Add($"wall at ({mid.X:F0},{mid.Y:F0}) from {sheet.Label}");
            }

            foreach (var col in geometry.Columns)
            {
                drawnTotal++;
                var at = new DxfPoint(col.Center.X + ox, col.Center.Y + oy);
                if (!Covered(at))
                    lost.Add($"column at ({at.X:F0},{at.Y:F0}) from {sheet.Label}");
            }
        }

        int allowed = name.StartsWith("31168") ? GeneratedModel.LangaraLostCeiling : GeneratedModel.WestFirstLostCeiling;
        _out.WriteLine($"{name}: {drawnTotal} drawn members, {lost.Count} not modelled and not already there (ceiling {allowed}).");
        foreach (string l in lost.Take(10)) _out.WriteLine("    " + l);

        Assert.True(lost.Count <= allowed,
            $"{name}: {lost.Count} drawn member(s) were read and then neither modelled nor found in the existing " +
            $"model, against {allowed} recorded. This number may only come down. First few: {string.Join("; ", lost.Take(5))}");
    }

    // ---------------------------------------------------------------------------------------
    // 4. The member is the RIGHT member.  Nothing was misclassified.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Position is not enough. A member can sit exactly where the drawing puts it and still be
    /// wrong: a 16" column built 24", a round column built square, a wall built at the wrong
    /// thickness. Every one of those leaves the counts and the positions perfect — which is how
    /// 160 chamfered square columns shipped as 10" circles and the engineer, not the build, found
    /// them.
    ///
    /// So each generated column is measured against the RAW linework underneath it — segments off
    /// the column layers, with the arc flag the DXF entity type gives — and not against the
    /// classifier's own idea of what it read. That distinction is the whole check: the first
    /// version of this compared the model to the classifier that built it, agreed with itself
    /// perfectly, and passed unchanged with round-column detection switched off altogether.
    /// </summary>
    [Theory]
    [MemberData(nameof(Projects))]
    public void EveryGeneratedMemberHasTheSizeItWasDrawnAt(string name)
    {
        var project = GeneratedModel.For(name);
        var built = GeneratedModel.BuildOrSkip(project);
        if (built is null) return;

        var sections = GeneratedModel.Sections(built.Lines);
        var joints = GeneratedModel.Joints(built.Lines);
        var assigns = GeneratedModel.AssignedStoreys(built.Lines);
        var sectionOf = GeneratedModel.SectionOfMember(built.Lines);
        var linework = GeneratedModel.ColumnLineworkByStorey(project, built.Report);

        var wrong = new List<string>();
        int checkedCount = 0;

        foreach (string line in built.Lines)
        {
            string t = line.Trim();

            var column = Regex.Match(t, @"^LINE\s+""(KC\d+)""\s+COLUMN\s+""([^""]+)""");
            if (!column.Success) continue;

            string member = column.Groups[1].Value;
            if (!joints.TryGetValue(column.Groups[2].Value, out var at)) continue;
            if (!sectionOf.TryGetValue(member, out string? section) || !sections.TryGetValue(section, out var built_)) continue;
            if (!assigns.TryGetValue(member, out var storeys)) continue;

            // The linework this column stands on: every column-layer segment whose ends both fall
            // inside the footprint the model gives it, widened by the tolerance.
            double reach = Math.Max(built_.D, built_.B) / 2.0 + Tolerance;
            var under = new List<DxfSegment>();
            foreach (string storey in storeys)
            {
                if (!linework.TryGetValue(storey, out var segments)) continue;
                foreach (var s in segments)
                    if (Inside(s.Start, at, reach) || Inside(s.End, at, reach))
                        under.Add(s);
            }
            if (under.Count == 0) continue;   // nothing drawn there is the other test's finding

            // Round is knowable only from arc provenance, so that is what is compared.
            bool drawnRound = under.Any(s => s.FromCurve);
            if (drawnRound != built_.IsRound)
            {
                wrong.Add($"{member} at ({at.X:F0},{at.Y:F0}) drawn with {(drawnRound ? "arcs" : "straight lines only")} " +
                          $"but built {(built_.IsRound ? "round" : "rectangular")} as '{section}'");
                continue;
            }

            double minX = under.Min(s => Math.Min(s.Start.X, s.End.X));
            double maxX = under.Max(s => Math.Max(s.Start.X, s.End.X));
            double minY = under.Min(s => Math.Min(s.Start.Y, s.End.Y));
            double maxY = under.Max(s => Math.Max(s.Start.Y, s.End.Y));

            double drawnLong = Math.Max(maxX - minX, maxY - minY);
            double drawnShort = Math.Min(maxX - minX, maxY - minY);

            // A footprint that reads as a sliver is one this window failed to capture whole, not a
            // sliver column. Judging it would report the window's own limits as defects.
            if (drawnShort < 4.0) continue;

            checkedCount++;
            double builtLong = built_.IsRound ? built_.D : Math.Max(built_.D, built_.B);
            double builtShort = built_.IsRound ? built_.D : Math.Min(built_.D, built_.B);

            // The drawn box is axis-aligned and the column may be turned inside it, so the only
            // sound bound is that the built section must fit within the box at SOME rotation:
            // its diagonal cannot exceed the box's. Comparing lengths side to side instead calls
            // an 8x43 turned 45 degrees oversize inside a 36x36 box, which it plainly is not.
            double builtDiagonal = Math.Sqrt(builtLong * builtLong + builtShort * builtShort);
            double drawnDiagonal = Math.Sqrt(drawnLong * drawnLong + drawnShort * drawnShort);

            if (builtDiagonal - drawnDiagonal > 2.0)
                wrong.Add($"{member} at ({at.X:F0},{at.Y:F0}) drawn inside {drawnShort:F0}x{drawnLong:F0} " +
                          $"but built {builtShort:F0}x{builtLong:F0} as '{section}', which does not fit");
        }

        int allowed = name.StartsWith("31168") ? GeneratedModel.LangaraMissizedCeiling : GeneratedModel.WestFirstMissizedCeiling;
        _out.WriteLine($"{name}: {checkedCount} columns matched to the footprint they were drawn from; " +
                       $"{wrong.Count} do not match it (ceiling {allowed}).");
        foreach (string w in wrong.Take(10)) _out.WriteLine("    " + w);

        Assert.True(wrong.Count <= allowed,
            $"{name}: {wrong.Count} generated column(s) are not the size or shape the drawing gives them, " +
            $"against {allowed} recorded. This number may only come down. First few: {string.Join("; ", wrong.Take(5))}");
    }

    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Was ANYTHING structural drawn where this member stands? Both layers count, because the
    /// classifier legitimately crosses between them — a short outline on the wall layer becomes a
    /// column by the engineer's own rule, and demanding column-layer linework under it calls a
    /// correct member an invention. Whether it is the RIGHT kind of member is the shape check's
    /// question, not this one.
    /// </summary>
    private static bool StandsOnLinework(string member, DxfPoint where,
        IReadOnlyDictionary<string, List<DxfSegment>> linework,
        IReadOnlyDictionary<string, List<DxfSegment>> alsoAcceptable,
        IReadOnlyDictionary<string, List<string>> assigns, double reach)
    {
        if (!assigns.TryGetValue(member, out var storeys)) return true;   // unassigned is another test's problem
        foreach (string storey in storeys)
        {
            foreach (var source in new[] { linework, alsoAcceptable })
            {
                if (!source.TryGetValue(storey, out var segments)) continue;
                foreach (var s in segments)
                    if (DistanceToSegment(where, s.Start, s.End) <= reach) return true;
            }
        }
        return false;
    }

    private static string Where(string member, IReadOnlyDictionary<string, List<string>> assigns) =>
        assigns.TryGetValue(member, out var s) ? string.Join("/", s) : "(unassigned)";

    private static DxfPoint Mid(DxfPoint a, DxfPoint b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    private static bool Near(DxfPoint a, DxfPoint b) =>
        Math.Abs(a.X - b.X) <= Tolerance && Math.Abs(a.Y - b.Y) <= Tolerance;

    /// <summary>Shortest distance from a point to a segment; a zero-length segment is a point.</summary>
    private static double DistanceToSegment(DxfPoint p, DxfPoint a, DxfPoint b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 1e-9) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));

        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0.0, 1.0);
        double qx = a.X + t * dx, qy = a.Y + t * dy;
        return Math.Sqrt((p.X - qx) * (p.X - qx) + (p.Y - qy) * (p.Y - qy));
    }

    private static bool Inside(DxfPoint p, DxfPoint centre, double reach) =>
        Math.Abs(p.X - centre.X) <= reach && Math.Abs(p.Y - centre.Y) <= reach;
}
