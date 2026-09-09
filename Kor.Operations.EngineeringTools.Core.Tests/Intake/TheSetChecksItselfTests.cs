#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// The set checks itself (foundation §3.3): the gatekeeper's findings from what the record holds.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a sheet with no number, a scale conflict, a plan with no scale, marks declared
/// but never placed, a column of an undeclared size, a footing box no label names, a label no
/// footing answers, a grid name drawn twice, a sheet number used twice, and a plan whose grid axis
/// sits elsewhere than the set's reference plan draws it. WHAT IT DOES NOT: a real set (the
/// set-check verb on 31130 and 31065 is that measurement); the storeys against a model, covered by
/// StoreyAgreement's own tests; what the gatekeeper finds that this does not, which is the done-when.
/// </remarks>
public sealed class TheSetChecksItselfTests
{
    [Fact]
    public void ASheetsOwnFindings()
    {
        var geometry = new ExtractedGeometry();
        geometry.Footings.Add(new FootingOutline("F1", [(0, 0), (2000, 0), (2000, 2000), (0, 2000)], (1000, 1000), 2000, 2000, 0) { LabelledOnThePlan = false });
        geometry.GridAxes.Add(new GridAxis("1", true, 1000)); geometry.GridAxes.Add(new GridAxis("1", true, 9000)); geometry.GridAxes.Add(new GridAxis("A", false, 500));
        var agreement = new PlanScheduleAgreement(3, 2, 1, 2, 3, ["C9"], [
            new ColumnAgreement(100, 100, 600, 600, "C1", 200, true, true),
            new ColumnAgreement(5000, 100, 800, 800, "C2", 200, true, false),
            new ColumnAgreement(9000, 100, 450, 450, null, null, false, false)]);
        var r = Record(pageNumber: 7, sheetNumber: null, type: "plan", scaleNote: null, geometry, agreement,
            labels: [new FootingOutlines.MarkLabel("F2", 8000, 8000)]) with { ScaleConflict = true };
        var kinds = SetCheck.Sheet(r).Select(f => f.Kind).ToList();
        Assert.Contains("sheet number: none read", kinds);
        Assert.Contains("scale: stated twice, differently", kinds);
        Assert.Contains("column size differs from its own mark's", kinds);
        Assert.Contains("footing box no label names", kinds);
        Assert.Contains("footing label no footing answers", kinds);
        Assert.Contains("grid name drawn twice on the sheet", kinds);

        // the set decides marks and sizes: C9 is placed nowhere, the 450 x 450 is declared nowhere
        var set = SetCheck.Set([r]);
        Assert.Contains(set.Findings, f => f.Kind == "schedule marks placed on no plan in the set" && f.Detail.Contains("C9"));
        Assert.Contains(set.Findings, f => f.Kind == "columns of a size no schedule in the set declares" && f.Detail.Contains("18x18"));
    }

    [Fact]
    public void AMarkPlacedOnAnotherSheetAndASizeDeclaredOnAnotherSheetAreNotFindings()
    {
        var g1 = new ExtractedGeometry();
        var declaresC9Places450 = Record(1, "S2.01", "plan", "1/8\" = 1'-0\"", g1,
            new PlanScheduleAgreement(1, 0, 0, 0, 1, ["C9"], [new ColumnAgreement(100, 100, 450, 450, null, null, false, false)]), [])
            with { Furniture = new SheetFurniture.Set([], [], [], 0, [(600.0, 600.0)], 25) };
        var placesC9Declares450 = Record(2, "S2.02", "plan", "1/8\" = 1'-0\"", new ExtractedGeometry(),
            new PlanScheduleAgreement(1, 1, 1, 1, 1, [], [new ColumnAgreement(100, 100, 600, 600, "C9", 100, true, true)]), [])
            with { Furniture = new SheetFurniture.Set([], [], [], 0, [(450.0, 450.0)], 25) };
        var set = SetCheck.Set([declaresC9Places450, placesC9Declares450]);
        Assert.DoesNotContain(set.Findings, f => f.Kind.StartsWith("schedule marks"));
        Assert.DoesNotContain(set.Findings, f => f.Kind.StartsWith("columns of a size"));
    }

    [Fact]
    public void TheSetsFindingsAreDuplicateNumbersAndAnAxisDrawnElsewhere()
    {
        static ExtractedGeometry Grid(params (string Name, bool V, double At)[] axes)
        {
            var g = new ExtractedGeometry();
            foreach (var (n, v, at) in axes) g.GridAxes.Add(new GridAxis(n, v, at));
            return g;
        }
        var reference = Record(1, "S2.01", "plan", "1/8\" = 1'-0\"", Grid(("1", true, 1000), ("2", true, 7000), ("3", true, 13000), ("A", false, 500), ("B", false, 6500)), null, []);
        // the same grid drawn 2.5 m right on its page, with axis 3 sitting 300 mm off where S2.01 draws it
        var shifted = Record(2, "S2.02", "plan", "1/8\" = 1'-0\"", Grid(("1", true, 3500), ("2", true, 9500), ("3", true, 15800), ("A", false, 500), ("B", false, 6500)), null, []);
        var twin = Record(3, "S2.02", "plan", "1/8\" = 1'-0\"", Grid(("1", true, 1000), ("2", true, 7000), ("3", true, 13000)), null, []);
        var report = SetCheck.Set([reference, shifted, twin]);
        Assert.Contains(report.Findings, f => f.Kind == "sheet number used twice" && f.Sheet == "S2.02");
        var off = Assert.Single(report.Findings, f => f.Kind == "grid axis elsewhere than the set draws it");
        Assert.Equal(2, off.Page);
        Assert.Contains("3 300 mm", off.Detail);
        Assert.Equal(3, report.Plans);
    }

    private static SheetRecord Record(int pageNumber, string? sheetNumber, string type, string? scaleNote, ExtractedGeometry geometry,
        PlanScheduleAgreement? agreement, IReadOnlyList<FootingOutlines.MarkLabel> labels)
    {
        var content = new VectorPageReader.PageContent(pageNumber, 3024, 2160, new List<VectorPageReader.TextToken>(), new List<VectorPageReader.GeomPath>());
        return new SheetRecord(pageNumber, 3024, 2160, 0, sheetNumber, null, type, "P1", null, scaleNote, scaleNote is null ? null : 96,
            geometry, Array.Empty<ScheduleTable>(), Array.Empty<PlanMark>(), Array.Empty<SlabThicknessZoner.Callout>(),
            new GridBubbles.Grid(Array.Empty<GridBubbles.Bubble>(), Array.Empty<double>(), Array.Empty<double>(), Array.Empty<GridBubbles.Axis>()),
            SheetFurniture.Set.Empty, Array.Empty<MarkupNote>(), 0, content, Array.Empty<PathFate>(), Array.Empty<WordFate>())
        {
            ColumnAgreement = agreement,
            FootingLabels = labels,
            TitleBlock = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["SHEET TITLE"] = "LEVEL P1 PLAN" },
        };
    }
}
