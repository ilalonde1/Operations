using System.Collections.Generic;
using System.Globalization;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

public class F2kModelPrepTests
{
    private static readonly CultureInfo Ic = CultureInfo.InvariantCulture;

    //  ComputeCentroid 

    [Fact]
    public void ComputeCentroid_SingleSlab_ReturnsAreaCentroid()
    {
        var geom = new ExtractedGeometry();
        geom.Slabs.Add(new List<(double, double)> { (0, 0), (2000, 0), (2000, 1000), (0, 1000) });

        var (cx, cy) = F2kModelPrep.ComputeCentroid(new[] { geom });
        Assert.Equal(1000.0, cx, precision: 3);
        Assert.Equal(500.0, cy, precision: 3);
    }

    [Fact]
    public void ComputeCentroid_SingleColumn_ReturnsColumnPosition()
    {
        var geom = new ExtractedGeometry();
        geom.Columns.Add((3000, 4000));

        var (cx, cy) = F2kModelPrep.ComputeCentroid(new[] { geom });
        Assert.Equal(3000.0, cx, precision: 3);
        Assert.Equal(4000.0, cy, precision: 3);
    }

    [Fact]
    public void ComputeCentroid_Empty_ReturnsOrigin()
    {
        var (cx, cy) = F2kModelPrep.ComputeCentroid(new[] { new ExtractedGeometry() });
        Assert.Equal(0.0, cx, precision: 10);
        Assert.Equal(0.0, cy, precision: 10);
    }

    //  PrepareGeometry 

    [Fact]
    public void PrepareGeometry_CentersCoordinates()
    {
        var geom = new ExtractedGeometry();
        geom.Slabs.Add(new List<(double, double)> { (0, 0), (2000, 0), (2000, 2000), (0, 2000) });
        geom.SlabColors.Add((255, 0, 0));
        geom.Columns.Add((1000, 1000));
        geom.ColumnColors.Add((0, 0, 255));
        geom.ColumnSizes.Add((500, 500));

        var (slabs, _, _, columns, _, _) = F2kModelPrep.PrepareGeometry(
            geom, cx: 1000, cy: 1000,
            excludedSlabs: null, excludedLines: null,
            excludedColumns: null, excludedColors: null);

        // Slab vertices should be shifted by (-1000, -1000)
        Assert.Single(slabs);
        Assert.Equal(-1000.0, slabs[0][0].X, precision: 3);
        Assert.Equal(-1000.0, slabs[0][0].Y, precision: 3);
        Assert.Equal(1000.0, slabs[0][1].X, precision: 3);

        // Column should be at (0, 0) after centering
        Assert.Single(columns);
        Assert.Equal(0.0, columns[0].X, precision: 3);
        Assert.Equal(0.0, columns[0].Y, precision: 3);
    }

    [Fact]
    public void PrepareGeometry_ExcludesBySlabIndex()
    {
        var geom = new ExtractedGeometry();
        geom.Slabs.Add(new List<(double, double)> { (0, 0), (2000, 0), (2000, 2000), (0, 2000) });
        geom.SlabColors.Add((255, 0, 0));
        geom.Slabs.Add(new List<(double, double)> { (3000, 0), (5000, 0), (5000, 2000), (3000, 2000) });
        geom.SlabColors.Add((0, 255, 0));

        var excluded = new HashSet<int> { 0 };
        var (slabs, colors, _, _, _, _) = F2kModelPrep.PrepareGeometry(
            geom, cx: 0, cy: 0,
            excludedSlabs: excluded, excludedLines: null,
            excludedColumns: null, excludedColors: null);

        Assert.Single(slabs);
        Assert.Equal((byte)0, colors[0].R);
        Assert.Equal((byte)255, colors[0].G); // green slab survived
    }

    [Fact]
    public void PrepareGeometry_ExcludesByColor()
    {
        var geom = new ExtractedGeometry();
        geom.Slabs.Add(new List<(double, double)> { (0, 0), (2000, 0), (2000, 2000), (0, 2000) });
        geom.SlabColors.Add((255, 0, 0));
        geom.Lines.Add(new List<(double, double)> { (0, 0), (5000, 0) });
        geom.LineColors.Add((255, 0, 0));

        var excludedColors = new HashSet<(byte, byte, byte)> { (255, 0, 0) };
        var (slabs, _, lines, _, _, _) = F2kModelPrep.PrepareGeometry(
            geom, cx: 0, cy: 0,
            excludedSlabs: null, excludedLines: null,
            excludedColumns: null, excludedColors: excludedColors);

        Assert.Empty(slabs);
        Assert.Empty(lines);
    }

    //  BuildStoryModel 

    [Fact]
    public void BuildStoryModel_SingleSlab_CreatesAreaWithPoints()
    {
        // 4000mm CCW square centred at origin (already centred)
        var slabs = new List<List<(double, double)>>
        {
            new() { (-2000, -2000), (2000, -2000), (2000, 2000), (-2000, 2000) }
        };
        var colors = new List<(byte, byte, byte)> { (255, 0, 0) };

        var story = F2kModelPrep.BuildStoryModel(
            slabs, colors,
            xLines: new(), xColumns: new(), xColumnBaseSizes: new(),
            xDropPanelCandidates: new(),
            annotations: new List<(string, double, double)>(),
            colorSettings: null,
            idPrefix: "", elevationMm: 0, ic: Ic);

        Assert.Single(story.Areas);
        Assert.Equal(4, story.Areas[0].PtNames.Count);
        Assert.Equal("A1", story.Areas[0].Id);
        // Default thickness 200, default grade C30
        Assert.Equal(200.0, story.Areas[0].ThicknessMm);
        Assert.Equal("C30", story.Areas[0].GradeCode);
        Assert.StartsWith("S200C30", story.Areas[0].PropName);
    }

    [Fact]
    public void BuildStoryModel_ColorSettings_OverrideThicknessAndGrade()
    {
        var slabs = new List<List<(double, double)>>
        {
            new() { (-2000, -2000), (2000, -2000), (2000, 2000), (-2000, 2000) }
        };
        var colors = new List<(byte, byte, byte)> { (255, 0, 0) };
        var settings = new Dictionary<(byte, byte, byte), SlabColorSettings>
        {
            [(255, 0, 0)] = new SlabColorSettings { ThicknessMm = 300, GradeCode = "C40" }
        };

        var story = F2kModelPrep.BuildStoryModel(
            slabs, colors,
            xLines: new(), xColumns: new(), xColumnBaseSizes: new(),
            xDropPanelCandidates: new(),
            annotations: new List<(string, double, double)>(),
            colorSettings: settings,
            idPrefix: "", elevationMm: 0, ic: Ic);

        Assert.Equal(300.0, story.Areas[0].ThicknessMm);
        Assert.Equal("C40", story.Areas[0].GradeCode);
        Assert.StartsWith("S300C40", story.Areas[0].PropName);
    }

    [Fact]
    public void BuildStoryModel_AnnotationThickness_TakesPriorityOverColorSettings()
    {
        var slabs = new List<List<(double, double)>>
        {
            new() { (-2000, -2000), (2000, -2000), (2000, 2000), (-2000, 2000) }
        };
        var colors = new List<(byte, byte, byte)> { (255, 0, 0) };
        var settings = new Dictionary<(byte, byte, byte), SlabColorSettings>
        {
            [(255, 0, 0)] = new SlabColorSettings { ThicknessMm = 300, GradeCode = "C30" }
        };
        // Annotation near slab centroid (0,0) saying 250 THK
        var annotations = new List<(string Text, double X, double Y)>
        {
            ("250 THK", 0, 0)
        };

        var story = F2kModelPrep.BuildStoryModel(
            slabs, colors,
            xLines: new(), xColumns: new(), xColumnBaseSizes: new(),
            xDropPanelCandidates: new(),
            annotations: annotations,
            colorSettings: settings,
            idPrefix: "", elevationMm: 0, ic: Ic);

        Assert.Equal(250.0, story.Areas[0].ThicknessMm);
    }

    [Fact]
    public void BuildStoryModel_SingleBeam_CreatesLineSegments()
    {
        var lines = new List<List<(double, double)>>
        {
            new() { (-3000, 0), (0, 0), (3000, 0) } // 2 segments
        };

        var story = F2kModelPrep.BuildStoryModel(
            xSlabs: new(), xSlabColors: new(),
            xLines: lines, xColumns: new(), xColumnBaseSizes: new(),
            xDropPanelCandidates: new(),
            annotations: new List<(string, double, double)>(),
            colorSettings: null,
            idPrefix: "", elevationMm: 0, ic: Ic);

        Assert.Equal(2, story.LineSegs.Count);
        Assert.Equal("L1", story.LineSegs[0].Id);
        Assert.Equal("L2", story.LineSegs[1].Id);
        Assert.Equal(3000.0, story.LineSegs[0].LenMm, precision: 1);
    }

    [Fact]
    public void BuildStoryModel_Column_DefaultSize500x500()
    {
        var columns = new List<(double, double)> { (0, 0) };
        var baseSizes = new List<(double, double)> { (0, 0) }; // no valid base size

        var story = F2kModelPrep.BuildStoryModel(
            xSlabs: new(), xSlabColors: new(),
            xLines: new(), xColumns: columns, xColumnBaseSizes: baseSizes,
            xDropPanelCandidates: new(),
            annotations: new List<(string, double, double)>(),
            colorSettings: null,
            idPrefix: "", elevationMm: 0, ic: Ic);

        Assert.Single(story.ColumnPointNames);
        Assert.Single(story.ColumnSections);
        Assert.Equal("C500x500", story.ColumnSections[0].SecName);
        Assert.Equal(500, story.ColumnSections[0].W);
        Assert.Equal(500, story.ColumnSections[0].D);
    }

    [Fact]
    public void BuildStoryModel_Column_UsesBaseSizeWhenValid()
    {
        var columns = new List<(double, double)> { (0, 0) };
        var baseSizes = new List<(double, double)> { (600, 800) };

        var story = F2kModelPrep.BuildStoryModel(
            xSlabs: new(), xSlabColors: new(),
            xLines: new(), xColumns: columns, xColumnBaseSizes: baseSizes,
            xDropPanelCandidates: new(),
            annotations: new List<(string, double, double)>(),
            colorSettings: null,
            idPrefix: "", elevationMm: 0, ic: Ic);

        Assert.Equal("C600x800", story.ColumnSections[0].SecName);
    }

    [Fact]
    public void BuildStoryModel_WithPrefix_NamesArePrefixed()
    {
        var slabs = new List<List<(double, double)>>
        {
            new() { (-2000, -2000), (2000, -2000), (2000, 2000), (-2000, 2000) }
        };
        var colors = new List<(byte, byte, byte)> { (255, 0, 0) };

        var story = F2kModelPrep.BuildStoryModel(
            slabs, colors,
            xLines: new(), xColumns: new(), xColumnBaseSizes: new(),
            xDropPanelCandidates: new(),
            annotations: new List<(string, double, double)>(),
            colorSettings: null,
            idPrefix: "s1_", elevationMm: 4000, ic: Ic);

        Assert.StartsWith("s1_A", story.Areas[0].Id);
        Assert.All(story.PointOrder, p => Assert.StartsWith("s1_J", p.Name));
        Assert.All(story.PointOrder, p => Assert.Equal(4000.0, p.Z));
    }

    [Fact]
    public void BuildStoryModel_NestedSlabs_BothExportedAsIndependentAreas()
    {
        // Automatic opening detection was disabled because Bluebeam markup places
        // structural elements (columns, walls, cores) inside the slab perimeter —
        // treating them all as openings produced swiss-cheese slabs. Nested slab
        // polygons now export as two independent Floor objects; openings are only
        // created when the user (or AI) explicitly assigns the "Opening" type.
        var outer = new List<(double, double)>
        {
            (-5000, -5000), (5000, -5000), (5000, 5000), (-5000, 5000)
        };
        var inner = new List<(double, double)>
        {
            (-1000, -1000), (1000, -1000), (1000, 1000), (-1000, 1000)
        };
        var slabs = new List<List<(double, double)>> { outer, inner };
        var colors = new List<(byte, byte, byte)> { (255, 0, 0), (0, 255, 0) };

        var story = F2kModelPrep.BuildStoryModel(
            slabs, colors,
            xLines: new(), xColumns: new(), xColumnBaseSizes: new(),
            xDropPanelCandidates: new(),
            annotations: new List<(string, double, double)>(),
            colorSettings: null,
            idPrefix: "", elevationMm: 0, ic: Ic);

        Assert.Equal(2, story.Areas.Count);
        Assert.Empty(story.OpeningRows);
    }

    [Fact]
    public void BuildStoryModel_PointDeduplication_SharedVerticesGetSameName()
    {
        // Two adjacent slabs sharing an edge at x=0
        var left = new List<(double, double)>
        {
            (-2000, -2000), (0, -2000), (0, 2000), (-2000, 2000)
        };
        var right = new List<(double, double)>
        {
            (0, -2000), (2000, -2000), (2000, 2000), (0, 2000)
        };
        var slabs = new List<List<(double, double)>> { left, right };
        var colors = new List<(byte, byte, byte)> { (255, 0, 0), (0, 255, 0) };

        var story = F2kModelPrep.BuildStoryModel(
            slabs, colors,
            xLines: new(), xColumns: new(), xColumnBaseSizes: new(),
            xDropPanelCandidates: new(),
            annotations: new List<(string, double, double)>(),
            colorSettings: null,
            idPrefix: "", elevationMm: 0, ic: Ic);

        Assert.Equal(2, story.Areas.Count);
        // 6 unique points (8 vertices minus 2 shared on the common edge)
        Assert.Equal(6, story.PointOrder.Count);
    }
}
