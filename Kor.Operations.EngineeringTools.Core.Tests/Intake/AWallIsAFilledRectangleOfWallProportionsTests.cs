using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// Synthetic wall dimensions, rotation, rule precedence, shared settings and DXF export.
/// Does not bank any stick-file count or prove that every wall-shaped fill is a structural wall.
/// </summary>
public sealed class AWallIsAFilledRectangleOfWallProportionsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(90)]
    public void AFootThickTwentyFootWallHasTheRightAxisAndThickness(double degrees)
    {
        double angle = degrees * Math.PI / 180;
        (double X, double Y) Transform((double X, double Y) p)
            => (5000 + p.X * Math.Cos(angle) - p.Y * Math.Sin(angle),
                8000 + p.X * Math.Sin(angle) + p.Y * Math.Cos(angle));
        var path = WallFixture.Rect(12, 240);
        path = path with { Points = path.Points.Select(Transform).ToList() };
        var (geometry, fates) = WallFixture.Read([path]);
        var wall = Assert.Single(geometry.Walls);
        Assert.Equal(304.8, wall.ThicknessMm, 6);
        var start = Transform((0, 152.4));
        var end = Transform((6096, 152.4));
        static double Distance((double X, double Y) a, (double X, double Y) b)
            => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
        Assert.True((Distance(start, wall.Start) < 1e-6 && Distance(end, wall.End) < 1e-6)
            || (Distance(start, wall.End) < 1e-6 && Distance(end, wall.Start) < 1e-6));
        Assert.Same(path.Points, wall.Outline);
        Assert.Equal(path.Color, Assert.Single(geometry.WallColors));
        Assert.False(Assert.Single(geometry.WallIsAnnotation));
        Assert.Equal(new PathFate(0, Disposition.Read, PathReason.BecameWall, 0), Assert.Single(fates));
        Assert.Empty(geometry.Slabs);
        Assert.Empty(geometry.Columns);
        Assert.Empty(geometry.Lines);
    }

    [Theory]
    [InlineData(14, 38)]
    [InlineData(12, 20)]
    [InlineData(3, 120)]
    [InlineData(61, 240)]
    public void ShapesOutsideTheBankedWindowKeepTheirFormerFate(double thickness, double length)
    {
        var paths = new[] { WallFixture.Rect(thickness, length) };
        var (expected, expectedFates) = WallFixture.Read(paths, WallFixture.Disabled);
        var (actual, actualFates) = WallFixture.Read(paths);
        Assert.Empty(actual.Walls);
        TheLedgerChangesNothingButTheLedgerTests.AssertGeometryEqual(expected, actual);
        Assert.Equal(expectedFates.ToArray(), actualFates.ToArray());
    }

    [Fact]
    public void AspectIsAnIndependentGate()
    {
        var (geometry, fates) = WallFixture.Read([WallFixture.Rect(12, 20)],
            PdfIntakeOptions.Default with { MinWallLengthMm = 0 });
        Assert.Empty(geometry.Walls);
        Assert.Equal(PathReason.BecameColumnByShape, Assert.Single(fates).Reason);
    }

    [Fact]
    public void FortyInchesIsWithinTheBankedSixtyButOutsideACustomThirtySix()
    {
        Assert.Single(WallFixture.Read([WallFixture.Rect(40, 120)]).Geometry.Walls);
        Assert.Empty(WallFixture.Read([WallFixture.Rect(40, 120)],
            PdfIntakeOptions.Default with { MaxWallThicknessMm = 36 * 25.4 }).Geometry.Walls);
    }

    [Theory]
    [InlineData(4, 48)]
    [InlineData(60, 120)]
    public void TheLimitsAreInclusive(double thickness, double length)
    {
        Assert.Single(WallFixture.Read([WallFixture.Rect(thickness, length)]).Geometry.Walls);
    }

    [Fact]
    public void DeclaredColumnsPaperAndAnnotationsKeepPrecedence()
    {
        var furniture = SheetFurniture.Set.Empty with
        {
            DeclaredColumnSizesMm = [(18 * 25.4, 60 * 25.4)], SizeToleranceMm = 1,
        };
        var (column, cf) = WallFixture.Read([WallFixture.Rect(18, 60)], furniture: furniture);
        Assert.Single(column.Columns);
        Assert.Empty(column.Walls);
        Assert.Equal(PathReason.BecameColumnByDeclaredSize, Assert.Single(cf).Reason);
        var (paper, pf) = WallFixture.Read([WallFixture.Rect(12, 240) with { Color = (0xF0, 0xF0, 0xF0) }]);
        Assert.Empty(paper.Walls);
        Assert.Equal(PathReason.PaperFill, Assert.Single(pf).Reason);
        var (annotation, af) = WallFixture.Read([WallFixture.Rect(12, 240) with { IsAnnotation = true }]);
        Assert.Empty(annotation.Walls);
        Assert.Single(annotation.Slabs);
        Assert.Equal(PathReason.BecameSlab, Assert.Single(af).Reason);
        var (unfilled, uf) = WallFixture.Read([WallFixture.Rect(12, 240) with { IsFilled = false, IsStroked = true }]);
        Assert.Empty(unfilled.Walls);
        Assert.Equal(PathReason.BecameSlab, Assert.Single(uf).Reason);
    }

    [Fact]
    public void ASixVertexRibbonIsCountedAndKeepsItsSlabFate()
    {
        var (geometry, fates) = WallFixture.Read([WallFixture.Ribbon()]);
        Assert.Empty(geometry.Walls);
        Assert.Single(geometry.Slabs);
        Assert.Equal(1, geometry.WallRibbonsNotSplit);
        Assert.Equal(PathReason.BecameSlab, Assert.Single(fates).Reason);
    }

    [Fact]
    public void DefaultsAndSharedKeysUseBankedInchesAndDimensionlessAspect()
    {
        var d = PdfIntakeOptions.Default;
        Assert.Equal((101.6, 1524.0, 1219.2, 2.0),
            (d.MinWallThicknessMm, d.MaxWallThicknessMm, d.MinWallLengthMm, d.MinWallAspect));
        var settings = new Dictionary<string, RuleSetting>
        {
            [PdfIntakeOptions.SharedMinWallThickness] = new(PdfIntakeOptions.SharedMinWallThickness, 6, "in", "test", "test", "test"),
            [PdfIntakeOptions.SharedMaxWallThickness] = new(PdfIntakeOptions.SharedMaxWallThickness, 48, "in", "test", "test", "test"),
            [PdfIntakeOptions.SharedMinWallLength] = new(PdfIntakeOptions.SharedMinWallLength, 72, "in", "test", "test", "test"),
            [PdfIntakeOptions.SharedMinWallAspect] = new(PdfIntakeOptions.SharedMinWallAspect, 3, "ratio", "test", "test", "test"),
        };
        Assert.All(settings.Keys, k => Assert.Contains(k, PdfIntakeOptions.SettingKeys));
        var applied = PdfIntakeOptions.ApplyRules(d, settings);
        Assert.Equal((6 * 25.4, 48 * 25.4, 72 * 25.4, 3.0),
            (applied.MinWallThicknessMm, applied.MaxWallThicknessMm, applied.MinWallLengthMm, applied.MinWallAspect));
        Assert.Equal(d, PdfIntakeOptions.ApplyRules(d, new Dictionary<string, RuleSetting>()));
        Assert.Empty(WallFixture.Read([WallFixture.Rect(12, 60)], applied).Geometry.Walls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APageContainingOnlyAWallExportsAClosedReadableWallOutline(bool korLayers)
    {
        var (geometry, _) = WallFixture.Read([WallFixture.Rect(12, 240)]);
        string path = Path.GetTempFileName();
        try
        {
            DxfExporter.Export(geometry, path, korLayers: korLayers);
            var segments = DxfPlanReader.ReadSegments(path);
            Assert.Equal(4, segments.Count);
            Assert.All(segments, s => Assert.Equal(korLayers ? "KOR_V-WALL" : "WALL", s.Layer));
            for (int i = 0; i < segments.Count; i++)
                Assert.Equal(segments[i].End, segments[(i + 1) % segments.Count].Start);
            var rules = new PlanClassificationOptions().InUnitOf(DxfPlanReader.UnitInInches(path)!.Value);
            var recovered = StructuralPlanClassifier.Classify(segments, rules);
            var wall = Assert.Single(recovered.Walls);
            Assert.Equal(304.8, wall.Thickness, 4);
            Assert.Equal(6096, wall.Length, 4);
        }
        finally { File.Delete(path); }
    }
}

internal static class WallFixture
{
    internal static PdfIntakeOptions Disabled => PdfIntakeOptions.Default with { MinWallLengthMm = double.PositiveInfinity };
    internal static RawSubpath Rect(double thicknessIn, double lengthIn)
        => FateFixture.Rect(lengthIn * 25.4, thicknessIn * 25.4);

    // Both legs are 12" thick; the bounding box is 24" wide. Counting does not split the ribbon.
    internal static RawSubpath Ribbon() => Rect(12, 240) with
    {
        Points = [(0, 0), (6096, 0), (6096, 304.8), (304.8, 304.8), (304.8, 609.6), (0, 609.6)],
    };

    internal static (ExtractedGeometry Geometry, List<PathFate> Fates) Read(IReadOnlyList<RawSubpath> paths,
        PdfIntakeOptions? options = null, SheetFurniture.Set? furniture = null, bool markupOnly = false, bool excludeGrid = false)
    {
        options ??= PdfIntakeOptions.Default;
        var geometry = new ExtractedGeometry();
        var fates = new List<PathFate>();
        GeometryFilterService.Classify(paths, geometry, options.SlabMinDiagonalMm, options.LineMinLengthMm,
            excludeGrid, 100000, 70000, markupOnly, options.ColumnMaxSizeMm, options.ColumnMinDimMm,
            options.ColumnMaxAspect, furniture, fates,
            options.MinWallThicknessMm, options.MaxWallThicknessMm, options.MinWallLengthMm, options.MinWallAspect);
        return (geometry, fates);
    }
}
