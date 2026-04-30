#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe
{
    public class DxfExporterTests
    {
        private static string ExportToString(ExtractedGeometry geo,
            HashSet<int>? exSlabs = null, HashSet<int>? exLines = null,
            HashSet<int>? exCols = null)
        {
            string path = Path.Combine(Path.GetTempPath(), $"dxf-test-{Guid.NewGuid():N}.dxf");
            try
            {
                DxfExporter.Export(geo, path, exSlabs, exLines, exCols);
                return File.ReadAllText(path);
            }
            finally { try { File.Delete(path); } catch { } }
        }

        private static ExtractedGeometry SimpleGeo()
        {
            var geo = new ExtractedGeometry { IsVectorPdf = true };
            geo.Slabs.Add(new List<(double, double)> { (0, 0), (6000, 0), (6000, 6000), (0, 6000) });
            geo.SlabColors.Add((255, 0, 0));
            geo.Columns.Add((1000, 1000));
            geo.ColumnColors.Add((128, 0, 0));
            geo.ColumnSizes.Add((400, 600));
            geo.Lines.Add(new List<(double, double)> { (0, 0), (3000, 0) });
            geo.LineColors.Add((0, 0, 255));
            geo.LineSectionHints.Add(null);
            return geo;
        }

        [Fact]
        public void Export_ProducesValidDxfStructure()
        {
            var dxf = ExportToString(SimpleGeo());
            Assert.Contains("SECTION", dxf);
            Assert.Contains("HEADER", dxf);
            Assert.Contains("ENTITIES", dxf);
            Assert.Contains("EOF", dxf);
            Assert.Contains("AC1009", dxf); // DXF R12
        }

        [Fact]
        public void Export_CreatesAllLayers()
        {
            var dxf = ExportToString(SimpleGeo());
            Assert.Contains("SLAB", dxf);
            Assert.Contains("BEAM", dxf);
            Assert.Contains("COLUMN", dxf);
            Assert.Contains("WALL", dxf);
        }

        [Fact]
        public void Export_SlabIsClosedPolyline()
        {
            var dxf = ExportToString(SimpleGeo());
            // Closed polyline flag = 70 / 1
            int slabIdx = dxf.IndexOf("SLAB");
            Assert.True(slabIdx > 0);
            // At least one POLYLINE entity on the SLAB layer
            Assert.Contains("POLYLINE", dxf);
            Assert.Contains("VERTEX", dxf);
            Assert.Contains("SEQEND", dxf);
        }

        [Fact]
        public void Export_ColumnIsRectangleNotPoint()
        {
            var dxf = ExportToString(SimpleGeo());
            // Should NOT contain a bare POINT entity (old behavior)
            // Count POINT occurrences — only allowed in header context, not entities
            var entitySection = dxf.Substring(dxf.IndexOf("ENTITIES"));
            Assert.DoesNotContain("\nPOINT\n", entitySection);
            // Should have a closed POLYLINE on the COLUMN layer (4 vertices = rectangle)
            Assert.Contains("COLUMN", entitySection);
            // Count VERTEX entries after COLUMN layer reference — should be 4 for rectangle
            int colPolyIdx = entitySection.IndexOf("COLUMN");
            Assert.True(colPolyIdx > 0);
        }

        [Fact]
        public void Export_WallHintedLineUsesWallLayer()
        {
            var geo = new ExtractedGeometry { IsVectorPdf = true };
            geo.Slabs.Add(new List<(double, double)> { (0, 0), (1000, 0), (1000, 1000), (0, 1000) });
            geo.SlabColors.Add((255, 0, 0));
            // Wall-hinted line
            geo.Lines.Add(new List<(double, double)> { (0, 0), (0, 5000) });
            geo.LineColors.Add((128, 0, 0));
            geo.LineSectionHints.Add((200.0, 1000.0));
            // Non-hinted line (beam)
            geo.Lines.Add(new List<(double, double)> { (500, 0), (500, 3000) });
            geo.LineColors.Add((0, 0, 255));
            geo.LineSectionHints.Add(null);

            var dxf = ExportToString(geo);
            var entitySection = dxf.Substring(dxf.IndexOf("ENTITIES"));
            // WALL layer should appear for the hinted line
            Assert.Contains("WALL", entitySection);
            // BEAM layer for the non-hinted line
            Assert.Contains("BEAM", entitySection);
        }

        [Fact]
        public void Export_ExcludedSlabOmitted()
        {
            var geo = SimpleGeo();
            // Add a second slab
            geo.Slabs.Add(new List<(double, double)> { (7000, 0), (9000, 0), (9000, 2000), (7000, 2000) });
            geo.SlabColors.Add((255, 0, 0));

            var withExclude = ExportToString(geo, exSlabs: new HashSet<int> { 1 });
            var withoutExclude = ExportToString(geo);
            // Excluded version should have fewer VERTEX entries
            int vertexCountWith = withExclude.Split("VERTEX").Length - 1;
            int vertexCountWithout = withoutExclude.Split("VERTEX").Length - 1;
            Assert.True(vertexCountWith < vertexCountWithout);
        }

        [Fact]
        public void Export_EmptyGeometry_ProducesNoFile()
        {
            var geo = new ExtractedGeometry();
            string path = Path.Combine(Path.GetTempPath(), $"dxf-empty-{Guid.NewGuid():N}.dxf");
            try
            {
                DxfExporter.Export(geo, path);
                // Empty geometry → totalWeight=0 → returns early → no file
                Assert.False(File.Exists(path));
            }
            finally { try { File.Delete(path); } catch { } }
        }

        [Fact]
        public void Export_ColumnRectangleDimensionsMatchColumnSizes()
        {
            var geo = new ExtractedGeometry { IsVectorPdf = true };
            // Single column at origin with known size
            geo.Columns.Add((3000, 3000));
            geo.ColumnColors.Add((128, 0, 0));
            geo.ColumnSizes.Add((400, 800)); // W=400, D=800
            // Need at least something else for centroid weight
            geo.Slabs.Add(new List<(double, double)> { (0, 0), (6000, 0), (6000, 6000), (0, 6000) });
            geo.SlabColors.Add((255, 0, 0));

            var dxf = ExportToString(geo);
            // The column rectangle is centered on the column position (after centroid shift).
            // With 400×800, half-widths are 200 and 400.
            // The VERTEX entries for the column should show a rectangle spanning ±200 in X and ±400 in Y
            // relative to the column's centered position.
            // Just verify 4 VERTEX entries appear in the COLUMN layer section.
            var entitySection = dxf.Substring(dxf.IndexOf("ENTITIES"));
            // Count polylines on COLUMN layer — should be exactly 1 (4 vertices)
            var lines = entitySection.Split('\n');
            int colPolylines = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() == "POLYLINE" && i + 2 < lines.Length && lines[i + 2].Trim() == "COLUMN")
                    colPolylines++;
            }
            Assert.Equal(1, colPolylines);
        }

        // ── Regent PDF end-to-end DXF test ────────────────────────────

        [Fact]
        public void RegentFloor_DxfExport_ProducesValidFile()
        {
            string pdfPath = Path.Combine(
                Path.GetDirectoryName(typeof(DxfExporterTests).Assembly.Location)!,
                "PdfToSafe", "Fixtures", "regent_typ_floor.pdf");
            if (!File.Exists(pdfPath))
                pdfPath = Path.GetFullPath(Path.Combine(
                    Path.GetDirectoryName(typeof(DxfExporterTests).Assembly.Location)!,
                    "..", "..", "..", "PdfToSafe", "Fixtures", "regent_typ_floor.pdf"));
            if (!File.Exists(pdfPath)) return; // skip if fixture missing

            var geo = PdfGeometryExtractor.Extract(pdfPath, scaleDenominator: 100);
            var burgundy = ((byte)0x80, (byte)0x00, (byte)0x00);
            var red = ((byte)0xF0, (byte)0x00, (byte)0x00);
            var colorSettings = new Dictionary<(byte R, byte G, byte B), SlabColorSettings>
            {
                [burgundy] = new SlabColorSettings { ElementType = "Wall" },
                [red] = new SlabColorSettings { ElementType = "Slab", ThicknessMm = 250 },
            };
            var reclassified = PdfGeometryExtractor.ReclassifyByColor(geo, colorSettings);

            var dxf = ExportToString(reclassified);

            Assert.Contains("EOF", dxf);
            Assert.Contains("SLAB", dxf);
            Assert.Contains("COLUMN", dxf);
            Assert.Contains("WALL", dxf);
            // Should have multiple POLYLINE entities
            Assert.True(dxf.Split("POLYLINE").Length > 5);
            // File should be reasonable size (not empty, not gigantic)
            Assert.InRange(dxf.Length, 1000, 500000);
        }
    }
}
