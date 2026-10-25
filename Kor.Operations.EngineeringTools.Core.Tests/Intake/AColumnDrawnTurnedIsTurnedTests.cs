#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A column's turn is carried to ETABS (intake step 178, 2026-10-25). ExtractedGeometry.ColumnTurnDegrees is the bearing of
/// the side ColumnSizes calls width; the DXF draws the footprint at that turn and the classifier's oriented box reads it
/// back as the column's angle. The Revit route fills it from the Virtual Drafter's manifest (rotation_deg); the PDF reader
/// writes 0 until its own reading of a turned column (step 181) is finished.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a 12 x 30 carried at 30 degrees - written turned, read back by the classifier as a 12 x 30 at 30
/// degrees. WHAT IT DOES NOT: where the turn comes from (the manifest read is RevitToDxfVerb's; the PDF reader's is step
/// 181, parked); the ETABS angle written from it (AColumnsAngIsTheBearingOfItsLongFace); the view split carrying it
/// (EveryListParallelToAMemberListGoesWithItIntoTheView). A fault it would NOT catch: a turn measured in a frame whose y
/// axis runs the other way - the DXF and the classifier share one frame.
/// </remarks>
public sealed class AColumnDrawnTurnedIsTurnedTests
{
    [Fact]
    public void ATurnedColumnLeavesTheReaderTurnedAndTheClassifierReadsItsBearing()
    {
        var g = new ExtractedGeometry();
        g.Columns.Add((5000, 5000)); g.ColumnColors.Add((0xD0, 0xD0, 0xD0)); g.ColumnIsAnnotation.Add(false);
        g.ColumnSizes.Add((762, 305)); g.ColumnIsRound.Add(false); g.ColumnTurnDegrees.Add(30);
        string path = Path.Combine(Path.GetTempPath(), $"turned-column-{Guid.NewGuid():N}.dxf");
        try
        {
            DxfExporter.Export(g, path, korLayers: true);
            var inches = DxfPlanReader.ReadSegments(path)
                .Select(s => s with { Start = new DxfPoint(s.Start.X / 25.4, s.Start.Y / 25.4), End = new DxfPoint(s.End.X / 25.4, s.End.Y / 25.4) })
                .ToList();
            var column = Assert.Single(StructuralPlanClassifier.Classify(inches, new PlanClassificationOptions()).Columns);
            Assert.InRange(column.Depth, 29.5, 30.5);
            Assert.InRange(column.Width, 11.5, 12.5);
            Assert.InRange(((column.AxisAngleDegrees % 180) + 180) % 180, 29, 31);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AColumnSquareToThePageIsWrittenAsBefore()
    {
        var g = new ExtractedGeometry();
        g.Columns.Add((5000, 5000)); g.ColumnColors.Add((0xD0, 0xD0, 0xD0)); g.ColumnIsAnnotation.Add(false);
        g.ColumnSizes.Add((305, 762)); g.ColumnIsRound.Add(false); g.ColumnTurnDegrees.Add(0);
        string path = Path.Combine(Path.GetTempPath(), $"square-column-{Guid.NewGuid():N}.dxf");
        try
        {
            DxfExporter.Export(g, path, korLayers: true);
            var xs = DxfPlanReader.ReadSegments(path).SelectMany(s => new[] { s.Start, s.End }).ToList();
            Assert.Equal(305, xs.Max(p => p.X) - xs.Min(p => p.X), 3);   // width along X, depth along Y: square to the page
            Assert.Equal(762, xs.Max(p => p.Y) - xs.Min(p => p.Y), 3);
        }
        finally { File.Delete(path); }
    }
}
