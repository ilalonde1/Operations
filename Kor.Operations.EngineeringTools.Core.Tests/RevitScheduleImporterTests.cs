using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// Reading the CSVs Revit actually exports.
///
/// Shapes taken from the real 31065 exports: a title row, the header on the second row, a blank
/// third, the level named differently in every category, and the unit written inside each cell.
/// </summary>
public sealed class RevitScheduleImporterTests
{
    private readonly ITestOutputHelper _out;
    public RevitScheduleImporterTests(ITestOutputHelper output) => _out = output;

    private static string Csv(params string[] lines) => string.Join("\r\n", lines);

    private static RevitScheduleImportResult Read(string name, string csv) =>
        RevitScheduleImporter.Import(new[] { (name, csv) });

    private const string Floors =
        "Floor Schedule,,\r\n" +
        "Level,Volume,Structural Material\r\n" +
        ",,\r\n" +
        "LEVEL P3,489.03 m³,<varies>\r\n" +
        "LEVEL P2,785.48 m³,30 MPa Floor\r\n";

    [Fact]
    public void TheHeaderIsTheSecondRowAndTheFirstRowSaysWhatTheElementsAre()
    {
        var r = Read("floors.csv", Floors);

        Assert.Equal(2, r.Inputs.Count);
        Assert.All(r.Inputs, i => Assert.Equal(TakeoffElementType.Slab, i.Element));
        Assert.Contains(r.Notes, n => n.Contains("read as Slab from the schedule title \"Floor Schedule\""));

        var p3 = Assert.Single(r.Inputs, i => i.Level == "LEVEL P3");
        Assert.Equal(489.03, p3.ConcreteVolume, 6);
    }

    [Theory]
    [InlineData("Floor Schedule", TakeoffElementType.Slab)]
    [InlineData("Wall Schedule 2", TakeoffElementType.Wall)]
    [InlineData("Structural Column Schedule 2", TakeoffElementType.Column)]
    [InlineData("Structural Foundation Schedule 2", TakeoffElementType.Foundation)]
    [InlineData("Structural Framing Schedule", TakeoffElementType.Beam)]
    [InlineData("Column Footing Schedule", TakeoffElementType.Foundation)]   // footing wins over column
    public void TheScheduleTitleNamesTheElement(string title, TakeoffElementType expected)
    {
        var r = Read("s.csv", Csv(title + ",", "Level,Volume", ",", "LEVEL 1,10 m³"));

        var only = Assert.Single(r.Inputs);
        Assert.Equal(expected, only.Element);
    }

    [Theory]
    [InlineData("Level,Volume", "LEVEL P3,271 m³")]
    [InlineData("Base Constraint,Volume", "LEVEL P3,271 m³")]
    [InlineData("Base Level,Volume", "LEVEL P3,271 m³")]
    [InlineData("Volume,Base Constraint", "271 m³,LEVEL P3")]                // walls put volume first
    public void TheLevelIsFoundWhateverRevitCallsItAndWhereverItPutIt(string header, string row)
    {
        var r = Read("w.csv", Csv("Wall Schedule 2,", header, ",", row));

        var only = Assert.Single(r.Inputs);
        Assert.Equal("LEVEL P3", only.Level);
        Assert.Equal(271, only.ConcreteVolume, 6);
    }

    // ---------------------------------------------------------------------------------------
    // The grand total is a duplicate of the category, and also a free check on our reading.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void RevitsOwnGrandTotalIsNotPricedAsAStorey()
    {
        // Priced as a row this added the whole category a second time: on 31065 it put 10,135.6 m³
        // on a storey called "Grand total: 140" and made the building 63% too big.
        var r = Read("floors.csv", Floors + "Grand total: 2,1274.51 m³,\r\n");

        Assert.DoesNotContain(r.Inputs, i => i.Level.Contains("total", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(489.03 + 785.48, r.Inputs.Sum(i => i.ConcreteVolume), 6);
    }

    [Fact]
    public void AGrandTotalThatAgreesWithTheRowsIsReportedAsAgreeing()
    {
        var r = Read("floors.csv", Floors + "Grand total: 2,1274.51 m³,\r\n");

        Assert.Contains(r.Notes, n => n.Contains("matching the schedule's own grand total"));
        Assert.DoesNotContain(r.Notes, n => n.StartsWith("WARNING", StringComparison.Ordinal));
    }

    [Fact]
    public void AGrandTotalThatDoesNotAgreeIsAWarningBecauseRowsAreBeingMissed()
    {
        var r = Read("floors.csv", Floors + "Grand total: 2,9999.00 m³,\r\n");

        var warning = Assert.Single(r.Notes, n => n.StartsWith("WARNING", StringComparison.Ordinal));
        Assert.Contains("9,999.0", warning);
        Assert.Contains("Rows are being missed or misread", warning);
    }

    // ---------------------------------------------------------------------------------------
    // What it refuses to invent.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ConcreteOnARowWithNoLevelIsNotPlacedAndItsVolumeIsSaidOutLoud()
    {
        // 21 foundation rows on the real 31065 export state no level and carry 76 m³ between them.
        // A count of skipped rows tells an estimator nothing; the volume tells them whether to chase it.
        var r = Read("f.csv", Csv(
            "Structural Foundation Schedule 2,",
            "Level,Volume",
            ",",
            "LEVEL P3,28 m³",
            ",9 m³",
            ",3 m³"));

        var only = Assert.Single(r.Inputs);
        Assert.Equal(28, only.ConcreteVolume, 6);

        Assert.Equal(12, r.UnplacedVolume, 6);
        Assert.Contains(r.Notes, n => n.StartsWith("WARNING", StringComparison.Ordinal) && n.Contains("12.0"));
        Assert.Equal(2, r.Residual.Count);
    }

    [Fact]
    public void VariesIsRevitDecliningToAnswerAndIsNotTurnedIntoAGrade()
    {
        var r = Read("floors.csv", Floors);

        Assert.Equal("", Assert.Single(r.Inputs, i => i.Level == "LEVEL P3").Grade);
        Assert.Equal("30 MPa Floor", Assert.Single(r.Inputs, i => i.Level == "LEVEL P2").Grade);
    }

    [Fact]
    public void TheUnitIsReadFromTheCellsRatherThanAssumed()
    {
        Assert.Equal(UnitSystem.Metric, Read("f.csv", Floors).Unit);

        var imperial = Read("f.csv", Csv("Floor Schedule,", "Level,Volume", ",", "LEVEL 1,640 yd³"));
        Assert.Equal(UnitSystem.Imperial, imperial.Unit);
        Assert.Contains(imperial.Notes, n => n.Contains("Volumes are in yd³"));
    }

    [Fact]
    public void ExportsThatDisagreeOnTheirUnitAreAWarningNotAQuietAverage()
    {
        var r = RevitScheduleImporter.Import(new[]
        {
            ("metric.csv", Csv("Floor Schedule,", "Level,Volume", ",", "LEVEL 1,100 m³")),
            ("imperial.csv", Csv("Wall Schedule,", "Level,Volume", ",", "LEVEL 1,100 yd³")),
        });

        Assert.Contains(r.Notes, n => n.StartsWith("WARNING", StringComparison.Ordinal) && n.Contains("do not agree on a unit"));
    }

    // ---------------------------------------------------------------------------------------
    // Found by the adversarial audit, 2026-08-27.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void CubicFeetArePricedAsCubicFeetNotAsCubicMetres()
    {
        // Only "yd" and "CY" counted as imperial, so everything else fell through to metric and
        // 100 cubic feet was priced as 100 cubic metres — thirty-five times over, nothing red.
        var r = Read("f.csv", Csv("Floor Schedule,", "Level,Volume", ",", "LEVEL 1,270 ft³"));

        Assert.Equal(UnitSystem.Imperial, r.Unit);
        Assert.Equal(10.0, Assert.Single(r.Inputs).ConcreteVolume, 6);      // 270 ft³ = 10 yd³
        Assert.Contains(r.Notes, n => n.Contains("Cubic feet in the export were converted"));
    }

    [Theory]
    [InlineData("ft3")]
    [InlineData("ft^3")]
    [InlineData("CF")]
    [InlineData("cu ft")]
    public void EverySpellingOfCubicFeetIsRecognised(string unit)
    {
        var r = Read("f.csv", Csv("Floor Schedule,", "Level,Volume", ",", $"LEVEL 1,270 {unit}"));

        Assert.Equal(UnitSystem.Imperial, r.Unit);
        Assert.Equal(10.0, Assert.Single(r.Inputs).ConcreteVolume, 6);
    }

    [Fact]
    public void AVolumeUnitThisReaderDoesNotKnowIsRefusedRatherThanTakenAsMetric()
    {
        var r = Read("f.csv", Csv("Floor Schedule,", "Level,Volume", ",", "LEVEL 1,100 barrels"));

        Assert.Empty(r.Inputs);
        Assert.Contains(r.Residual, x => x.Note.Contains("not one this reader knows"));
        Assert.Contains(r.Notes, n => n.StartsWith("WARNING", StringComparison.Ordinal) && n.Contains("barrels"));
    }

    [Fact]
    public void AEuropeanDecimalIsRefusedBecauseACommaCannotBeToldFromASeparator()
    {
        // Stripping the comma turned "1.234,56" into 1.23456 — a thousandth of the figure — and if
        // the grand total used the same format the self-check agreed with it.
        var r = Read("f.csv", Csv("Floor Schedule,", "Level,Volume", ",", "LEVEL 1,\"1.234,56 m³\""));

        Assert.Empty(r.Inputs);
        Assert.Single(r.Residual);
    }

    [Fact]
    public void AnAmericanThousandsSeparatorStillReads()
    {
        var r = Read("f.csv", Csv("Floor Schedule,", "Level,Volume", ",", "LEVEL 1,\"1,234.56 m³\""));

        Assert.Equal(1234.56, Assert.Single(r.Inputs).ConcreteVolume, 6);
    }

    [Fact]
    public void AFileThatIsNotAQuantityScheduleIsRefusedWithTheReason()
    {
        var r = Read("notes.csv", Csv("Some Notes,", "Sheet,Comment", ",", "S2.01,check this"));

        Assert.Empty(r.Inputs);
        var why = Assert.Single(r.Residual);
        Assert.Contains("not a Revit quantity schedule", why.Note);
    }

    [Fact]
    public void AScheduleWhoseTitleDoesNotNameACategoryIsRefusedRatherThanGuessed()
    {
        var r = Read("mystery.csv", Csv("Sheet1,", "Level,Volume", ",", "LEVEL 1,10 m³"));

        Assert.Empty(r.Inputs);
        Assert.Contains(r.Residual, x => x.Note.Contains("does not say what the elements are"));
    }

    [Fact]
    public void RowsAreRolledUpPerLevelElementAndGrade()
    {
        var r = Read("c.csv", Csv(
            "Structural Column Schedule 2,",
            "Base Level,Volume",
            ",",
            "LEVEL P3,5 m³",
            "LEVEL P3,5 m³",
            "LEVEL P3,4 m³",
            "LEVEL P2,3 m³"));

        Assert.Equal(2, r.Inputs.Count);
        Assert.Equal(14, Assert.Single(r.Inputs, i => i.Level == "LEVEL P3").ConcreteVolume, 6);
        Assert.Equal(4, r.RowsRead);
    }
}
