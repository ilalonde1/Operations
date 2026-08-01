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
    public void ProducesThreeSheetLiveWorkbook()
    {
        var bytes = StructuralTakeoffReportGenerator.BuildXlsx(SampleModel(UnitSystem.Imperial));
        Assert.True(bytes.Length > 1000);

        using var wb = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal(3, wb.Worksheets.Count);
        Assert.Contains(wb.Worksheets, w => w.Name == "Takeoff");
        Assert.Contains(wb.Worksheets, w => w.Name == "Basis & Density");
        Assert.Contains(wb.Worksheets, w => w.Name == "Detail (calc)");
    }

    [Fact]
    public void DetailReinforcingIsLiveFormulaOfEditableDensity()
    {
        // The workbook must be calibratable: each line's reinforcing is a formula = concrete × a
        // density looked up from the Basis sheet, where the density cells are editable. We verify the
        // wiring (formulas + the seed densities match the engine) — Excel does the arithmetic.
        var model = SampleModel(UnitSystem.Imperial);
        using var wb = new XLWorkbook(new MemoryStream(StructuralTakeoffReportGenerator.BuildXlsx(model)));

        var detail = wb.Worksheet("Detail (calc)");
        var dataRows = detail.RowsUsed().Skip(2).ToList(); // title + header
        Assert.Equal(model.Result.Lines.Count, dataRows.Count);
        foreach (var dr in dataRows)
        {
            Assert.Contains("VLOOKUP(", dr.Cell(7).FormulaA1, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("*", dr.Cell(8).FormulaA1); // reinforcing = concrete × density
        }

        // The seed density for a known (element, variant) equals the engine's, so the live calc ties.
        var basis = wb.Worksheet("Basis & Density");
        var shearRow = basis.CellsUsed(c => c.GetString() == "Shear").Single().Address.RowNumber;
        Assert.Equal(500, basis.Cell(shearRow, 4).GetDouble(), 0); // shear wall imperial density
    }

    [Fact]
    public void MetricAndImperialBothRender()
    {
        Assert.True(StructuralTakeoffReportGenerator.BuildXlsx(SampleModel(UnitSystem.Metric)).Length > 1000);
        Assert.True(StructuralTakeoffReportGenerator.BuildXlsx(SampleModel(UnitSystem.Imperial)).Length > 1000);
    }
}
