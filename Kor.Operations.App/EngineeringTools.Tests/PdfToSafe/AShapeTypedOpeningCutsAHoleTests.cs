#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

/// <summary>
/// A SHAPE TYPED OPENING CUTS A HOLE (2026-10-20, Ian's decision C3). It used to be dropped with Ignore, so a shaft the
/// engineer typed Opening left the slab whole. One drawing, every output: a 10 m slab holding a 2 m shape typed Opening
/// (a polygon), a 2 m outline drawn as four lines typed Opening, and a 600 mm filled square typed Opening (the column
/// bucket); plus a shape typed Opening outside the slab.
/// WHAT THIS COVERS: ReclassifyByColor turns all four into openings and none into slabs; the SAFE file cuts the three in
/// the slab from it (FLOOR OBJECT OPENINGS) and the fourth from nothing; the ETABS file marks the same three OPENING
/// "Yes"; the OAPI push adds the same three as opening areas. WHAT IT DOES NOT: that SAFE or ETABS opens either file
/// (neither writer has been import-tested); the window's colour grid and saved projects (no UI here); the app DXF, which
/// writes the page un-reclassified by design (TheConvergenceProof), so a typed Opening is not on it. A same-class fault it
/// would miss: an opening that straddles a slab edge - the host is chosen by the opening's centre alone.
/// </summary>
public sealed class AShapeTypedOpeningCutsAHoleTests
{
    static readonly (byte R, byte G, byte B) SlabColour = (0, 0, 255);
    static readonly (byte R, byte G, byte B) HoleColour = (255, 0, 255);

    static List<(double X, double Y)> Box(double x0, double y0, double x1, double y1)
        => new() { (x0, y0), (x1, y0), (x1, y1), (x0, y1) };

    static ExtractedGeometry Drawing()
    {
        var g = new ExtractedGeometry { IsVectorPdf = true };
        g.Slabs.Add(Box(0, 0, 10000, 10000)); g.SlabColors.Add(SlabColour);
        g.Slabs.Add(Box(1000, 1000, 3000, 3000)); g.SlabColors.Add(HoleColour);        // a polygon typed Opening
        g.Slabs.Add(Box(20000, 0, 22000, 2000)); g.SlabColors.Add(HoleColour);         // typed Opening, in no slab
        foreach (var (a, b) in new[] { ((6000.0, 1000.0), (8000.0, 1000.0)), ((8000.0, 1000.0), (8000.0, 3000.0)),
                                       ((8000.0, 3000.0), (6000.0, 3000.0)), ((6000.0, 3000.0), (6000.0, 1000.0)) })
        {
            g.Lines.Add(new() { a, b }); g.LineColors.Add(HoleColour); g.LineSectionHints.Add(null);   // four lines typed Opening
        }
        g.Columns.Add((7000, 7000)); g.ColumnColors.Add(HoleColour); g.ColumnSizes.Add((600, 600));    // a filled square typed Opening
        return g;
    }

    static Dictionary<(byte R, byte G, byte B), SlabColorSettings> Types() => new()
    {
        [SlabColour] = new SlabColorSettings { ElementType = "Slab", ThicknessMm = 250, GradeCode = "C30" },
        [HoleColour] = new SlabColorSettings { ElementType = "Opening", ThicknessMm = 250, GradeCode = "C30" },
    };

    static ExtractedGeometry Reclassified() => PdfGeometryExtractor.ReclassifyByColor(Drawing(), Types());

    [Fact]
    public void EveryShapeTypedOpeningBecomesAnOpeningAndNoneASlab()
    {
        var r = Reclassified();
        Assert.Single(r.Slabs);
        Assert.Empty(r.Lines);
        Assert.Empty(r.Columns);
        Assert.Equal(4, r.CarriedOpenings.Count);
        Assert.Equal(3, r.CarriedOpenings.Count(o => WallOpeningDetector.HostSlab(r.Slabs, o) >= 0));
    }

    [Fact]
    public void TheSafeFileCutsTheThreeInTheSlabFromIt()
    {
        string f2k = Path.Combine(Path.GetTempPath(), $"kor-opening-{Guid.NewGuid():N}.f2k");
        try
        {
            SafeF2kExporter.Export(Reclassified(), f2k, colorSettings: Types(),
                settings: new ExportSettings { AutoGenerateOpeningsFromWalls = false });
            var lines = File.ReadAllLines(f2k, Encoding.ASCII);
            int start = Array.FindIndex(lines, l => l.Contains("\"FLOOR OBJECT OPENINGS\""));
            Assert.True(start >= 0, "no FLOOR OBJECT OPENINGS table written");
            var rows = lines.Skip(start + 1).TakeWhile(l => l.Trim().Length > 0).ToList();
            Assert.Equal(3, rows.Count);
            var hosts = rows.Select(l => System.Text.RegularExpressions.Regex.Match(l, @"FloorObject=(\S+)").Groups[1].Value).Distinct().ToList();
            Assert.Single(hosts);
            // the one floor is the slab; no hole was written as a floor of its own
            int summary = Array.FindIndex(lines, l => l.Contains("\"AREA ASSIGNMENTS - SUMMARY\""));
            var floors = lines.Skip(summary + 1).TakeWhile(l => l.Trim().Length > 0).ToList();
            Assert.Single(floors);
            Assert.Contains($"UniqueName={hosts[0]} ", floors[0]);
        }
        finally { File.Delete(f2k); }
    }

    [Fact]
    public void TheEtabsFileMarksTheSameThreeOpening()
    {
        string e2k = Path.Combine(Path.GetTempPath(), $"kor-opening-{Guid.NewGuid():N}.e2k");
        try
        {
            EtabsE2kExporter.Export(e2k, Reclassified(), Types(), new ExportSettings());
            var lines = File.ReadAllLines(e2k, Encoding.ASCII);
            Assert.Equal(3, lines.Count(l => l.Contains("OPENING \"Yes\"")));
            Assert.Equal(4, lines.Count(l => l.TrimStart().StartsWith("AREA \"", StringComparison.Ordinal) && l.Contains(" FLOOR ")));
        }
        finally { File.Delete(e2k); }
    }

    [Fact]
    public void ThePushAddsTheSameThreeAsOpenings()
    {
        var r = Reclassified();
        var driver = new VirtualSafeDriver();
        var result = ExportOrchestrator.Run(driver, new SafeApiExporter.ExportInput
        {
            Slabs = r.Slabs,
            SlabColors = r.SlabColors,
            Columns = r.Columns,
            ColumnSizes = r.ColumnSizes,
            Lines = r.Lines,
            LineSectionHints = r.LineSectionHints,
            ColorSettings = Types(),
            DefaultGradeCode = "C30",
            DesignCode = DesignCodeOption.CSA_A23_3_19,
            DefaultThicknessMm = 250.0,
            DefaultWallDepthMm = 1000.0,
            ColumnHeightMm = 3000.0,
            DestFdbPath = Path.Combine(Path.GetTempPath(), "kor-opening-test.fdb"),
            AutoGenerateOpeningsFromWalls = false,
            Openings = r.CarriedOpenings,
        });

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.SlabsExported);
        Assert.Equal(3, result.OpeningsCreated);
        Assert.Equal(3, driver.Openings.Count);
    }
}
