using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

public class StructuralGridGeneratorTests
{
    [Fact]
    public void Generate_TwoColumnsSameX_ProducesOneXGridLine()
    {
        // Two columns at X=1000, Y=0 and Y=5000
        var columns = new List<(double, double)> { (1000, 0), (1000, 5000) };
        var grids = StructuralGridGenerator.Generate(columns);

        var xGrids = grids.Where(g => !g.IsAlongX).ToList(); // constant-X lines
        Assert.Single(xGrids);
        Assert.Equal("1", xGrids[0].Label);
        Assert.Equal(1000.0, xGrids[0].OrdMm, precision: 1);
    }

    [Fact]
    public void Generate_ThreeDistinctXPositions_ProducesNumberedLabels()
    {
        var columns = new List<(double, double)>
        {
            (0, 0), (3000, 0), (6000, 0)
        };
        var grids = StructuralGridGenerator.Generate(columns);

        var xGrids = grids.Where(g => !g.IsAlongX).OrderBy(g => g.OrdMm).ToList();
        Assert.Equal(3, xGrids.Count);
        Assert.Equal("1", xGrids[0].Label);
        Assert.Equal("2", xGrids[1].Label);
        Assert.Equal("3", xGrids[2].Label);
    }

    [Fact]
    public void Generate_YPositions_ProduceLetterLabels()
    {
        var columns = new List<(double, double)>
        {
            (0, 0), (0, 3000), (0, 6000)
        };
        var grids = StructuralGridGenerator.Generate(columns);

        var yGrids = grids.Where(g => g.IsAlongX).OrderBy(g => g.OrdMm).ToList();
        Assert.Equal(3, yGrids.Count);
        Assert.Equal("A", yGrids[0].Label);
        Assert.Equal("B", yGrids[1].Label);
        Assert.Equal("C", yGrids[2].Label);
    }

    [Fact]
    public void Generate_NearbyColumns_ClusteredIntoOneGridLine()
    {
        // Two columns within 400mm tolerance  merged
        var columns = new List<(double, double)> { (1000, 0), (1200, 0) };
        var grids = StructuralGridGenerator.Generate(columns);

        var xGrids = grids.Where(g => !g.IsAlongX).ToList();
        Assert.Single(xGrids);
        Assert.Equal(1100.0, xGrids[0].OrdMm, precision: 1); // average
    }

    [Fact]
    public void Generate_Empty_ReturnsEmpty()
    {
        Assert.Empty(StructuralGridGenerator.Generate(new List<(double, double)>()));
    }

    [Fact]
    public void Generate_TooManyColumns_ReturnsEmptyGrid()
    {
        // 50 columns at distinct X positions  exceeds 20 cap
        var columns = new List<(double, double)>();
        for (int i = 0; i < 50; i++)
            columns.Add((i * 2000.0, 0));

        var grids = StructuralGridGenerator.Generate(columns);

        Assert.Empty(grids);
    }
}

public class DesignStripGeneratorTests
{
    [Fact]
    public void Generate_SingleSlab_ProducesStripsInBothDirections()
    {
        var slabs = new List<List<(double, double)>>
        {
            new() { (0, 0), (6000, 0), (6000, 6000), (0, 6000) }
        };
        var strips = DesignStripGenerator.Generate(slabs, spacingMm: 3000, stripAAlongX: true);

        var sa = strips.Where(s => s.Name.StartsWith("SA")).ToList();
        var sb = strips.Where(s => s.Name.StartsWith("SB")).ToList();
        Assert.True(sa.Count > 0, "Should have SA (horizontal) strips");
        Assert.True(sb.Count > 0, "Should have SB (vertical) strips");
        Assert.All(sa, s => Assert.True(s.IsAlongX));
        Assert.All(sb, s => Assert.False(s.IsAlongX));
    }

    [Fact]
    public void Generate_ZeroSpacing_ReturnsEmpty()
    {
        var slabs = new List<List<(double, double)>>
        {
            new() { (0, 0), (6000, 0), (6000, 6000), (0, 6000) }
        };
        Assert.Empty(DesignStripGenerator.Generate(slabs, spacingMm: 0, stripAAlongX: true));
    }

    [Fact]
    public void Generate_HalfWidthEqualsHalfSpacing()
    {
        var slabs = new List<List<(double, double)>>
        {
            new() { (0, 0), (6000, 0), (6000, 6000), (0, 6000) }
        };
        var strips = DesignStripGenerator.Generate(slabs, spacingMm: 2000, stripAAlongX: true);
        Assert.All(strips, s => Assert.Equal(1000.0, s.HalfWidth, precision: 1));
    }
}

public class StructuralMaterialDatabaseTests
{
    [Fact]
    public void GetGrade_C30Default_ReturnsExpectedFc()
    {
        var (_, _, fc) = StructuralMaterialDatabase.GetGrade("C30");
        Assert.Equal(30.0, fc);
    }

    [Fact]
    public void GetGrade_CSA_UsesCorrectFormula()
    {
        // CSA: E = 4500 * sqrt(fc)
        var (e, _, fc) = StructuralMaterialDatabase.GetGrade("C30", DesignCodeOption.CSA_A23_3_19);
        Assert.Equal(30.0, fc);
        Assert.Equal(4500 * System.Math.Sqrt(30), e, precision: 1);
    }

    [Fact]
    public void GetGrade_ACI_UsesCorrectFormula()
    {
        var (e, _, _) = StructuralMaterialDatabase.GetGrade("C30", DesignCodeOption.ACI_318_19);
        Assert.Equal(4700 * System.Math.Sqrt(30), e, precision: 1);
    }

    [Fact]
    public void GetGrade_UnknownGrade_FallsBackToC30()
    {
        var (_, _, fc) = StructuralMaterialDatabase.GetGrade("C99");
        Assert.Equal(30.0, fc);
    }

    [Fact]
    public void GetGrade_ShearModulus_IsEOver2Point4()
    {
        // G = E / (2 * (1 + 0.20)) = E / 2.4
        var (e, g, _) = StructuralMaterialDatabase.GetGrade("C30");
        Assert.Equal(e / 2.4, g, precision: 1);
    }

    [Fact]
    public void SupportedGrades_ContainsExpectedSet()
    {
        var grades = StructuralMaterialDatabase.SupportedGrades;
        Assert.Contains("C20", grades);
        Assert.Contains("C30", grades);
        Assert.Contains("C50", grades);
        Assert.Equal(8, grades.Count);
    }

    [Fact]
    public void GetRebarSizes_CSA_ReturnsSizes()
    {
        var sizes = StructuralMaterialDatabase.GetRebarSizes(DesignCodeOption.CSA_A23_3_19);
        Assert.True(sizes.Count > 0);
        Assert.Contains(sizes, r => r.Name == "10M");
    }

    [Fact]
    public void GetRebarSizes_ACI_ReturnsSizes()
    {
        var sizes = StructuralMaterialDatabase.GetRebarSizes(DesignCodeOption.ACI_318_19);
        Assert.True(sizes.Count > 0);
        Assert.Contains(sizes, r => r.Name == "#3");
    }
}

public class F2kWriterTests
{
    private static readonly CultureInfo Ic = CultureInfo.InvariantCulture;

    private static string WriteToString(System.Action<StreamWriter> write)
    {
        using var ms = new MemoryStream();
        using (var sw = new StreamWriter(ms, leaveOpen: true))
        {
            write(sw);
            sw.Flush();
        }
        ms.Position = 0;
        return new StreamReader(ms).ReadToEnd();
    }

    /// <summary>
    /// Builds a minimal single-story model and writes it through WriteTables,
    /// then verifies the output contains all expected table headers and key values.
    /// </summary>
    [Fact]
    public void WriteTables_MinimalModel_ContainsExpectedSections()
    {
        // Build a minimal story: one slab, one beam, one column
        var slabs = new List<List<(double, double)>>
        {
            new() { (-2000, -2000), (2000, -2000), (2000, 2000), (-2000, 2000) }
        };
        var colors = new List<(byte, byte, byte)> { (255, 0, 0) };
        var lines = new List<List<(double, double)>> { new() { (-3000, 0), (3000, 0) } };
        var columns = new List<(double, double)> { (0, 0) };
        var baseSizes = new List<(double, double)> { (500, 500) };

        var story = F2kModelPrep.BuildStoryModel(
            slabs, colors, lines, columns, baseSizes,
            xDropPanelCandidates: new(),
            annotations: new List<(string, double, double)>(),
            colorSettings: null,
            idPrefix: "", elevationMm: 0, ic: Ic);

        var gridLines = StructuralGridGenerator.Generate(story.ColumnsForGrid);
        var settings = new ExportSettings { MeshSizeMm = 500 };

        string output = WriteToString(sw =>
            F2kWriter.WriteTables(sw, new[] { story }, null, settings, gridLines, Ic));

        // Verify essential table headers
        Assert.Contains("PROGRAM CONTROL", output);
        Assert.Contains("MATERIAL PROPERTIES", output);
        Assert.Contains("SLAB PROPERTY DEFINITIONS", output);
        Assert.Contains("FLOOR OBJECT CONNECTIVITY", output);
        Assert.Contains("POINT OBJECT CONNECTIVITY", output);
        Assert.Contains("LINE OBJECT CONNECTIVITY", output);
        Assert.Contains("COLUMN SECTION DEFINITIONS", output);
        Assert.Contains("FLOOR AUTO MESH OPTIONS", output);
        Assert.Contains("JOINT ASSIGNMENTS - RESTRAINTS", output);
        Assert.Contains("LOAD PATTERN DEFINITIONS", output);
        Assert.Contains("END TABLE DATA", output);

        // Verify units
        Assert.Contains("N, mm, C", output);

        // Verify area and line IDs present
        Assert.Contains("A1", output);
        Assert.Contains("L1", output);

        // Verify default material
        Assert.Contains("Material=C30", output);
    }

    [Fact]
    public void WriteTables_WithLoads_ContainsLoadPatterns()
    {
        var slabs = new List<List<(double, double)>>
        {
            new() { (-2000, -2000), (2000, -2000), (2000, 2000), (-2000, 2000) }
        };
        var colors = new List<(byte, byte, byte)> { (255, 0, 0) };
        var colorSettings = new Dictionary<(byte, byte, byte), SlabColorSettings>
        {
            [(255, 0, 0)] = new SlabColorSettings
            {
                ThicknessMm = 200, GradeCode = "C30",
                SdlKPa = 2.5, LiveKPa = 5.0
            }
        };

        var story = F2kModelPrep.BuildStoryModel(
            slabs, colors,
            xLines: new(), xColumns: new(), xColumnBaseSizes: new(),
            xDropPanelCandidates: new(),
            annotations: new List<(string, double, double)>(),
            colorSettings: colorSettings,
            idPrefix: "", elevationMm: 0, ic: Ic);

        var settings = new ExportSettings { LoadCombCode = "NBC" };

        string output = WriteToString(sw =>
            F2kWriter.WriteTables(sw, new[] { story }, colorSettings, settings, new List<StructuralGridLine>(), Ic));

        Assert.Contains("Name=SDL", output);
        Assert.Contains("Name=LIVE", output);
        Assert.Contains("AREA LOAD ASSIGNMENTS - UNIFORM", output);
        Assert.Contains("LOAD COMBINATION DEFINITIONS", output);
    }

    [Fact]
    public void WriteTables_WithDesignStrips_ContainsStripTable()
    {
        var slabs = new List<List<(double, double)>>
        {
            new() { (-3000, -3000), (3000, -3000), (3000, 3000), (-3000, 3000) }
        };
        var colors = new List<(byte, byte, byte)> { (255, 0, 0) };

        var story = F2kModelPrep.BuildStoryModel(
            slabs, colors,
            xLines: new(), xColumns: new(), xColumnBaseSizes: new(),
            xDropPanelCandidates: new(),
            annotations: new List<(string, double, double)>(),
            colorSettings: null,
            idPrefix: "", elevationMm: 0, ic: Ic);

        var settings = new ExportSettings
        {
            AutoGenerateStrips = true,
            StripSpacingMm = 2000,
            StripAAlongX = true
        };

        string output = WriteToString(sw =>
            F2kWriter.WriteTables(sw, new[] { story }, null, settings, new List<StructuralGridLine>(), Ic));

        Assert.Contains("DESIGN STRIPS", output);
        Assert.Contains("SA001", output);
        Assert.Contains("SB001", output);
    }

    [Fact]
    public void WriteTables_MaterialProperties_FormattedToTwoDecimalPlaces()
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
            idPrefix: "", elevationMm: 0, ic: Ic);

        var settings = new ExportSettings();
        string output = WriteToString(sw =>
            F2kWriter.WriteTables(sw, new[] { story }, null, settings, new List<StructuralGridLine>(), Ic));

        // E1 and G12 should have exactly 2 decimal places, not 15+
        Assert.Matches(@"E1=\d+\.\d{2}\s", output);
        Assert.Matches(@"G12=\d+\.\d{2}\s", output);
        Assert.Matches(@"Fc=\d+\.\d{2}\s", output);
    }
}
