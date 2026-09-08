#nullable enable

using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// The measurement behind the PDF classifier's column size window, repeatable: the same five sheets
/// read with the PDF side's own window (200–1,500 mm) and with the window KorStandards banks for
/// the DXF side (dxf.min-column-size 6 in, dxf.max-column-size 132 in, replay-verified on 1,126
/// engineer models), side by side, per page: coverage, precision, slabs.
/// </summary>
/// <remarks>
/// On 2026-09-02 adopting the DXF window was measured and backed out: coverage identical, slabs
/// halved, over-detection 2.6x → 4.3x, because with no layers the size window was the only thing
/// keeping white knock-out squares and table cells out of the column branch. Those are excluded
/// on their own terms now (invisible ink, sheet furniture), so the question is open again — and
/// the 1,500 mm ceiling is exactly what keeps 31138's PC7 (18" x 60") and PC8 (18" x 96") from
/// ever being found. This prints the answer; the decision is taken from the numbers, not here.
///
/// WHAT THIS COVERS: the five local stick files' schedule pages. WHAT IT DOES NOT: it asserts
/// nothing, and it is skipped, saying so, when the local mirror is absent.
/// </remarks>
public sealed class ColumnSizeWindowMeasurement
{
    private readonly ITestOutputHelper _out;
    public ColumnSizeWindowMeasurement(ITestOutputHelper output) => _out = output;

    private static readonly string StickFiles =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "kor-drawings", "stickfiles");

    private static readonly (string Job, int[] Pages, int Scale)[] Sheets =
    [
        ("31130-01", [11, 12, 13], 96),
        ("31168-01", [11, 12, 13], 96),
        ("31138-01", [9, 11], 96),
        ("31065-01", [14, 15, 16], 100),
        ("31202-01", [17], 96),
    ];

    [Trait("Speed", "Slow")]
    [Fact]
    public void TheDxfSidesColumnWindowAgainstThePdfSidesOnEveryScheduleSheet()
    {
        if (!Directory.Exists(StickFiles))
        {
            _out.WriteLine($"SKIPPED: no local stick files at {StickFiles}");
            return;
        }

        var pdfWindow = PdfIntakeOptions.Default;
        var dxfWindow = pdfWindow with { ColumnMinDimMm = 6 * 25.4, ColumnMaxSizeMm = 132 * 25.4 };

        _out.WriteLine("job      page   window        cover   emitted  precision  slabs   unplaced");
        foreach (var (job, pages, scale) in Sheets)
        {
            string pdf = Path.Combine(StickFiles, job + ".pdf");
            if (!File.Exists(pdf)) { _out.WriteLine($"{job}: missing"); continue; }

            foreach (int page in pages)
            {
                var schedulePage = VectorPageReader.ReadPage(pdf, page);
                var declared = ColumnScheduleReader.ReadSchedule(schedulePage);
                foreach (var (name, options) in new[] { ("pdf 200-1500", pdfWindow), ("dxf 152-3353", dxfWindow) })
                {
                    var geo = PdfPlanReader.Read(pdf, scale, page, options, annotationsOnly: false);
                    var check = PlanAgreesWithItsSchedule.Check(geo, declared, schedulePage, options.AgreementToleranceMm, options.AgreementLabelReachMm);
                    _out.WriteLine(
                        $"{job}  p{page,-3}  {name,-13} {check.MatchedToTheirOwnMark,3}/{check.LabelsOnThePlan,-3}  {check.ColumnsFound,5}   " +
                        $"{check.Precision,6:0.0}x  {geo.Slabs.Count,5}   {string.Join(",", check.MarksDeclaredButNeverFound)}");
                }
            }
        }
    }
}
