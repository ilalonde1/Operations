#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;
using GP = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.GeomPath;
using PC = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.PageContent;
using TT = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.TextToken;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A wall is what its tag says it is (intake step 33). On a set with an assembly schedule the plans
/// tag their walls with its codes; a wall takes the nearest tag within reach of its axis, the code's
/// material comes from the card, and a partition goes to the DXF's KOR_PARTITION layer, which the
/// model does not read. A wall with no tag near it is modelled as drawn and counted as untagged.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a tag beside a wall typing it; the nearer of two tags; a tag out of reach
/// leaving the wall untagged; a stud code making a partition and a concrete code not; the tags
/// themselves kept on the geometry for the DXF; (step 34) a tag naming its whole run through the
/// doorways, and an untagged wall on a tagging sheet being no wall while on a sparse sheet it is
/// modelled as drawn. WHAT IT DOES NOT: the real sheets (31170: 1,673
/// tags, 1,174 walls typed, 1,046 partitions, 1,856 untagged, 2026-09-10); a tag on another sheet
/// (the 1/8" floor plan's walls are untagged and the enlargement carries the tags — a spatial
/// stand-down between sheets is step 34's); a tag reached only through a leader; the DXF layer the
/// exporter writes (`DxfExporter`, not banked here).
/// </remarks>
public sealed class AWallIsWhatItsTagSaysItIsTests
{
    private const double W = 3024, H = 2160;
    private const int Scale = 96;
    private static double Pt(double mm) => mm / (Scale * PdfToSafeConstants.PointsToMm);   // mm on the plan → PDF points
    private static TT Tok(string text, double xMm, double yMm) => new(text, Pt(xMm), Pt(yMm), Pt(xMm) - 8, Pt(yMm) - 4, Pt(xMm) + 8, Pt(yMm) + 4);

    private static readonly IReadOnlyList<AssemblySchedule.Assembly> Legend =
    [
        new("WALL", "C12", "12\" C.I.P WALL", ["305mm CONCRETE WALL"], new Dictionary<string, string>(), [], [], AssemblySchedule.Material.Concrete, 305, 3),
        new("WALL", "S8.1", "STEEL STUD PARTY WALL", ["STEEL STUDS"], new Dictionary<string, string>(), [], [], AssemblySchedule.Material.Stud, 41, 3),
    ];

    private static ExtractedGeometry Walls(params ((double X, double Y) Start, (double X, double Y) End)[] axes)
    {
        var g = new ExtractedGeometry { ScaleDenominator = Scale };
        foreach (var (s, e) in axes)
            g.Walls.Add(new WallPanel([s, e, (e.X, e.Y + 200), (s.X, s.Y + 200)], s, e, 200));
        return g;
    }

    private static SheetFurniture.Set NoFurniture(PC page) => SheetFurniture.On(page, 0);

    [Fact]
    public void AWallTakesTheTagBesideItAndAPartitionIsNotStructure()
    {
        var g = Walls(((0, 0), (6000, 0)), ((0, 5000), (6000, 5000)));
        var page = new PC(1, W, H, new List<TT> { Tok("S8.1", 3000, 400), Tok("C12", 3000, 5400) }, new List<GP>());
        WallTypeTagging.Apply(g, page, NoFurniture(page), Legend);

        Assert.Equal(["S8.1", "C12"], g.WallTypeCodes);
        Assert.Equal([true, false], g.WallIsPartition);
        Assert.Equal(2, g.WallTypeTags.Count);
    }

    [Fact]
    public void TheNearerOfTwoTagsWins()
    {
        var g = Walls(((0, 0), (6000, 0)));
        var page = new PC(1, W, H, new List<TT> { Tok("C12", 3000, 300), Tok("S8.1", 3000, 1000) }, new List<GP>());
        WallTypeTagging.Apply(g, page, NoFurniture(page), Legend);
        Assert.Equal("C12", g.WallTypeCodes[0]);
        Assert.False(g.WallIsPartition[0]);
    }

    [Fact]
    public void ATagOutOfReachLeavesTheWallUntaggedAndModelled()
    {
        var g = Walls(((0, 0), (6000, 0)));
        var page = new PC(1, W, H, new List<TT> { Tok("S8.1", 3000, WallTypeTagging.ReachMm + 500) }, new List<GP>());
        WallTypeTagging.Apply(g, page, NoFurniture(page), Legend);
        Assert.Null(g.WallTypeCodes[0]);
        Assert.False(g.WallIsPartition[0]);
        Assert.Single(g.WallTypeTags);                                          // the tag is still kept for the DXF
    }

    /// <summary>A tag names its whole run: a pier on the same line as a typed neighbour, abutting it, takes the type (step 34).</summary>
    [Fact]
    public void ATagNamesItsWholeRun()
    {
        // one wall drawn as three piers along y = 0, tagged once beside the first
        var g = Walls(((0, 0), (2000, 0)), ((2100, 0), (4000, 0)), ((4100, 0), (6000, 0)), ((0, 5000), (6000, 5000)));
        var tags = new List<TT>();
        for (int i = 0; i < WallTypeTagging.TaggingSheetMinTags; i++) tags.Add(Tok("S8.1", 1000 + i * 40, 400));   // a sheet that tags its walls
        var page = new PC(1, W, H, tags, new List<GP>());
        WallTypeTagging.Apply(g, page, NoFurniture(page), Legend);
        Assert.Equal("S8.1", g.WallTypeCodes[0]);
        Assert.Equal("S8.1", g.WallTypeCodes[1]);                              // through the doorway
        Assert.Equal("S8.1", g.WallTypeCodes[2]);                              // and the next
        Assert.Null(g.WallTypeCodes[3]);                                        // the wall on another line is not in the run
    }

    /// <summary>On a plan that tags its walls, a wall with no tag is not a wall; on one that does not, it is modelled as drawn (step 34).</summary>
    [Fact]
    public void OnATaggingSheetAnUntaggedWallIsNotAWall()
    {
        var g = Walls(((0, 0), (6000, 0)), ((0, 5000), (6000, 5000)));
        var tags = new List<TT> { Tok("C12", 3000, 400) };
        for (int i = 0; i < WallTypeTagging.TaggingSheetMinTags; i++) tags.Add(Tok("S8.1", 20000 + i * 40, 20000));   // tags far away, so the sheet tags
        var page = new PC(1, W, H, tags, new List<GP>());
        WallTypeTagging.Apply(g, page, NoFurniture(page), Legend);
        Assert.Equal("C12", g.WallTypeCodes[0]); Assert.False(g.WallIsPartition[0]);   // concrete, modelled
        Assert.Null(g.WallTypeCodes[1]);          Assert.True(g.WallIsPartition[1]);    // untagged on a tagging sheet: not a wall

        var few = Walls(((0, 0), (6000, 0)), ((0, 5000), (6000, 5000)));
        var sparse = new PC(1, W, H, new List<TT> { Tok("C12", 3000, 400) }, new List<GP>());
        WallTypeTagging.Apply(few, sparse, NoFurniture(sparse), Legend);
        Assert.Null(few.WallTypeCodes[1]);        Assert.False(few.WallIsPartition[1]);  // a sheet that does not tag: modelled as drawn
    }

    [Fact]
    public void WithNoLegendEveryWallIsUntagged()
    {
        var g = Walls(((0, 0), (6000, 0)));
        var page = new PC(1, W, H, new List<TT> { Tok("S8.1", 3000, 400) }, new List<GP>());
        WallTypeTagging.Apply(g, page, NoFurniture(page), []);
        Assert.Equal([null], g.WallTypeCodes);
        Assert.Empty(g.WallTypeTags);
    }
}
