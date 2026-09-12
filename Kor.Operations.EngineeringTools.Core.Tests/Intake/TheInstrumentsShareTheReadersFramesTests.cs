#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// The instruments (completion plan WP2, 2026-09-11) read the same frames the readers write, through
/// the same code: a model's grid through <see cref="E2kDocument.ReadGrids"/> — one reader for the
/// differential, the yardstick, grid-names and model-to-page — and a scratch DXF's page origin
/// through <see cref="DxfPlanReader.PageOriginInDrawing"/>, which the exporter banks as
/// <c>$INSBASE</c> so a point in the DXF's recentred frame can be carried back to the page.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: the grid reader on a model with two systems and a lower-case label; the
/// origin round trip — exported geometry recentred on its weighted centroid, the header stating
/// where the page origin went, a drawn point carried back to where it was; a DXF with no such
/// header answering null. WHAT IT DOES NOT: the verbs' printed text (grid-names, model-to-page,
/// vector-lines, vector-find, pdf-overlay --mark/--crop are exercised by hand on 31168 p4, §55);
/// the shift model-to-page takes from the shared grid names (ModelDiff's, covered by
/// TheDifferentialAndTheRenderAreCodeTests).
/// </remarks>
public sealed class TheInstrumentsShareTheReadersFramesTests
{
    [Fact]
    public void AModelsGridsAreReadOncePerSystemLabelAndDirection()
    {
        var doc = E2kDocument.Parse(
        [
            "$ CONTROLS", "  UNITS  \"LB\"  \"MM\"  \"F\"", "",
            "$ GRIDS",
            "  GRIDSYSTEM \"G1\"  TYPE \"CARTESIAN\"  BUBBLESIZE 60",
            "  GRID \"G1\"  LABEL \"1\"  DIR \"X\"  COORD -43800.32 VISIBLE \"Yes\"  BUBBLELOC \"End\"",
            "  GRID \"G1\"  LABEL \"r\"  DIR \"Y\"  COORD -9137.453 VISIBLE \"Yes\"  BUBBLELOC \"End\"",
            "  GRID \"G2\"  LABEL \"1\"  DIR \"X\"  COORD 5 VISIBLE \"Yes\"  BUBBLELOC \"End\"",
            "",
        ]);
        var grids = doc.ReadGrids();
        Assert.Equal(3, grids.Count);
        Assert.Equal(-43800.32, grids[("G1", "1", "X")]);
        Assert.Equal(-9137.453, grids[("G1", "R", "Y")]);                    // labels are read upper-case, as the DXF's names are matched
        Assert.Equal(5, grids[("G2", "1", "X")]);                             // the same label in a second system is a second axis
    }

    [Fact]
    public void TheScratchDxfStatesWhereThePageOriginWentAndAPointComesBack()
    {
        string path = Path.Combine(Path.GetTempPath(), $"insbase-{Guid.NewGuid():N}.dxf");
        try
        {
            var g = new ExtractedGeometry();
            g.Slabs.Add(new List<(double X, double Y)> { (10000, 20000), (30000, 20000), (30000, 32000), (10000, 32000) });
            g.SlabColors.Add((0xD0, 0xD0, 0xD0));
            DxfExporter.Export(g, path);

            var origin = DxfPlanReader.PageOriginInDrawing(path);
            Assert.NotNull(origin);
            // one slab: the weighted centroid is its centre (20000, 26000), so the page origin sits at
            // minus that in the recentred frame, and the slab's first corner reads back where it was drawn
            Assert.Equal(-20000, origin!.Value.X, 1e-3);
            Assert.Equal(-26000, origin.Value.Y, 1e-3);
            var corner = DxfPlanReader.ReadSegments(path).Select(s => s.Start).OrderBy(p => p.X).ThenBy(p => p.Y).First();
            Assert.Equal(10000, corner.X - origin.Value.X, 1e-3);
            Assert.Equal(20000, corner.Y - origin.Value.Y, 1e-3);

            Assert.Null(DxfPlanReader.PageOriginInDrawing(["0", "SECTION", "2", "HEADER", "9", "$INSUNITS", "70", "4", "0", "ENDSEC", "0", "SECTION", "2", "ENTITIES", "0", "ENDSEC"]));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
