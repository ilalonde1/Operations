#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A wall is what the cut pen encloses (intake step 20): two lines in the sheet's cut pen — the
/// pen the lines along its filled walls' edges are drawn with — parallel within a degree, a wall's
/// thickness apart, overlapping a wall's length, with no third cut-pen line between them or at the
/// same spacing beyond either, nothing drawn across them between their ends, and not under a wall
/// or column already read. The wall is the overlap, the thickness is the gap, and both lines are
/// read as its faces, not emitted as beams.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a pair becoming one wall of the right thickness, length and outline; the two
/// lines' fates (BecameWallFace, read, indexed to the wall) and their exclusion from the exporter's
/// lines; the cut pen taken from the filled walls' edge lines, and a sheet with no filled wall
/// reading no face wall; a pair in a lighter pen (a rebar extent box); a lighter line between two
/// cut faces (a tapered wall's batter) still a wall; a third cut-pen line beyond at the same
/// spacing (a hatch) and a third between; a pair under a filled wall (its own edges) and under a
/// column (its outline drawn as lines); risers across the pair (a stair) and an X corner to corner
/// (a shaft), and a single end cap that is neither; faces of unequal length reading as the overlap;
/// a gap outside wall thicknesses; a pair turned 30°. WHAT IT DOES NOT: a real sheet
/// (FiveStickFilesTests banks the counts, the census the DXF side, pdf-vs-dxf the model); a wall
/// whose second face is drawn lighter than the cut pen (31168 p11's property-line wall, 984" x 23",
/// filled and tapered — the filled-wall rule's counter-example, not this rule's); a curb 4" wide
/// drawn with the cut pen, which this reads as a 4" wall; a hatched wall, whose diagonal hatch is
/// allowed across it but was not measured on these five sets.
/// </remarks>
public sealed class TwoFaceLinesAWallsThicknessApartAreAWallTests
{
    private const double T = 12 * 25.4, L = 240 * 25.4;
    private const double Pen = 0.5;                   // FateFixture.Line's width: the fixture sheet's cut pen

    private static RawSubpath Face(double x0, double y0, double x1, double y1, double pen = Pen)
        => FateFixture.Line(x0, y0, x1, y1) with { LineWidth = pen };

    /// <summary>A filled wall at the origin with its two edge lines in the cut pen: what gives the sheet a cut pen.</summary>
    private static IEnumerable<RawSubpath> FilledWall()
    {
        yield return WallFixture.Rect(thicknessIn: 12, lengthIn: 240);                // 0..L along x, 0..T across y
        yield return Face(0, 0, L, 0);
        yield return Face(0, T, L, T);
    }

    private static (ExtractedGeometry Geometry, List<PathFate> Fates) Read(params RawSubpath[] more)
        => WallFixture.Read(FilledWall().Concat(more).ToList());

    private static WallPanel FaceWall(ExtractedGeometry g) => Assert.Single(g.Walls.Skip(1));

    [Fact]
    public void APairInTheCutPenIsOneWallOfTheGapsThicknessOverTheOverlap()
    {
        var (g, fates) = Read(Face(1000, 5000, 1000 + L, 5000), Face(1000, 5000 + T, 1000 + L, 5000 + T));
        Assert.Equal(2, g.Walls.Count);                                          // the filled one and the read one
        var wall = FaceWall(g);
        Assert.Equal(T, wall.ThicknessMm, 1);
        Assert.Equal(L, Length(wall), 1);
        Assert.Equal(4, wall.Outline.Count);
        Assert.Equal(5000 + T / 2, wall.Start.Y, 1);
        Assert.Equal(2, g.WallFaceLines.Count);
        Assert.All(g.WallFaceLines.Values, v => Assert.Equal(1, v));
        Assert.Equal(4, g.Lines.Count);                                          // still lines; the exporter skips the faces
        var faces = fates.Where(f => f.Reason == PathReason.BecameWallFace).ToList();
        Assert.Equal(2, faces.Count);
        Assert.All(faces, f => Assert.Equal(Disposition.Read, f.Disposition));
        Assert.All(faces, f => Assert.Equal(1, f.ObjectIndex));
        Assert.Equal(5, fates.Count);
        Assert.Equal(Pen, GeometryFilterService.CutPen(g));
    }

    [Fact]
    public void TheExporterWritesTheWallsAndNotTheFaces()
    {
        var (g, _) = Read(Face(1000, 5000, 1000 + L, 5000), Face(1000, 5000 + T, 1000 + L, 5000 + T));
        string path = Path.GetTempFileName();
        try
        {
            DxfExporter.Export(g, path, korLayers: true);
            var segments = DxfPlanReader.ReadSegments(path);
            Assert.Equal(8, segments.Count(s => s.Layer == "KOR_V-WALL"));      // two outlines
            Assert.Equal(2, segments.Count(s => s.Layer != "KOR_V-WALL"));      // the filled wall's edge lines, beams still
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ASheetWithNoFilledWallHasNoCutPenAndReadsNoFaceWall()
    {
        var (g, fates) = WallFixture.Read([Face(1000, 5000, 1000 + L, 5000), Face(1000, 5000 + T, 1000 + L, 5000 + T)]);
        Assert.Empty(g.Walls);
        Assert.Equal(0, GeometryFilterService.CutPen(g));
        Assert.Equal(2, fates.Count(f => f.Reason == PathReason.EmittedAsLine));
    }

    [Fact]
    public void APairInALighterPenIsARebarExtentNotAWall()
    {
        var (g, _) = Read(Face(1000, 5000, 1000 + L, 5000, pen: 0.2), Face(1000, 5000 + T, 1000 + L, 5000 + T, pen: 0.2));
        Assert.Single(g.Walls);
        Assert.Empty(g.WallFaceLines);
    }

    [Fact]
    public void OneLighterLineBetweenTwoCutFacesIsTheWallsOwnBatter()
    {
        var (g, _) = Read(Face(1000, 5000, 1000 + L, 5000), Face(1000, 5000 + T, 1000 + L, 5000 + T),
                          Face(1000, 5000 + T / 3, 1000 + L, 5000 + T / 3, pen: 0.2));
        var wall = FaceWall(g);
        Assert.Equal(T, wall.ThicknessMm, 1);
    }

    [Fact]
    public void SeveralLighterLinesBetweenTheFacesAreSomethingDrawnThereNotAWall()
    {
        // 31138 p9's flights: 45" between the cut lines, a dozen 15" segments in a lighter pen between them
        double w = 45 * 25.4;
        var paths = new List<RawSubpath> { Face(1000, 5000, 1000 + L, 5000), Face(1000, 5000 + w, 1000 + L, 5000 + w) };
        for (double x = 1500; x < 1000 + L - 400; x += 600)
            paths.Add(FateFixture.Line(x, 5000 + w / 3, x + 380, 5000 + w / 3) with { LineWidth = 0.2 });
        var (g, _) = Read(paths.ToArray());
        Assert.Single(g.Walls);
    }

    [Fact]
    public void AThirdCutLineAtTheSameSpacingBeyondIsAHatchNotAWall()
    {
        var (g, fates) = Read(
            Face(1000, 5000, 1000 + L, 5000), Face(1000, 5000 + T, 1000 + L, 5000 + T), Face(1000, 5000 + 2 * T, 1000 + L, 5000 + 2 * T));
        Assert.Single(g.Walls);
        Assert.Equal(5, fates.Count(f => f.Reason == PathReason.EmittedAsLine));
    }

    [Fact]
    public void AThirdCutLineBetweenTheFacesMeansTheyAreNotAdjacent()
    {
        var (g, _) = Read(
            Face(1000, 5000, 1000 + L, 5000), Face(1000, 5000 + T, 1000 + L, 5000 + T), Face(1000, 5000 + T / 2, 1000 + L, 5000 + T / 2));
        // the outer pair has a cut line between; the inner pairs are 6" apart, thinner than a wall
        Assert.Single(g.Walls);
    }

    [Fact]
    public void AFilledWallsOwnEdgesAreNotASecondWall()
    {
        var (g, fates) = Read();
        Assert.Single(g.Walls);
        Assert.Empty(g.WallFaceLines);
        Assert.Equal(2, fates.Count(f => f.Reason == PathReason.EmittedAsLine));
    }

    [Fact]
    public void AColumnsOutlineDrawnAsLinesIsNotAWall()
    {
        // a column of a size the schedule declares (the fixture's 1800 x 400), filled, with its outline
        // drawn again as lines (31202 p17 has eleven at 14" x 48"): its long sides are a pair 16" apart
        double cl = 1800, cw = 400;
        var column = FateFixture.Rect(cl, cw, 8000, 8000);
        var declares = new SheetFurniture.Set([], [], [], 1.5, [(cl, cw)], 1);
        var (g, fates) = WallFixture.Read(
            FilledWall().Concat([column, Face(8000, 8000, 8000 + cl, 8000), Face(8000, 8000 + cw, 8000 + cl, 8000 + cw)]).ToList(),
            furniture: declares);
        Assert.Equal(PathReason.BecameColumnByDeclaredSize, fates[3].Reason);
        Assert.Single(g.Columns);
        Assert.Single(g.Walls);
        Assert.Empty(g.WallFaceLines);
    }

    [Theory]
    [InlineData(50.0)]           // risers inset 2" from each stringer
    [InlineData(-100.0)]         // risers past the stringers to the walls, 4" beyond each face
    public void ARunOfRisersAcrossThePairMakesItAStairNotAWall(double inset)
    {
        double w = 44 * 25.4;                                                    // a stair flight's width
        var paths = new List<RawSubpath> { Face(1000, 5000, 1000 + L, 5000), Face(1000, 5000 + w, 1000 + L, 5000 + w) };
        for (double x = 1000 + w + 300; x < 1000 + L - w; x += 280)              // risers 11" apart
            paths.Add(Face(x, 5000 + inset, x, 5000 + w - inset, pen: 0.2));
        var (g, _) = Read(paths.ToArray());
        Assert.Single(g.Walls);
        Assert.Empty(g.WallFaceLines);
    }

    [Fact]
    public void ADimensionsExtensionLinesAcrossAWallAreABayApartAndItIsStillAWall()
    {
        var paths = new List<RawSubpath> { Face(1000, 5000, 1000 + L, 5000), Face(1000, 5000 + T, 1000 + L, 5000 + T) };
        for (double x = 2000; x < 1000 + L - T; x += 1500)                       // extension lines every 5', through the wall and 4' beyond
            paths.Add(Face(x, 5000 - 1219, x, 5000 + T + 1219, pen: 0.2));
        var (g, _) = Read(paths.ToArray());
        Assert.Equal(2, g.Walls.Count);
    }

    [Fact]
    public void AnXCornerToCornerMakesItAShaftNotAWall()
    {
        double w = 45 * 25.4, l = 60 * 25.4;
        var (g, _) = Read(Face(1000, 5000, 1000 + l, 5000), Face(1000, 5000 + w, 1000 + l, 5000 + w),
                          Face(1000, 5000, 1000 + l, 5000 + w, pen: 0.2), Face(1000, 5000 + w, 1000 + l, 5000, pen: 0.2));
        Assert.Single(g.Walls);
    }

    [Fact]
    public void OneEndCapAcrossThePairIsStillAWall()
    {
        var (g, _) = Read(Face(1000, 5000, 1000 + L, 5000), Face(1000, 5000 + T, 1000 + L, 5000 + T), Face(1000 + L, 5000, 1000 + L, 5000 + T));
        Assert.Equal(2, g.Walls.Count);
    }

    [Fact]
    public void FacesOfUnequalLengthReadAsTheirOverlap()
    {
        var (g, _) = Read(Face(1000, 5000, 1000 + L, 5000), Face(3000, 5000 + T, 3000 + L, 5000 + T));
        var wall = FaceWall(g);
        Assert.Equal(L - 2000, Length(wall), 1);
        Assert.Equal(3000, wall.Outline.Min(p => p.X), 1);
        Assert.Equal(1000 + L, wall.Outline.Max(p => p.X), 1);
    }

    [Theory]
    [InlineData(2 * 25.4)]       // 2": a double line, not a wall
    [InlineData(72 * 25.4)]      // 6': a corridor, not a wall
    public void AGapOutsideWallThicknessesIsNotAWall(double gap)
    {
        var (g, _) = Read(Face(1000, 5000, 1000 + L, 5000), Face(1000, 5000 + gap, 1000 + L, 5000 + gap));
        Assert.Single(g.Walls);
    }

    [Fact]
    public void ATurnedPairIsATurnedWall()
    {
        double c = Math.Cos(Math.PI / 6), s = Math.Sin(Math.PI / 6);
        (double, double) P(double u, double v) => (12000 + u * c - v * s, 2000 + u * s + v * c);
        var a0 = P(0, 0); var a1 = P(L, 0); var b0 = P(0, T); var b1 = P(L, T);
        var (g, _) = Read(Face(a0.Item1, a0.Item2, a1.Item1, a1.Item2), Face(b0.Item1, b0.Item2, b1.Item1, b1.Item2));
        var wall = FaceWall(g);
        Assert.Equal(T, wall.ThicknessMm, 1);
        Assert.Equal(L, Length(wall), 1);
    }

    private static double Length(WallPanel w)
        => Math.Sqrt(Math.Pow(w.End.X - w.Start.X, 2) + Math.Pow(w.End.Y - w.Start.Y, 2));
}
