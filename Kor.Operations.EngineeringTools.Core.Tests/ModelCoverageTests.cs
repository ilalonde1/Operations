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
    /// Every generated wall and column must stand on linework from a sheet placed on one of the
    /// storeys it is assigned to. A member with no drawing under it came from somewhere else —
    /// the wrong storey, or a rule that fired where it should not have.
    /// </summary>
    [Theory]
    [MemberData(nameof(Projects))]
    public void EveryGeneratedMemberStandsOnLineworkFromItsOwnStorey(string name)
    {
        var project = GeneratedModel.For(name);
        var built = GeneratedModel.BuildOrSkip(project);
        if (built is null) return;

        var drawn = GeneratedModel.DrawnByStorey(project, built.Report);
        var joints = GeneratedModel.Joints(built.Lines);
        var assigns = GeneratedModel.AssignedStoreys(built.Lines);

        var unfounded = new List<string>();
        int walls = 0, columns = 0;

        foreach (string line in built.Lines)
        {
            string t = line.Trim();

            var wall = Regex.Match(t, @"^AREA\s+""(KW\d+)""\s+PANEL\s+4\s+""([^""]+)""\s+""([^""]+)""");
            if (wall.Success && joints.TryGetValue(wall.Groups[2].Value, out var a) && joints.TryGetValue(wall.Groups[3].Value, out var b))
            {
                walls++;
                if (!Stands(wall.Groups[1].Value, Mid(a, b), drawn, assigns, wallLike: true))
                    unfounded.Add($"{wall.Groups[1].Value} at ({Mid(a, b).X:F0},{Mid(a, b).Y:F0}) on {Where(wall.Groups[1].Value, assigns)}");
                continue;
            }

            var column = Regex.Match(t, @"^LINE\s+""(KC\d+)""\s+COLUMN\s+""([^""]+)""");
            if (column.Success && joints.TryGetValue(column.Groups[2].Value, out var at))
            {
                columns++;
                if (!Stands(column.Groups[1].Value, at, drawn, assigns, wallLike: false))
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

        // Where the generated members ended up, by storey.
        var modelled = new Dictionary<string, List<DxfPoint>>(StringComparer.OrdinalIgnoreCase);
        void Put(string member, DxfPoint where)
        {
            if (!assigns.TryGetValue(member, out var storeys)) return;
            foreach (string s in storeys)
            {
                if (!modelled.TryGetValue(s, out var list)) modelled[s] = list = new List<DxfPoint>();
                list.Add(where);
            }
        }

        foreach (string line in built.Lines)
        {
            string t = line.Trim();
            var wall = Regex.Match(t, @"^AREA\s+""(KW\d+)""\s+PANEL\s+4\s+""([^""]+)""\s+""([^""]+)""");
            if (wall.Success && joints.TryGetValue(wall.Groups[2].Value, out var a) && joints.TryGetValue(wall.Groups[3].Value, out var b))
            { Put(wall.Groups[1].Value, Mid(a, b)); continue; }

            var column = Regex.Match(t, @"^LINE\s+""(KC\d+)""\s+COLUMN\s+""([^""]+)""");
            if (column.Success && joints.TryGetValue(column.Groups[2].Value, out var at))
                Put(column.Groups[1].Value, at);
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

            var candidates = new List<DxfPoint>();
            foreach (string storey in sheet.Stories)
            {
                if (modelled.TryGetValue(storey, out var m)) candidates.AddRange(m);
                if (existing.TryGetValue(storey, out var e)) candidates.AddRange(e);
            }

            foreach (var axis in geometry.Walls)
            {
                drawnTotal++;
                var mid = new DxfPoint((axis.Start.X + axis.End.X) / 2 + ox, (axis.Start.Y + axis.End.Y) / 2 + oy);
                if (!candidates.Any(c => Near(c, mid)))
                    lost.Add($"wall at ({mid.X:F0},{mid.Y:F0}) from {sheet.Label}");
            }

            foreach (var col in geometry.Columns)
            {
                drawnTotal++;
                var at = new DxfPoint(col.Center.X + ox, col.Center.Y + oy);
                if (!candidates.Any(c => Near(c, at)))
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

    private static bool Stands(string member, DxfPoint where,
        IReadOnlyDictionary<string, List<DxfPoint>> drawn,
        IReadOnlyDictionary<string, List<string>> assigns, bool wallLike)
    {
        if (!assigns.TryGetValue(member, out var storeys)) return true;   // unassigned is another test's problem
        foreach (string storey in storeys)
            if (drawn.TryGetValue(storey, out var points) && points.Any(p => Near(p, where)))
                return true;
        return false;
    }

    private static string Where(string member, IReadOnlyDictionary<string, List<string>> assigns) =>
        assigns.TryGetValue(member, out var s) ? string.Join("/", s) : "(unassigned)";

    private static DxfPoint Mid(DxfPoint a, DxfPoint b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    private static bool Near(DxfPoint a, DxfPoint b) =>
        Math.Abs(a.X - b.X) <= Tolerance && Math.Abs(a.Y - b.Y) <= Tolerance;
}
