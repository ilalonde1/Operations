#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A grid axis is a named line (brief 22): its name is the bubble's label, its position the rule
/// through the bubble. The record carries them as Geometry.GridAxes and the DXF gets a GRID layer —
/// one LINE across the drawn extent and the name at both ends — which the ETABS side already reads
/// as a grid by layer name. The line pieces the drafter drew are read, not discarded.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: the DXF layer table and entities for a geometry with and without axes, the
/// recentring the exporter applies to everything, and the GridAxis fate. WHAT IT DOES NOT: real
/// drawings — FiveStickFilesTests banks the named axes on the five schedule pages.
/// </remarks>
public sealed class AGridAxisIsANamedLineTests
{
    private static List<(string Kind, string Layer, List<(int Code, string Value)> Groups)> Entities(string dxf)
    {
        var lines = File.ReadAllLines(dxf);
        var entities = new List<(string, string, List<(int, string)>)>();
        int i = Array.IndexOf(lines, "ENTITIES");
        for (; i < lines.Length - 1; i++)
        {
            if (lines[i].Trim() != "0") continue;
            string kind = lines[i + 1].Trim();
            if (kind is "ENDSEC" or "EOF") break;
            var groups = new List<(int, string)>();
            string layer = "?";
            for (int j = i + 2; j < lines.Length - 1 && lines[j].Trim() != "0"; j += 2)
            {
                int code = int.Parse(lines[j].Trim());
                groups.Add((code, lines[j + 1].Trim()));
                if (code == 8) layer = lines[j + 1].Trim();
            }
            entities.Add((kind, layer, groups));
        }
        return entities;
    }

    private static ExtractedGeometry Geometry(bool withAxes)
    {
        var g = new ExtractedGeometry();
        g.Slabs.Add(new List<(double X, double Y)> { (10000, 20000), (30000, 20000), (30000, 32000), (10000, 32000) });
        g.SlabColors.Add((0xD0, 0xD0, 0xD0));
        if (withAxes)
        {
            g.GridAxes.Add(new GridAxis("3", Vertical: true, AtMm: 15000));
            g.GridAxes.Add(new GridAxis("B", Vertical: false, AtMm: 26000));
        }
        return g;
    }

    [Fact]
    public void TheDxfCarriesEachAxisAsALineWithItsNameAtBothEndsOnAGridLayer()
    {
        string path = Path.Combine(Path.GetTempPath(), $"grid-axes-{Guid.NewGuid():N}.dxf");
        try
        {
            DxfExporter.Export(Geometry(withAxes: true), path);
            string text = File.ReadAllText(path);
            Assert.Contains("\nGRID\n", text.Replace("\r\n", "\n"));
            var entities = Entities(path);
            var gridLines = entities.Where(e => e.Kind == "LINE" && e.Layer == "GRID").ToList();
            var gridText = entities.Where(e => e.Kind == "TEXT" && e.Layer == "GRID").ToList();
            Assert.Equal(2, gridLines.Count);
            Assert.Equal(4, gridText.Count);
            Assert.Equal(2, gridText.Count(t => t.Groups.Any(gp => gp.Code == 1 && gp.Value == "3")));
            Assert.Equal(2, gridText.Count(t => t.Groups.Any(gp => gp.Code == 1 && gp.Value == "B")));

            // the exporter recentres everything on the drawing's weighted centroid; the axes move with
            // the slab, so they are measured against its written vertices, not against the input frame
            double X(List<(int Code, string Value)> g, int code) => double.Parse(g.First(gp => gp.Code == code).Value, System.Globalization.CultureInfo.InvariantCulture);
            var vertices = entities.Where(e => e.Kind == "VERTEX").ToList();
            Assert.Equal(4, vertices.Count);
            double slabLeft = vertices.Min(v => X(v.Groups, 10)), slabBottom = vertices.Min(v => X(v.Groups, 20)), slabTop = vertices.Max(v => X(v.Groups, 20));
            var vertical = gridLines.Single(l => Math.Abs(X(l.Groups, 10) - X(l.Groups, 11)) < 1e-6);
            Assert.Equal(15000 - 10000, X(vertical.Groups, 10) - slabLeft, 1);
            var horizontal = gridLines.Single(l => Math.Abs(X(l.Groups, 20) - X(l.Groups, 21)) < 1e-6);
            Assert.Equal(26000 - 20000, X(horizontal.Groups, 20) - slabBottom, 1);
            // and the line runs past the drawn extent on both sides
            Assert.True(X(vertical.Groups, 20) < slabBottom && X(vertical.Groups, 21) > slabTop);
            // the two names stand at the line's two ends, not somewhere on the layer (audit Q7)
            var threes = gridText.Where(t => t.Groups.Any(gp => gp.Code == 1 && gp.Value == "3")).ToList();
            Assert.Contains(threes, t => Math.Abs(X(t.Groups, 10) - X(vertical.Groups, 10)) < 1e-6 && Math.Abs(X(t.Groups, 20) - X(vertical.Groups, 20)) < 1e-6);
            Assert.Contains(threes, t => Math.Abs(X(t.Groups, 10) - X(vertical.Groups, 11)) < 1e-6 && Math.Abs(X(t.Groups, 20) - X(vertical.Groups, 21)) < 1e-6);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WithoutAxesThereIsNoGridLayer()
    {
        string path = Path.Combine(Path.GetTempPath(), $"grid-axes-{Guid.NewGuid():N}.dxf");
        try
        {
            DxfExporter.Export(Geometry(withAxes: false), path);
            Assert.DoesNotContain("\nGRID\n", File.ReadAllText(path).Replace("\r\n", "\n"));
            Assert.DoesNotContain(Entities(path), e => e.Layer == "GRID");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AGridLinePieceIsReadNotDiscarded()
    {
        Assert.Equal(Disposition.Read, PathFate.DispositionOf(PathReason.GridAxis));
    }
}
