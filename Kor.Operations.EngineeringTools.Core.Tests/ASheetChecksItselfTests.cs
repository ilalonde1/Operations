#nullable enable
using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// A structural sheet states its columns twice — as geometry and as a schedule — so it can be
/// checked against itself, with no reference model and no engineer.
/// </summary>
/// <remarks>
/// Every other gate in this repo compares against a reference model, an engineer's ruling, or a
/// previous run. None of them can say anything about the first sheet of a new job, which is exactly
/// when a reader is most likely to be silently wrong about a practice it has not met.
///
/// The two halves are read by entirely separate code — GeometryFilterService off the vector paths,
/// ColumnScheduleReader off the vector text — so agreement between them is evidence.
///
/// ⭐ WHAT MAKES IT WORTH HAVING: it catches a WRONG SCALE, which nothing else here can. A scale
/// error renders identically and is wrong by a constant everywhere, so no picture and no count
/// shows it. Measured on 31065, whose sheets state 1:100, on the same three pages:
///
///     scale 96 (wrong)    3/36   3/31   3/33
///     scale 100 (stated) 19/36  25/31  22/33
///
/// A 4% error takes the score from about 65% to about 9%.
///
/// WHAT THIS COVERS: that a found column counts when the schedule declares its size; that matching
/// its OWN nearby mark is counted separately and more strictly; that a declared mark never placed
/// is reported; and that a small proportional error in every size — a wrong scale — collapses the
/// score rather than passing quietly.
///
/// WHAT IT DOES NOT COVER: it cannot see a column the reader MISSED, because it only looks at what
/// was found — a sheet where half went unread scores well on the half that did. It says nothing
/// about walls or slabs. And it does not open a PDF, so it cannot catch a fault in how the geometry
/// or the schedule got here.
/// </remarks>
public sealed class ASheetChecksItselfTests
{
    private static ColumnScheduleRow Declared(string mark, double wMm, double dMm)
        => new(mark, wMm, dMm, StrengthMPa: 45, Reinforcing: null, Ties: null);

    /// <summary>31130's PC1 (12x24in), PC2 (24x24in) and PC5 (24x30in), in millimetres.</summary>
    private static IReadOnlyList<ColumnScheduleRow> Schedule() =>
    [
        Declared("PC1", 304.8, 609.6),
        Declared("PC2", 609.6, 609.6),
        Declared("PC5", 609.6, 762.0),
    ];

    private static ExtractedGeometry Geometry(params (double X, double Y, double W, double D)[] columns)
    {
        var geo = new ExtractedGeometry { ScaleDenominator = 96 };
        foreach (var (x, y, w, d) in columns)
        {
            geo.Columns.Add((x, y));
            geo.ColumnSizes.Add((w, d));
            geo.ColumnColors.Add((0, 0, 0));
            geo.ColumnIsAnnotation.Add(false);
        }
        return geo;
    }

    [Fact]
    public void AColumnWhoseSizeTheScheduleDeclaresCounts()
    {
        var geo = Geometry(
            (1000, 1000, 304.8, 609.6),      // PC1
            (2000, 1000, 609.6, 609.6),      // PC2
            (3000, 1000, 999.0, 1400.0));    // nothing like any row

        var check = PlanAgreesWithItsSchedule.Check(geo, Schedule());

        Assert.Equal(3, check.ColumnsFound);
        Assert.Equal(2, check.SizesDeclaredSomewhere);
    }

    /// <summary>
    /// A size that is declared SOMEWHERE is the weak form. Matching the mark printed beside it is
    /// the strong one, and it is what catches a column built to the wrong row of its own schedule.
    /// </summary>
    [Fact]
    public void MatchingTheMarkPrintedBesideItIsCountedSeparately()
    {
        var geo = Geometry((1000, 1000, 304.8, 609.6));    // a PC1-sized column

        // the label says PC2 — a size the schedule declares, but not this mark's
        var page = new VectorPageReader.PageContent(
            1, 3000, 1800,
            [new VectorPageReader.TextToken("PC2", 30, 30, 22, 27, 38, 33)],
            new List<VectorPageReader.GeomPath>());

        var check = PlanAgreesWithItsSchedule.Check(geo, Schedule(), page);

        Assert.Equal(1, check.SizesDeclaredSomewhere);   // 12x24 is a real row
        Assert.Equal(0, check.MatchedToTheirOwnMark);    // but its label says PC2
    }

    [Fact]
    public void AMarkTheScheduleDeclaresAndThePlanNeverPlacesIsReported()
    {
        var geo = Geometry((1000, 1000, 304.8, 609.6));

        var page = new VectorPageReader.PageContent(
            1, 3000, 1800,
            [new VectorPageReader.TextToken("PC1", 30, 30, 22, 27, 38, 33)],
            new List<VectorPageReader.GeomPath>());

        var check = PlanAgreesWithItsSchedule.Check(geo, Schedule(), page);

        Assert.Equal(new[] { "PC2", "PC5" }, check.MarksDeclaredButNeverFound);
    }

    /// <summary>
    /// The one that earns the gate. Every size out by the same 4% — a 1:96 read of a 1:100 sheet —
    /// looks identical rendered and is wrong everywhere. The score is what shows it.
    /// </summary>
    [Fact]
    public void AWrongScaleCollapsesTheScoreThoughNothingLooksWrong()
    {
        var right = Geometry(
            (1000, 1000, 304.8, 609.6),
            (2000, 1000, 609.6, 609.6),
            (3000, 1000, 609.6, 762.0));

        const double Off = 100.0 / 96.0;                 // read at 96 what was drawn at 100
        var wrong = Geometry(
            (1000, 1000, 304.8 * Off, 609.6 * Off),
            (2000, 1000, 609.6 * Off, 609.6 * Off),
            (3000, 1000, 609.6 * Off, 762.0 * Off));

        var good = PlanAgreesWithItsSchedule.Check(right, Schedule());
        var bad  = PlanAgreesWithItsSchedule.Check(wrong, Schedule());

        Assert.Equal(3, good.SizesDeclaredSomewhere);
        Assert.True(bad.SizesDeclaredSomewhere < good.SizesDeclaredSomewhere,
            $"a 4% scale error scored {bad.SizesDeclaredSomewhere}/{bad.ColumnsFound}, the same as "
            + "the correct read — then this gate cannot see the one fault no picture shows");
    }

    [Fact]
    public void ASheetWithNoColumnsScoresZeroRatherThanDividingByIt()
    {
        var check = PlanAgreesWithItsSchedule.Check(Geometry(), Schedule());

        Assert.Equal(0, check.ColumnsFound);
        Assert.Equal(0.0, check.Score);
    }

    // ── the drawing's own count, which is the denominator that means something ───────────────────

    private static VectorPageReader.PageContent PageWith(params (string Text, double X, double Y)[] words)
        => new(1, 3000, 1800,
               words.Select(w => new VectorPageReader.TextToken(w.Text, w.X, w.Y, w.X - 8, w.Y - 3, w.X + 8, w.Y + 3)).ToList(),
               new List<VectorPageReader.GeomPath>());

    /// <summary>
    /// The schedule lists every mark once. Counting its own table would add a phantom column per
    /// mark and quietly deflate coverage.
    /// </summary>
    [Fact]
    public void TheSchedulesOwnMarkColumnIsNotCountedAsColumnsOnThePlan()
    {
        var page = PageWith(
            ("COLUMN", 2000, 900), ("SCHEDULE", 2060, 900),   // the heading
            ("PC1", 1990, 860),                               // its own row — not a column
            ("PC1", 400, 1500), ("PC1", 600, 1500),           // two columns out on the plan
            ("PC2", 800, 1500));

        Assert.Equal(3, PlanAgreesWithItsSchedule.CountPlanLabels(page, Schedule()));
    }

    /// <summary>A footing mark on the same plan is not a column and must not inflate the count.</summary>
    [Fact]
    public void OnlyMarksTheColumnScheduleDeclaresAreCounted()
    {
        var page = PageWith(
            ("PC1", 400, 1500),
            ("F2", 600, 1500), ("SF1", 800, 1500));          // 31130 p12 prints F2 twenty times

        Assert.Equal(1, PlanAgreesWithItsSchedule.CountPlanLabels(page, Schedule()));
    }

    /// <summary>
    /// Coverage must use the strict numerator. A shape with no label that happens to be a declared
    /// size is not evidence a labelled column was found — counting it gave 22 of 15 on 31138 p9.
    /// </summary>
    [Fact]
    public void AnUnlabelledCoincidentalMatchDoesNotRaiseCoverage()
    {
        // one labelled PC1-sized column, and one PC1-sized shape nowhere near a label
        var geo = Geometry((1000, 1000, 304.8, 609.6), (90_000, 90_000, 304.8, 609.6));
        var page = PageWith(("PC1", 30, 30), ("PC1", 34, 30));

        var check = PlanAgreesWithItsSchedule.Check(geo, Schedule(), page);

        Assert.Equal(2, check.SizesDeclaredSomewhere);            // both are a declared size
        Assert.Equal(2, check.LabelsOnThePlan);
        Assert.True(check.Coverage <= 1.0, $"coverage was {check.Coverage:0.00}");
    }

    /// <summary>
    /// The other half: over-detection. 31168 p11 emitted 266 columns where the drawing labels 72,
    /// and coverage alone called that sheet healthy.
    /// </summary>
    [Fact]
    public void EmittingFarMoreColumnsThanTheDrawingLabelsShowsUpAsPrecision()
    {
        var many = Enumerable.Range(0, 20)
            .Select(i => ((double)(i * 1000), 1000.0, 304.8, 609.6))
            .ToArray();

        var check = PlanAgreesWithItsSchedule.Check(Geometry(many), Schedule(), PageWith(("PC1", 30, 30)));

        Assert.Equal(1, check.LabelsOnThePlan);
        Assert.Equal(20.0, check.Precision);
    }
}
