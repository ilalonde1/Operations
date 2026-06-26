#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class StructuralTakeoffReportGeneratorTests
{
    private static StructuralTakeoffReportModel SampleModel(UnitSystem unit)
    {
        var table = unit == UnitSystem.Imperial
            ? StructuralDensityTable.KorImperialDefault
            : StructuralDensityTable.KorMetricDefault;
        var inputs = new List<StructuralTakeoffInput>
        {
            new("L1", TakeoffElementType.Slab, "Parking", 200, FormworkArea: 1000),
            new("L1", TakeoffElementType.Column, null, 30),
            new("L2", TakeoffElementType.Slab, "Residential", 180, FormworkArea: 950),
            new("L2", TakeoffElementType.Wall, "Shear", 40),
        };
        var result = StructuralTakeoffService.Compute(inputs, table);
        return new StructuralTakeoffReportModel("31065", "5380 Heather", "IFC", new DateTime(2026, 3, 6), result);
    }

    [Fact]
    public void ProducesTwoSheetWorkbook()
    {
        var bytes = StructuralTakeoffReportGenerator.BuildXlsx(SampleModel(UnitSystem.Imperial));
        Assert.True(bytes.Length > 1000);

        using var wb = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal(2, wb.Worksheets.Count);
        Assert.Contains(wb.Worksheets, w => w.Name == "Takeoff");
        Assert.Contains(wb.Worksheets, w => w.Name == "Basis & Density");
    }

    [Fact]
    public void ReportTotalReinforcingTiesToEngine()
    {
        var model = SampleModel(UnitSystem.Imperial);
        var bytes = StructuralTakeoffReportGenerator.BuildXlsx(model);

        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet("Takeoff");
        var label = ws.CellsUsed(c => c.GetString() == "Total reinforcing").Single();
        double reported = ws.Cell(label.Address.RowNumber, 2).GetDouble();
        Assert.Equal(Math.Round(model.Result.TotalRebarWeight), reported, 0);
    }

    [Fact]
    public void MetricAndImperialBothRender()
    {
        Assert.True(StructuralTakeoffReportGenerator.BuildXlsx(SampleModel(UnitSystem.Metric)).Length > 1000);
        Assert.True(StructuralTakeoffReportGenerator.BuildXlsx(SampleModel(UnitSystem.Imperial)).Length > 1000);
    }
}
