using System.Globalization;
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// The stack merge renames. It must change NOTHING ELSE — and this builds the same job both ways
/// and diffs everything that is not a name.
/// </summary>
/// <remarks>
/// WHY THIS EXISTS, AND WHY IT IS ONE TEST RATHER THAN SIX.
///
/// The merge gives a member one label its whole height, which is the engineer's own convention. It
/// also breaks every reader that keys on an OBJECT NAME, because one object now holds members drawn
/// on several sheets, standing on several storeys, carrying several sections. That fault was found
/// EIGHT SEPARATE TIMES over two days, one symptom at a time:
///
///   the report and dossier counts        counted objects, called them "wall panels"
///   the publish gate                     refused a correct model: 744 columns, 238 rows
///   the coverage audit                   read one section per member
///   the benchmark against her own model  the same, on both models
///   the plausibility heights             spanned lowest to highest assign, so a wall read 454ft
///   the sheet ledger                     credited one sheet with 32 storeys
///   the baseline structure counts        counted labels
///   building attribution                 the merge ran BEFORE the building cut, so the cut hunted
///                                        names nothing was assigned to any more and 2,591 of the
///                                        towers' members rode into the YMCA's model
///
/// Every one of those was green until somebody looked. Finding the ninth by looking is not a plan.
///
/// So: build the job with the merge on and off, and assert that everything the ENGINEER can see is
/// identical. Object counts and object names are expected to differ and are the only things that
/// may. Whatever the next name-keyed reader turns out to be, it fails here on the run that
/// introduces it rather than in her model.
/// </remarks>
[Trait("Speed", "Slow")]
public class StackMergeChangesNothingButLabelsTests
{
    private readonly ITestOutputHelper _out;

    public StackMergeChangesNothingButLabelsTests(ITestOutputHelper output) => _out = output;

    public static TheoryData<string> Projects => GeneratedModel.Projects;

    private sealed record Built(string[] Lines, DxfToEtabsReport Report);

    private static Built? BuildOrSkip(GeneratedModel.Project project, bool merge)
    {
        if (!Directory.Exists(project.DxfFolder) || !File.Exists(project.Reference)) return null;

        string output = Path.Combine(Path.GetTempPath(), $"kor-mergediff-{Guid.NewGuid():N}.e2k");
        try
        {
            var report = DxfToEtabsService.Run(new DxfToEtabsRequest
            {
                RequireRuleSettings = true,
                DxfFolder = DrawingCache.Local(project.DxfFolder),
                ReferenceE2k = project.Reference,
                OutputE2k = output,
                MergeStacksIntoOneLabel = merge,
            });
            return new Built(File.ReadAllLines(output), report);
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    /// <summary>Every (kind, plan position, storey, section) a model puts in the building.</summary>
    private static Dictionary<string, int> Members(string[] lines)
    {
        var doc = E2kDocument.Parse(lines);
        var where = doc.PlanPointsOfObjects();
        var kinds = doc.ReadContents().Objects.ToDictionary(o => o.Name, o => o.Kind, StringComparer.Ordinal);

        var assign = new Regex(@"^\s*(?:AREA|LINE)ASSIGN\s+""([^""]+)""\s+""([^""]+)""(?:.*?SECTION\s+""([^""]+)"")?");
        var counted = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (string raw in lines)
        {
            var m = assign.Match(raw);
            if (!m.Success) continue;

            string name = m.Groups[1].Value;
            if (!name.StartsWith("K", StringComparison.Ordinal)) continue;   // hers, carried through
            if (!where.TryGetValue(name, out var points) || points.Count == 0) continue;

            kinds.TryGetValue(name, out string? kind);
            string at = string.Join("|", points
                .Select(p => $"{p.X.ToString("0.###", CultureInfo.InvariantCulture)},{p.Y.ToString("0.###", CultureInfo.InvariantCulture)}")
                .OrderBy(x => x, StringComparer.Ordinal));

            string key = $"{kind}|{at}|{m.Groups[2].Value}|{m.Groups[3].Value}";
            counted[key] = counted.TryGetValue(key, out int had) ? had + 1 : 1;
        }

        return counted;
    }

    private static string Difference(Dictionary<string, int> a, Dictionary<string, int> b, int show = 6)
    {
        var moved = a.Keys.Concat(b.Keys).Distinct(StringComparer.Ordinal)
            .Select(k => (Key: k, A: a.GetValueOrDefault(k), B: b.GetValueOrDefault(k)))
            .Where(x => x.A != x.B)
            .ToList();

        if (moved.Count == 0) return string.Empty;

        var said = moved.Take(show).Select(x => $"  {x.Key}   unmerged {x.A}, merged {x.B}");
        return $"{moved.Count} member placement(s) differ:\n{string.Join("\n", said)}";
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void TheBuildingIsTheSameWhicheverWayItIsLabelled(string name)
    {
        var project = GeneratedModel.For(name);
        var plain = BuildOrSkip(project, merge: false);
        var merged = BuildOrSkip(project, merge: true);
        if (plain is null || merged is null) return;   // share unreachable

        var a = plain.Report.Summary;
        var b = merged.Report.Summary;

        _out.WriteLine($"{name}: unmerged {a.Walls} walls / {a.Columns} columns / {a.Floors} slabs, " +
                       $"merged {b.Walls} / {b.Columns} / {b.Floors}");

        // 1. THE MEMBERS. Every (kind, position, storey, section) the engineer receives, with
        //    multiplicity. This is the whole building; a rename cannot touch it.
        string moved = Difference(Members(plain.Lines), Members(merged.Lines));
        Assert.True(moved.Length == 0, $"{name}: the merge moved the building.\n{moved}");

        // 2. THE COUNTS SHE READS. Report, summary page and dossier all state these, and they must
        //    describe the building rather than the labels it is carried on.
        Assert.Equal(a.Walls, b.Walls);
        Assert.Equal(a.Columns, b.Columns);
        Assert.Equal(a.Floors, b.Floors);
        Assert.Equal(a.Stories, b.Stories);

        // 3. WHOSE MEMBERS THEY ARE. The building cut reads attribution keyed by object name, so a
        //    rename before it ran let 2,591 of the towers' members into the YMCA's model while
        //    every one of them sat exactly where it had always been.
        Assert.Equal(Foreign(plain.Report), Foreign(merged.Report));

        // 4. WHICH DRAWING EACH STOREY CAME FROM. She reads this ledger, and the coverage audit
        //    uses it to decide which sheet to check a member against; one sheet was credited with
        //    thirty-two storeys.
        Assert.Equal(SheetPlacement(plain.Report), SheetPlacement(merged.Report));
    }

    /// <summary>How many members the building cut removed as belonging to somebody else.</summary>
    private static int Foreign(DxfToEtabsReport report)
    {
        var said = new Regex(@"([\d,]+) member\(s\) drawn for another building were removed");
        foreach (string flag in report.Summary.Flags.Concat(report.Warnings))
        {
            var m = said.Match(flag);
            if (m.Success) return int.Parse(m.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture);
        }
        return 0;
    }

    private static string SheetPlacement(DxfToEtabsReport report)
        => string.Join("\n", report.Sheets
            .Where(s => s.Stories.Count > 0)
            .OrderBy(s => s.File, StringComparer.OrdinalIgnoreCase)
            .Select(s => $"{s.File} -> {string.Join(",", s.Stories.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}"));
}
