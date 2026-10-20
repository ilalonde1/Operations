using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class ADxfOpensOnItsDrawingTests
{
    [Fact]
    public void TheActiveViewportFramesTheDrawingInItsPageCoordinates()
    {
        var geometry = new ExtractedGeometry { IsVectorPdf = true, ScaleDenominator = 96 };
        geometry.Slabs.Add(new List<(double X, double Y)>
        {
            (50000, 10000), (60000, 10000), (60000, 40000), (50000, 40000),
        });
        geometry.SlabColors.Add((0x80, 0x00, 0x00));

        var lines = DxfExporter.ExportLines(geometry);
        Assert.NotEmpty(lines);
        Assert.Equal(0, lines.Count % 2);
        var groups = Enumerable.Range(0, lines.Count / 2)
            .Select(i => (Code: int.Parse(lines[2 * i], CultureInfo.InvariantCulture), Value: lines[2 * i + 1].Trim()))
            .ToList();
        var tables = Enumerable.Range(0, groups.Count - 1)
            .Where(i => groups[i].Code == 0 && groups[i].Value == "TABLE" && groups[i + 1].Code == 2)
            .Select(i => (Index: i, Name: groups[i + 1].Value)).ToList();
        var viewportTable = Assert.Single(tables, t => t.Name == "VPORT");
        var lineTypeTable = Assert.Single(tables, t => t.Name == "LTYPE");
        Assert.True(viewportTable.Index < lineTypeTable.Index, "VPORT must precede LTYPE.");

        int entryStart = Assert.Single(Enumerable.Range(0, groups.Count),
            i => groups[i].Code == 0 && groups[i].Value == "VPORT");
        int entryEnd = groups.FindIndex(entryStart + 1, g => g.Code == 0);
        Assert.True(entryStart > viewportTable.Index && entryEnd < lineTypeTable.Index);
        Assert.True(entryEnd > entryStart);
        Assert.Equal("ENDTAB", groups[entryEnd].Value);
        var entry = groups.Skip(entryStart + 1).Take(entryEnd - entryStart - 1)
            .ToDictionary(g => g.Code, g => g.Value);

        Assert.Equal("*ACTIVE", entry[2]);
        Assert.InRange(Math.Abs(double.Parse(entry[12], CultureInfo.InvariantCulture) - 55000), 0.0, 0.01);
        Assert.InRange(Math.Abs(double.Parse(entry[22], CultureInfo.InvariantCulture) - 25000), 0.0, 0.01);
        Assert.InRange(double.Parse(entry[40], CultureInfo.InvariantCulture), 30000.0, double.MaxValue);
    }
}
