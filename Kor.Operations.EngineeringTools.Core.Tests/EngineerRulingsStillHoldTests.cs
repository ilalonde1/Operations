using Kor.Operations.EngineeringTools.Dxf;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// Rulings the engineer gave that the code appeared to obey and nothing checked.
///
/// RulingCoverageTests makes every banked ruling either proven by a named test or declared
/// unobeyed with a reason. Being declared is honest, but four of those entries said the same
/// thing — "it works, nothing asserts it" — and that is the state
/// solid-linework-belongs-to-the-storey-above was in while every model went out a storey low.
/// A rule that works by luck and a rule that works by design are the same colour until something
/// measures them.
///
/// These four are measured here. They are separate from ModelPlausibilityTests because they are
/// not plausibility: each one is a sentence an engineer said, checked against the file.
/// </summary>
public class EngineerRulingsStillHoldTests
{
    private readonly ITestOutputHelper _out;

    public EngineerRulingsStillHoldTests(ITestOutputHelper output) => _out = output;

    /// <summary>Both reference jobs, the same pair every other shipped-model test runs on.</summary>
    public static TheoryData<string> Projects => GeneratedModel.Projects;

    /// <summary>
    /// "all walls should be assigned a pier label" — hers, banked as pier-label-every-wall.
    ///
    /// dxf.assign-pier-labels was on and labels were written, which is not the same claim. A wall
    /// without one is invisible in a count and wrong in the model: ETABS reports a pier's forces
    /// up the building, and a panel outside every pier reports nothing, so the wall she asked
    /// about is the one with no answer.
    /// </summary>
    [Theory]
    [MemberData(nameof(Projects))]
    public void EveryGeneratedWallCarriesAPierLabel(string projectName)
    {
        var project = GeneratedModel.For(projectName);
        var built = GeneratedModel.BuildOrSkip(project);
        if (built is null) { _out.WriteLine("SKIPPED: share unreachable."); return; }

        var walls = new List<string>();
        var labelled = new HashSet<string>(StringComparer.Ordinal);

        foreach (string line in built.Lines)
        {
            string t = line.Trim();

            // The e2k separates its tokens with TWO spaces, and matching on one found nothing:
            // the first run of this test reported 0 of 1,388 walls labelled against a file that
            // labels every one. A test that reads the format wrongly accuses the code.
            string[] token = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (token.Length < 2) continue;

            string name = token[1].Trim('"');
            if (!name.StartsWith("KW", StringComparison.Ordinal)) continue;

            // A generated wall panel. Headers (KS) are spandrels and take spandrel labels.
            if (token[0] == "AREA" && t.Contains("PANEL", StringComparison.Ordinal))
                walls.Add(name);

            if (token[0] == "AREAASSIGN" && token.Contains("PIER"))
                labelled.Add(name);
        }

        var bare = walls.Where(w => !labelled.Contains(w)).ToList();

        _out.WriteLine($"{projectName}: {walls.Count} generated wall panel(s), {labelled.Count} carry a pier label.");

        Assert.NotEmpty(walls);
        Assert.True(bare.Count == 0,
            $"{bare.Count} generated wall panel(s) carry no pier label, and she asked that every wall " +
            $"have one: {string.Join(", ", bare.Take(12))}");
    }

    /// <summary>
    /// Hatch is fill, not structure — migration 044, banked and never asserted.
    ///
    /// The first version of this test simulated hatch as LINEWORK on a wall layer and failed,
    /// which proved nothing: forty diagonal strokes on a structural layer are genuinely
    /// ambiguous, and no reader should be asked to tell them from thin walls. Hatch in a DXF is
    /// a HATCH entity, and the claim worth making is about that.
    ///
    /// The reader was safe only by omission — no hatch layer on either reference job happened to
    /// match a structural pattern. That is a property of two jobs' layer names rather than of the
    /// reader, and the day a firm's hatch sits on a matching layer, every filled region becomes
    /// concrete and the counts look BETTER rather than worse.
    /// </summary>
    [Fact]
    public void HatchOnAStructuralLayerContributesNoGeometry()
    {
        string[] dxf =
        [
            "0", "SECTION", "2", "ENTITIES",

            // A real wall, so the fixture cannot pass by reading nothing at all.
            "0", "LWPOLYLINE", "8", "JBP_V-WALL", "90", "4", "70", "1",
            "10", "0", "20", "0", "10", "600", "20", "0",
            "10", "600", "20", "12", "10", "0", "20", "12",

            // And fill, on the same structural layer.
            "0", "HATCH", "8", "JBP_V-WALL",
            "0", "HATCH", "8", "JBP_V-WALL",
            "0", "ENDSEC", "0", "EOF",
        ];

        var options = new PlanClassificationOptions
        {
            WallLayerPatterns = new[] { "JBP_V-WALL" },
            ColumnLayerPatterns = new[] { "JBP_V_COL" },
            SlabLayerPatterns = new[] { "JBP_C_SLABEDG" },
        };

        var segments = DxfPlanReader.ReadSegments(dxf);
        var set = StructuralPlanClassifier.Classify(segments, options);

        _out.WriteLine($"segments {segments.Count}, walls {set.Walls.Count}, columns {set.Columns.Count}");

        // The wall is read; the hatch adds not one segment to it.
        Assert.Equal(4, segments.Count);
        Assert.Single(set.Walls);
        Assert.Empty(set.Columns);

        // And it is not reported as shape the model might be missing — she ruled that out on
        // 2026-08-21, "Almost positive hatching is always fill".
        var unread = DxfPlanReader.UnsupportedStructuralEntities(dxf, options);
        Assert.Contains(unread, e => e.EntityType.Equals("HATCH", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// "the gap between two perimeter columns is not a door" — perimeter-column-layer-openings,
    /// banked and marked Unverified.
    ///
    /// Cutting those gaps puts holes along a facade the drawings never showed, and an opening in
    /// a model is not a cosmetic error: it is where the tool also puts a header.
    /// </summary>
    [Fact]
    public void TheGapBetweenTwoPerimeterColumnsIsNotCutAsAnOpening()
    {
        var segments = new List<DxfSegment>();

        // A row of five square columns on a COLUMN layer, a bay apart — a facade, not a wall
        // with doorways in it.
        for (int i = 0; i < 5; i++)
        {
            double x = i * 240.0;
            segments.Add(new DxfSegment("JBP_V_COL", new DxfPoint(x, 0), new DxfPoint(x + 24, 0)));
            segments.Add(new DxfSegment("JBP_V_COL", new DxfPoint(x + 24, 0), new DxfPoint(x + 24, 24)));
            segments.Add(new DxfSegment("JBP_V_COL", new DxfPoint(x + 24, 24), new DxfPoint(x, 24)));
            segments.Add(new DxfSegment("JBP_V_COL", new DxfPoint(x, 24), new DxfPoint(x, 0)));
        }

        var options = new PlanClassificationOptions
        {
            WallLayerPatterns = new[] { "JBP_V-WALL" },
            ColumnLayerPatterns = new[] { "JBP_V_COL" },
            SlabLayerPatterns = new[] { "JBP_C_SLABEDG" },
        };

        var set = StructuralPlanClassifier.Classify(segments, options);
        _out.WriteLine($"columns {set.Columns.Count}, walls {set.Walls.Count}, openings {set.Openings.Count}");

        Assert.Equal(5, set.Columns.Count);
        Assert.Empty(set.Openings);
    }

    /// <summary>
    /// A layer this tool does not recognise contributes nothing —
    /// layers-that-are-not-structure.
    ///
    /// The banked reason for leaving this unproven was exact: "nothing proves a non-structural
    /// layer is REFUSED rather than merely ABSENT from the pattern list." Those differ the day a
    /// firm names a furniture layer something the patterns match, and the difference is a model
    /// full of desks.
    /// </summary>
    [Fact]
    public void GeometryOnAnUnrecognisedLayerIsNotModelled()
    {
        var segments = new List<DxfSegment>
        {
            new("JBP_V-WALL", new DxfPoint(0, 0), new DxfPoint(600, 0)),
            new("JBP_V-WALL", new DxfPoint(600, 0), new DxfPoint(600, 12)),
            new("JBP_V-WALL", new DxfPoint(600, 12), new DxfPoint(0, 12)),
            new("JBP_V-WALL", new DxfPoint(0, 12), new DxfPoint(0, 0)),

            // A closed rectangle of exactly wall proportions, on a layer nobody named.
            new("A-FURN", new DxfPoint(0, 400), new DxfPoint(600, 400)),
            new("A-FURN", new DxfPoint(600, 400), new DxfPoint(600, 412)),
            new("A-FURN", new DxfPoint(600, 412), new DxfPoint(0, 412)),
            new("A-FURN", new DxfPoint(0, 412), new DxfPoint(0, 400)),
        };

        var options = new PlanClassificationOptions
        {
            WallLayerPatterns = new[] { "JBP_V-WALL" },
            ColumnLayerPatterns = new[] { "JBP_V_COL" },
            SlabLayerPatterns = new[] { "JBP_C_SLABEDG" },
        };

        var set = StructuralPlanClassifier.Classify(segments, options);
        foreach (string f in set.Flags) _out.WriteLine(f);
        _out.WriteLine($"walls {set.Walls.Count}, columns {set.Columns.Count}, slabs {set.Slabs.Count}");

        // One wall — the one on a layer the rules name. Nothing from A-FURN, in any role.
        Assert.Single(set.Walls);
        Assert.Empty(set.Columns);
        Assert.Empty(set.Slabs);
    }

    private static string Between(string s, string start, string end)
    {
        int a = s.IndexOf(start, StringComparison.Ordinal);
        if (a < 0) return string.Empty;
        a += start.Length;
        int b = s.IndexOf(end, a, StringComparison.Ordinal);
        return b < 0 ? string.Empty : s[a..b];
    }
}
