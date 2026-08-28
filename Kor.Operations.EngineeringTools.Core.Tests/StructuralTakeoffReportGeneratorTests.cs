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

    /// <summary>
    /// What the numbers rest on has to be IN the workbook, because the estimator reads the workbook.
    /// Checked by opening the file rather than by reading the writer: the flags printed to a console
    /// nobody sees were, for one afternoon, the only place these lines existed.
    /// </summary>
    [Fact]
    public void WhatTheNumbersRestOnIsWrittenIntoTheWorkbook()
    {
        var basic = SampleModel(UnitSystem.Imperial);
        var model = basic with
        {
            ConcreteBasis = "Concrete volume is the ETABS model's own geometry.",
            FoundationNote = "Footings are below the lowest modelled storey and are not in this model.",
            Assumptions = new List<string>
            {
                "Walls and columns are priced over the full rise to the floor above.",
                "Shear and non-shear walls are not distinguished.",
            },
        };

        using var ms = new MemoryStream(StructuralTakeoffReportGenerator.BuildXlsx(model));
        using var wb = new XLWorkbook(ms);
        var sheet = wb.Worksheet("Basis & Density");

        string all = string.Join("\n", sheet.CellsUsed().Select(c => c.GetString()));

        Assert.Contains("What these numbers rest on", all);
        Assert.Contains("full rise to the floor above", all);
        Assert.Contains("not distinguished", all);
        Assert.Contains("ETABS model's own geometry", all);
        Assert.Contains("below the lowest modelled storey", all);
    }

    [Fact]
    public void AWorkbookWithNothingToDeclareCarriesNoEmptyBasisBlock()
    {
        using var ms = new MemoryStream(StructuralTakeoffReportGenerator.BuildXlsx(SampleModel(UnitSystem.Imperial)));
        using var wb = new XLWorkbook(ms);

        string all = string.Join("\n", wb.Worksheet("Basis & Density").CellsUsed().Select(c => c.GetString()));
        Assert.DoesNotContain("What these numbers rest on", all);
    }
}
