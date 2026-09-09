#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A mark-up is a list of instructions (foundation §3.1, the engineer-to-drafter loop): each
/// annotation with words is classified, its request parsed, its distance taken from the words or
/// from the dimension line drawn beside it, and it is placed on the grid beside the nearest member.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: the words Building C's diagram and MB-6's back-check actually carry — "Move
/// Column right 1'-11"", "Move Gridline down 6.5"", "Align Gridline with multipurpose hall column",
/// a line reading 0'-6 1/2", an ink tick, a comment that asks nothing; the grid reference with its
/// offset; the nearest member within reach. WHAT IT DOES NOT: the real files (the markup-list verb
/// is that measurement); an instruction split across two annotations; a language the vocabulary
/// does not know, which reads as a note and is listed as one.
/// </remarks>
public sealed class AMarkUpIsAListOfInstructionsTests
{
    // ink is read by its size: a tick is 14 x 14 on the paper, a text box 40 x 12
    private static MarkupNote Note(string type, string text, double cx = 0, double cy = 0)
        => new(type, text, "wilma", 1) { Cx = cx, Cy = cy, Width = type == "Ink" ? 14 : 40, Height = type == "Ink" ? 14 : 12 };

    [Theory]
    [InlineData("Move Column right 1'-11\"", "move", "column", "right", 584.2)]
    [InlineData("Move Gridline down 6.5\"", "move", "gridline", "down", 165.1)]
    [InlineData("Move Gridline Left 7\"", "move", "gridline", "left", 177.8)]
    [InlineData("Add 2 dowels each face", "add", "dowel", null, null)]
    [InlineData("Extend wall 300mm to grid 5", "extend", "wall", null, 300.0)]
    public void AnInstructionIsParsedIntoActionSubjectDirectionAndDistance(string text, string action, string subject, string? direction, double? mm)
    {
        var (kind, a, s, d, dist) = MarkupList.Classify(Note("FreeText", text));
        Assert.Equal(MarkupList.Kind.Instruction, kind);
        Assert.Equal(action, a);
        Assert.Equal(subject, s);
        Assert.Equal(direction, d);
        if (mm is null) Assert.Null(dist); else Assert.Equal(mm.Value, dist!.Value, 1);
    }

    [Fact]
    public void ALineWithADimensionMeasuresATickApprovesAndACommentIsANote()
    {
        Assert.Equal(MarkupList.Kind.Measurement, MarkupList.Classify(Note("Line", "0'-6 1/2\"")).Kind);
        Assert.Equal(165.1, MarkupList.Classify(Note("Line", "0'-6 1/2\"")).DistanceMm!.Value, 1);
        Assert.Equal(MarkupList.Kind.Approval, MarkupList.Classify(Note("Ink", ".")).Kind);
        Assert.Equal(MarkupList.Kind.Approval, MarkupList.Classify(Note("Ink", "/")).Kind);
        Assert.Equal(MarkupList.Kind.Note, MarkupList.Classify(Note("FreeText", "Try to show the dowels closer to the centre of the overlapping area.")).Kind);
        Assert.Equal(MarkupList.Kind.Instruction, MarkupList.Classify(Note("FreeText", "Align Gridline with multipurpose hall column")).Kind);
    }

    [Fact]
    public void TheGridReferenceNamesTheNearestAxesAndHowFarOff()
    {
        var axes = new List<GridAxis> { new("4", true, 4000), new("5", true, 12000), new("J", false, 3000), new("K", false, 9000) };
        Assert.Equal("5/J", MarkupList.GridReference(axes, 12100, 2900));
        Assert.Equal("4+2.0 m/K-1.5 m", MarkupList.GridReference(axes, 6000, 7500));
        Assert.Equal("(1.00, 2.00) m", MarkupList.GridReference(Array.Empty<GridAxis>(), 1000, 2000));
    }

    [Fact]
    public void AnInstructionTakesTheDimensionLineDrawnBesideIt()
    {
        // FreeText at (100, 200) pt says move down, a Line at (105, 190) pt reads 0'-6 1/2"; 1:96
        var record = Fixture([Note("FreeText", "Move Gridline down", 100, 200), Note("Line", "0'-6 1/2\"", 105, 190), Note("Ink", "/", 500, 500)]);
        var items = MarkupList.Build(record);
        var move = Assert.Single(items, i => i.Kind == MarkupList.Kind.Instruction);
        Assert.Equal(165.1, move.DistanceMm!.Value, 1);
        Assert.Equal("the line drawn beside it", move.DistanceFrom);
        Assert.Equal("5/J", move.GridRef);
        Assert.StartsWith("column", move.Nearest);
        Assert.Contains(items, i => i.Kind == MarkupList.Kind.Approval);
    }

    /// <summary>A record with a grid and one column beside the note at (100, 200) pt, 1:96 — 3,387 mm x 6,774 mm on the plan.</summary>
    private static SheetRecord Fixture(List<MarkupNote> notes)
    {
        var geometry = new ExtractedGeometry();
        geometry.GridAxes.Add(new GridAxis("5", true, 3400));
        geometry.GridAxes.Add(new GridAxis("J", false, 6800));
        geometry.Columns.Add((3500, 6700));
        geometry.ColumnSizes.Add((600, 600));
        var content = new VectorPageReader.PageContent(1, 3024, 2160, new List<VectorPageReader.TextToken>(), new List<VectorPageReader.GeomPath>());
        return new SheetRecord(1, 3024, 2160, 0, "S2.01", null, "plan", "P1", null, "1/8\" = 1'-0\"", 96,
            geometry, Array.Empty<ScheduleTable>(), Array.Empty<PlanMark>(), Array.Empty<SlabThicknessZoner.Callout>(),
            new GridBubbles.Grid(Array.Empty<GridBubbles.Bubble>(), Array.Empty<double>(), Array.Empty<double>(), Array.Empty<GridBubbles.Axis>()),
            SheetFurniture.Set.Empty, notes, 0, content, Array.Empty<PathFate>(), Array.Empty<WordFate>());
    }
}
