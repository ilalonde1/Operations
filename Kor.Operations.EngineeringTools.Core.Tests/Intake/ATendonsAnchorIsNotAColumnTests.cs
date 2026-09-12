#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A post-tensioning tendon's anchor is not a column (intake step 48, 2026-09-12): a line labelled
/// with a force is a tendon, and the small filled block at its end is where the strand is stressed.
/// On 31202 the reader took 55 of them for columns on every typical storey.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a force word beside a long line names it a tendon and the column at its end is
/// stood down; a column the tendon runs OVER stays; a line with no force label ending in a column
/// (a beam) stands nothing down; a short line beside a force word (a leader) is not a tendon; the
/// force vocabulary is the compiled default extended by the row. WHAT IT DOES NOT: the reader's
/// column candidates themselves (the classifier's); a real drawing (the six-set gate and 31202's
/// yardstick measure that); an anchor drawn as anything but a column-shaped block.
/// </remarks>
public sealed class ATendonsAnchorIsNotAColumnTests
{
    private const double MmPerPoint = 96 * 25.4 / 72.0;   // 1:96

    private static VectorPageReader.TextToken Word(string text, double xMm, double yMm, double heightPt = 8)
    {
        double x = xMm / MmPerPoint, y = yMm / MmPerPoint;
        return new VectorPageReader.TextToken(text, x, y, x - 10, y - heightPt / 2, x + 10, y + heightPt / 2);
    }

    private static ExtractedGeometry Geometry()
    {
        var g = new ExtractedGeometry();
        // a real 12x48 column the tendon runs over, at (6000, 3000); the anchors at the tendon's ends
        g.Columns.Add((6000, 3000)); g.ColumnSizes.Add((305, 1219));
        g.Columns.Add((1000, 3000)); g.ColumnSizes.Add((229, 305));
        g.Columns.Add((11000, 3000)); g.ColumnSizes.Add((229, 305));
        // a column a beam ends at, with no force label anywhere near
        g.Columns.Add((6000, 9000)); g.ColumnSizes.Add((500, 500));
        // the tendon: a 10 m line from anchor to anchor, passing over the column; the beam: 4 m into the fourth column
        g.Lines.Add([(1000, 3000), (11000, 3000)]);
        g.Lines.Add([(2000, 9000), (6000, 9000)]);
        // a leader: 360 mm long, 2.5 m from the tendon, not a tendon
        g.Lines.Add([(6000, 5300), (6300, 5500)]);
        return g;
    }

    [Fact]
    public void TheColumnsAtATendonsEndsAreAnchorsAndTheOneItRunsOverStays()
    {
        var g = Geometry();
        var content = new VectorPageReader.PageContent(1, 3000, 2000, [Word("270", 5500, 3200), Word("Kips", 5800, 3200), Word("4", 6100, 9200)], []);
        var tendons = TendonAnchors.Read(content, g, MmPerPoint, TendonAnchors.DefaultForceWords);
        var t = Assert.Single(tendons);
        Assert.Equal((1000, 3000), t.Start);
        Assert.Equal((11000, 3000), t.End);

        Assert.Equal(2, TendonAnchors.StandDownColumns(g, tendons));
        Assert.Equal([false, true, true, false], g.ColumnIsTendonAnchor);
    }

    [Fact]
    public void ALineWithNoForceLabelIsNotATendonAndALeaderIsTooShortToBeOne()
    {
        var g = Geometry();
        // the force word sits by the leader only, 2.5 m from the long line - past four label heights: no tendon
        var content = new VectorPageReader.PageContent(1, 3000, 2000, [Word("kN", 6350, 5550)], []);
        Assert.Empty(TendonAnchors.Read(content, g, MmPerPoint, TendonAnchors.DefaultForceWords));
        Assert.Equal(0, TendonAnchors.StandDownColumns(g, []));
        Assert.All(g.ColumnIsTendonAnchor, a => Assert.False(a));
    }

    [Fact]
    public void OnASheetThatLabelsForcesEveryLongRunIsATendonAndADeclaredSizeIsNeverAnAnchor()
    {
        var g = Geometry();
        // three force labels anywhere make it a P/T plan: the unlabelled beam's end column is an anchor too...
        var content = new VectorPageReader.PageContent(1, 3000, 2000, [Word("Kips", 5800, 3200), Word("Kips/ft", 20000, 20000), Word("kN", 25000, 25000)], []);
        var tendons = TendonAnchors.Read(content, g, MmPerPoint, TendonAnchors.DefaultForceWords);
        Assert.Equal(2, tendons.Count);                                          // the labelled tendon and the 4 m beam; the leader is too short
        // ...but a run the sheet's signature named stands a block down only when the set's smallest column is bigger than it:
        // with no schedule at all the beam's 500x500 stays; with a 12x24 (305x610) scheduled the 229x305 anchors go and the 500x500 (bigger) stays
        Assert.Equal(2, TendonAnchors.StandDownColumns(g, tendons));
        Assert.Equal(2, TendonAnchors.StandDownColumns(g, tendons, null, 305.0 * 610.0));
        Assert.Equal([false, true, true, false], g.ColumnIsTendonAnchor);
        // and a schedule that declares the 500x500 keeps it whatever ends there
        Assert.Equal(2, TendonAnchors.StandDownColumns(g, tendons, [false, false, false, true], 100.0 * 100.0));
        Assert.Equal([false, true, true, false], g.ColumnIsTendonAnchor);
    }

    [Fact]
    public void ThePracticesOwnForceWordExtendsTheVocabulary()
    {
        var g = Geometry();
        var content = new VectorPageReader.PageContent(1, 3000, 2000, [Word("TONNES", 5800, 3200)], []);
        Assert.Empty(TendonAnchors.Read(content, g, MmPerPoint, TendonAnchors.DefaultForceWords));
        var extended = PdfIntakeOptions.ApplyRules(PdfIntakeOptions.Default,
            new Dictionary<string, RuleSetting> { ["dxf.pdf.force-words"] = new RuleSetting("dxf.pdf.force-words", double.NaN, RuleSettings.TextUnits, "test", "test", "test") { Text = "TONNES;T" } });
        Assert.Contains("KIPS", extended.ForceWords);
        Assert.Contains("TONNES", extended.ForceWords);
        Assert.Single(TendonAnchors.Read(content, g, MmPerPoint, extended.ForceWords));
    }
}
