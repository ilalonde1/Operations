using System;
using System.Collections.Generic;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe;

/// <summary>
/// Locks the contract of the .summary.txt sidecar the engineer reads
/// before launching SAFE. Failures here mean the engineer's pre-launch
/// view of the model is misleading — verify every change deliberately.
/// </summary>
public class ExportSummaryReportTests
{
    private static readonly DateTime FixedTime = new DateTime(2026, 5, 11, 14, 30, 0);

    private static ExtractedGeometry BuildGeo()
    {
        var geo = new ExtractedGeometry();
        // 5 columns: 3 × 350x950, 1 × 500x800, 1 × 1000x1050 (the "C1000x1050"
        // mirroring the Regent drawing's outlier column).
        geo.Columns.AddRange(new (double, double)[]
        {
            (0, 0), (1000, 0), (2000, 0), (3000, 0), (4000, 0),
        });
        geo.ColumnSizes.AddRange(new (double, double)[]
        {
            (350, 950), (350, 950), (350, 950),
            (500, 800), (1000, 1050),
        });
        // 2 walls hinted as W300x1000 (mirroring shaft decomposition output)
        // and 1 hinted as W950x1000 (the legit thick shear wall).
        geo.Lines.AddRange(new List<(double, double)>[]
        {
            new() { (0, 0), (1000, 0) },
            new() { (0, 1000), (1000, 1000) },
            new() { (0, 0), (0, 5000) },
        });
        geo.LineSectionHints.AddRange(new (double, double)?[]
        {
            (300, 1000), (300, 1000), (950, 1000),
        });
        return geo;
    }

    [Fact]
    public void Build_IncludesHeader_AndAllSummarySections()
    {
        var geo = BuildGeo();
        var counts = new ExportSummaryReport.SummaryCounts(SlabCount: 5, WallCount: 3, ColumnCount: 5, OpeningCount: 1);
        var validation = new ValidationResult(new List<ValidationIssue>
        {
            new(ValidationSeverity.Warning, "slab-triangle", "Slab [2] has only 3 vertices")
        });
        var openings = new List<(int ParentSlabIndex, List<(double X, double Y)> Polygon)>
        {
            (0, new List<(double X, double Y)> { (0, 0), (2000, 0), (2000, 5000), (0, 5000) })
        };

        string report = ExportSummaryReport.Build(
            sourcePdfName: "test.pdf",
            outputF2kName: "test_SAFE.f2k",
            generatedAt: FixedTime,
            geo, counts, validation, openings);

        // Header
        Assert.Contains("KOR Operations - Structural PDF Import", report);
        Assert.Contains("Source PDF : test.pdf", report);
        Assert.Contains("Output F2K : test_SAFE.f2k", report);
        Assert.Contains("Generated  : 2026-05-11 14:30", report);

        // Model counts
        Assert.Contains("Slabs    : 5", report);
        Assert.Contains("Walls    : 3", report);
        Assert.Contains("Columns  : 5", report);
        Assert.Contains("Openings : 1", report);

        // Column section inventory grouped by snapped section
        Assert.Contains("COLUMN SECTIONS USED", report);
        Assert.Contains("C350x950", report);
        Assert.Contains("x3", report);

        // Wall section inventory
        Assert.Contains("WALL SECTIONS USED", report);
        Assert.Contains("W300x1000", report);
        Assert.Contains("W950x1000", report);

        // Openings
        Assert.Contains("AUTO-CUT OPENINGS (1)", report);
        Assert.Contains("2000x5000mm", report);
        Assert.Contains("in slab[0]", report);

        // Warnings
        Assert.Contains("PRE-EXPORT WARNINGS (1)", report);
        Assert.Contains("slab-triangle", report);

        // Pre-launch checklist
        Assert.Contains("CHECKLIST BEFORE RUNNING SAFE", report);
        Assert.Contains("Verify each auto-cut opening", report);
        Assert.Contains("Review the warnings above", report);
    }

    [Fact]
    public void Build_OmitsOpeningsAndWarningsSections_WhenEmpty()
    {
        var geo = BuildGeo();
        var counts = new ExportSummaryReport.SummaryCounts(SlabCount: 1, WallCount: 3, ColumnCount: 5, OpeningCount: 0);
        var validation = new ValidationResult(new List<ValidationIssue>());
        var openings = new List<(int ParentSlabIndex, List<(double X, double Y)> Polygon)>();

        string report = ExportSummaryReport.Build(
            sourcePdfName: "test.pdf",
            outputF2kName: "test_SAFE.f2k",
            generatedAt: FixedTime,
            geo, counts, validation, openings);

        // The section header strings must NOT appear when their content is empty,
        // so the engineer's eye doesn't waste time on a "0 warnings" line.
        Assert.DoesNotContain("AUTO-CUT OPENINGS", report);
        Assert.DoesNotContain("PRE-EXPORT WARNINGS", report);

        // Conditional checklist items also drop out.
        Assert.DoesNotContain("Verify each auto-cut opening", report);
        Assert.DoesNotContain("Review the warnings above", report);

        // Unconditional checklist items always appear.
        Assert.Contains("Confirm slab thicknesses match drawing schedules.", report);
    }

    [Fact]
    public void Build_GroupsColumnsBySnappedSection_AndSortsDescByCount()
    {
        var geo = BuildGeo(); // 3 × C350x950, 1 × C500x800, 1 × C1000x1050
        var counts = new ExportSummaryReport.SummaryCounts(0, 0, 5, 0);

        string report = ExportSummaryReport.Build(
            sourcePdfName: null,
            outputF2kName: "x.f2k",
            generatedAt: FixedTime,
            geo, counts, new ValidationResult(new List<ValidationIssue>()),
            new List<(int, List<(double X, double Y)>)>());

        // Most-frequent section listed first (sort by count desc).
        int idx350 = report.IndexOf("C350x950", StringComparison.Ordinal);
        int idx500 = report.IndexOf("C500x800", StringComparison.Ordinal);
        int idx1000 = report.IndexOf("C1000x1050", StringComparison.Ordinal);
        Assert.True(idx350 > 0, "C350x950 must appear");
        Assert.True(idx500 > 0, "C500x800 must appear");
        Assert.True(idx1000 > 0, "C1000x1050 must appear");
        Assert.True(idx350 < idx500, "most-frequent section first");
        Assert.True(idx350 < idx1000, "most-frequent section first");
    }
}
