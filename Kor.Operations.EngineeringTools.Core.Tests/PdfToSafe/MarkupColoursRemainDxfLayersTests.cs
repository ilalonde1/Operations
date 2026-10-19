using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

public sealed class MarkupColoursRemainDxfLayersTests
{
    [Fact]
    public void ColourLayersKeepBothColoursWithoutStructuralLayerNames()
    {
        var geometry = new ExtractedGeometry { IsVectorPdf = true, ScaleDenominator = 96 };
        geometry.Slabs.Add(new List<(double X, double Y)>
        {
            (0, 0), (1000, 0), (1000, 1000), (0, 1000),
        });
        geometry.SlabColors.Add((0x80, 0x00, 0x00));
        geometry.Slabs.Add(new List<(double X, double Y)>
        {
            (2000, 0), (3000, 0), (3000, 1000), (2000, 1000),
        });
        geometry.SlabColors.Add((0x00, 0x40, 0x00));

        var lines = DxfExporter.ExportLines(geometry, layerByColour: true);
        Assert.NotEmpty(lines);
        var declaredLayers = new List<string>();
        var entityLayers = new HashSet<string>(StringComparer.Ordinal);
        bool layerRecord = false;
        for (int i = 0; i + 1 < lines.Count; i += 2)
        {
            string code = lines[i].Trim();
            string value = lines[i + 1].Trim();
            if (code == "0") layerRecord = value == "LAYER";
            else if (layerRecord && code == "2") declaredLayers.Add(value);
            if (code == "8") entityLayers.Add(value);
        }

        string[] expected = { "PDF-004000", "PDF-800000" };
        Assert.Equal(expected, declaredLayers.Where(l => l.StartsWith("PDF-", StringComparison.Ordinal))
            .OrderBy(l => l, StringComparer.Ordinal).ToArray());
        Assert.Equal(expected, entityLayers.Where(l => l != "0").OrderBy(l => l, StringComparer.Ordinal).ToArray());
        foreach (string layer in new[] { "SLAB", "BEAM", "COLUMN" })
        {
            Assert.DoesNotContain(layer, declaredLayers);
            Assert.DoesNotContain(layer, entityLayers);
        }
    }
}
