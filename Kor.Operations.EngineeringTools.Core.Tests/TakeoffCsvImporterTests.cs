#nullable enable
using System;
using System.Linq;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class TakeoffCsvImporterTests
{
    [Fact]
    public void Imports_rows_uses_supplied_rebar_and_estimates_when_blank()
    {
        var csv =
            "Level,Element,Grade,ConcreteM3,RebarKg\n" +
            "P3,Slab,C30,100,\n" +          // rebar blank -> estimated (slab density 100)
            "L1,Column,C40,10,2500\n";      // rebar supplied -> used as-is

        var lines = TakeoffCsvImporter.Import(csv, RebarDensityTable.Default).ToList();

        Assert.Equal(2, lines.Count);

        var slab = lines.Single(l => l.ElementType == TakeoffElementType.Slab);
        Assert.Equal("P3", slab.Level);
        Assert.Equal(100, slab.ConcreteM3, 3);
        Assert.Equal(10000, slab.RebarKg, 3);            // 100 m³ × 100 kg/m³
        Assert.Equal(RebarSource.Density, slab.RebarSource);
        Assert.False(slab.Unresolved);

        var col = lines.Single(l => l.ElementType == TakeoffElementType.Column);
        Assert.Equal(2500, col.RebarKg, 3);              // supplied, not estimated
        Assert.Equal(RebarSource.Modeled, col.RebarSource);
    }

    [Fact]
    public void Flexible_headers_and_units_are_tolerated()
    {
        var csv =
            "Element,Level,Concrete (m³),Grade\n" +
            "Wall,P2,3.75,C30\n";

        var line = Assert.Single(TakeoffCsvImporter.Import(csv, RebarDensityTable.Default));
        Assert.Equal(TakeoffElementType.Wall, line.ElementType);
        Assert.Equal(3.75, line.ConcreteM3, 3);
        Assert.Equal(3.75 * 120, line.RebarKg, 3);       // wall density 120
    }

    [Fact]
    public void Bad_concrete_value_becomes_unresolved_and_excluded()
    {
        var csv =
            "Level,Element,Grade,ConcreteM3\n" +
            "P1,Slab,C30,\n";                // missing volume

        var line = Assert.Single(TakeoffCsvImporter.Import(csv, RebarDensityTable.Default));
        Assert.True(line.Unresolved);
        Assert.Equal(0, line.ConcreteM3, 3);
    }

    [Fact]
    public void EndToEnd_two_revit_schedules_produce_a_client_delta_report()
    {
        // Two issues straight from Revit concrete schedules.
        var ift =
            "Level,Element,Grade,ConcreteM3\n" +
            "P3,Slab,C30,200\n" +
            "L1,Slab,C40,150\n";          // L1 transfer slab at IFT
        var ifc =
            "Level,Element,Grade,ConcreteM3\n" +
            "P3,Slab,C30,200\n" +         // unchanged
            "L1,Slab,C40,130\n" +         // transfer slab reduced
            "P1,Slab,C30,40\n";           // plenum slab ADDED at IFC

        var before = TakeoffCsvImporter.Import(ift, RebarDensityTable.Default);
        var after = TakeoffCsvImporter.Import(ifc, RebarDensityTable.Default);

        var diff = TakeoffDiffService.Compare(before, after, basisBefore: "rev", basisAfter: "rev");

        // P3 unchanged (0), L1 −20, P1 +40  =>  net +20 m³
        Assert.Equal(20, diff.TotalConcreteDeltaM3, 3);
        Assert.Contains("P1", diff.AddedLevels);
        Assert.False(diff.BasisMismatch);

        // And it flows all the way to the client report.
        var model = new TakeoffReportModel(
            "31065-01", "5380 Heather Street", "IFT Addendum", "IFC",
            new DateTime(2026, 4, 6), diff);

        var html = TakeoffReportGenerator.BuildHtml(model);
        Assert.Contains("for information only", html);
        Assert.Contains("31065-01", html);
        Assert.Contains("P1", html);
    }
}
