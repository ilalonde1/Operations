#nullable enable
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using UglyToad.PdfPig;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// One command that measures what the schedule readers read off the five local KOR stick files.
/// The C# port of <c>tools/Measure-StickFileSchedules.ps1</c>, check for check, so that the
/// standard lives in the build and not in a script.
/// </summary>
/// <remarks>
/// It exists because on 2026-09-04 a commit stated "Footings unchanged: 31065 1,174 cy, 31138
/// 353 cy, 31130 258 cy" and the same reader on the same files read 855 and 48 — nothing measured
/// every deliverable, so nothing said so (CLAUDE.md rule 9).
///
/// WHAT IT COVERS
///   - spread-footing totals and the set of footing marks read, per job
///   - the set of COLUMN marks read off one schedule page per job, exactly, and that every one was
///     read from the table's own border (MarkRoute.ScheduleBorder) on the four jobs that draw one
///   - the flat shear-wall rows on that page as MARK:THICKNESS-IN:MPa, read off the crops by eye
///   - the plan self-check per page: a coverage FLOOR (may only rise), and that the unplaced list
///     holds only mark-shaped tokens (a bar size like 8-35M or a wrapped word like BOT. read as a
///     mark is the fault this guards)
///
/// WHAT IT DOES NOT COVER
///   - whether a coverage number is RIGHT — only that it does not go down; on 31130 p11 the
///     number is 14 of 42 while all 42 columns are emitted where drawn, because the label match
///     fails on rotated marks (measured 2026-09-08). The floor guards the reader, not the truth
///   - the DXF written, the storeys, or anything downstream of intake
///   - a page the standard does not list
///   - a schedule read with the right marks and the wrong sizes: the mark set would not move
///   - a mark whose size VARIES (31168's C03-B): banked as a mark, its columns agree by definition
///
/// THE FIXTURES are a frozen local mirror, one PDF per job, at %LOCALAPPDATA%\Temp\kor-drawings\stickfiles:
///   31130-01 2026-05-20 issue · 31138-01 2026-09-01 · 31168-01 2026-04-21 · 31065-01 2026-07-08 · 31202-01 2026-09-04.
/// A missing mirror FAILS, with the path; it never skips. A gate that passes by not running is the
/// fault it exists to catch (see LiveProjects).
///
/// THE STANDARD is the .ps1's, 2026-09-08: footings from the Codex 10 landing (4fd1abbe), column
/// marks from the border change (2026-09-07), wall rows read off the crops. 31168 footings 0 and
/// 31202 footings 0 are KNOWN (a placeholder table; a raft). Change a value when a reader changes
/// what it reads, in the same commit, and say why.
/// </remarks>
[Trait("Speed", "Slow")]
public sealed class FiveStickFilesTests
{
    private static readonly string StickFiles =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "kor-drawings", "stickfiles");

    /// <summary>A mark as KOR draws one: C4, PC1, TC02, C02-A, PC03-A, GC11-C, SF1, PL1, PC1A — or a bare numeral (31202).</summary>
    private static readonly Regex MarkShape = new(@"^(?:[A-Z]{1,3}\d{1,2}[A-Z]?(?:-[A-Z0-9]{1,3})?|\d{1,2})$", RegexOptions.Compiled);

    /// <summary>A wall mark (SWA, SWB1, W2) or a zone mark (ZA); a bar size, a note number or a footing mark is not.</summary>
    private static readonly Regex WallShape = new(@"^(?:SW|W|Z)[A-Z0-9]{0,3}$", RegexOptions.Compiled);

    private sealed record Job(
        string Number, int Scale, int FootingCy, string FootingMarks,
        int SchedulePage, string ColumnMarks, string WallRows,
        IReadOnlyDictionary<int, int> CoverFloors,
        // Schedule-page wall count is banked by the verifier; 0 is an unbanked slot, not a measured count.
        int WallCount = 0,
        // Footings read as dashed outlines of a scheduled size on the schedule page, and the spread
        // footing marks the plan places there. Banked 2026-09-08 (31130 p11 35 of 36, 31138 p9 11 of 11,
        // 31065 p14 26 of 31; 31168 and 31202 schedule no spread footing on their banked pages).
        int FootingCount = 0, int FootingMarksPlaced = 0);

    private static readonly Job[] Jobs =
    {
        new("31130-01", 96, 258, "F1,F2,F3,F4,SF1", 11,
            "PC1,PC2,PC4,PC5,PC6,PC7,PC8", "SWA:12:35,SWB:12:45,SWC:12:45,SWD:16:55",
            new Dictionary<int, int> { [11] = 14, [12] = 25, [13] = 41 }, WallCount: 11, FootingCount: 35, FootingMarksPlaced: 36),
        new("31168-01", 96, 0, "", 11,
            "C02-A,C02-B,C03-A,C03-B,C04-A,C04-B,GC11-C,PC01,PC02,PC03-A,PC03-B,TC01,TC02,TC03,TC04", "SWA:12:35,SWB:12:45,SWC:12:45,SWD:16:55",
            new Dictionary<int, int> { [11] = 43, [12] = 65, [13] = 47 }, WallCount: 25),
        new("31138-01", 96, 353, "F1,F2,SF1,SF2", 9,
            "PC1,PC1A,PC2,PC3,PC3A,PC4,PC5,PC6,PC7,PC8,PC9,PL1,PL2", "SWA:8:35",
            new Dictionary<int, int> { [9] = 24, [11] = 21 }, WallCount: 41, FootingCount: 11, FootingMarksPlaced: 11),
        new("31065-01", 100, 1174, "F1,F2,F3,F4,SF1,SF2", 14,
            "PC1,PC1A,PC2,PC3,PC4,PC5,ZC1,ZC2", "SWA:8:35,SWB:8:35,SWC:24:45",
            new Dictionary<int, int> { [14] = 24, [15] = 22, [16] = 30 }, WallCount: 36, FootingCount: 26, FootingMarksPlaced: 31),
        new("31202-01", 96, 0, "", 17,
            "1,2,3,4,5,6,7,8", "",
            new Dictionary<int, int> { [17] = 23 }, WallCount: 19),
    };

    public static IEnumerable<object[]> JobNumbers() => Jobs.Select(j => new object[] { j.Number });

    private static Job JobNamed(string number) => Jobs.Single(j => j.Number == number);

    private static string PdfOf(Job job)
    {
        string pdf = Path.Combine(StickFiles, job.Number + ".pdf");
        Assert.True(File.Exists(pdf),
            $"Stick file missing: {pdf}. Mirror the five PDFs there first (see the class remarks); this gate never skips.");
        return pdf;
    }

    private static string Sorted(string csv) =>
        string.Join(",", csv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).OrderBy(s => s, StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(JobNumbers))]
    public void FootingTotalAndMarksAreTheBankedOnes(string number)
    {
        var job = JobNamed(number);
        string pdf = PdfOf(job);

        double total = 0;
        var marks = new SortedSet<string>(StringComparer.Ordinal);
        using var doc = PdfDocument.Open(pdf);
        for (int p = 1; p <= doc.NumberOfPages; p++)
        {
            var pc = VectorPageReader.ReadPage(doc.GetPage(p));
            if (pc.Words.Count == 0) continue;
            string ds = string.Concat(string.Join(" ", pc.Words.Select(w => w.Text)).ToUpperInvariant().Where(c => !char.IsWhiteSpace(c)));
            if (!ds.Contains("FOUNDATIONSCHEDULE") && !ds.Contains("FOOTINGSCHEDULE")
                && !ds.Contains("RAFTSLAB") && !ds.Contains("MATFOUNDATION") && !ds.Contains("MATSLAB") && !ds.Contains("PILESCHEDULE")) continue;
            var (types, box) = FootingScheduleReader.ReadSchedule(pc);
            if (types.Count == 0) continue;
            var counts = FootingScheduleReader.CountPlacements(pc, types, box);
            foreach (var ft in types)
            {
                marks.Add(ft.Mark);
                if (ft.IsSpread) total += counts.GetValueOrDefault(ft.Mark) * ft.VolumeCuYdEach;
            }
        }

        Assert.Equal(job.FootingCy, (int)Math.Round(total));
        Assert.Equal(Sorted(job.FootingMarks), string.Join(",", marks));
    }

    [Theory]
    [MemberData(nameof(JobNumbers))]
    public void ColumnMarksAreTheBankedSetAndEveryOneCameFromTheBorder(string number)
    {
        var job = JobNamed(number);
        var page = VectorPageReader.ReadPage(PdfOf(job), job.SchedulePage);
        var rows = ColumnScheduleReader.ReadSchedule(page);

        Assert.Equal(Sorted(job.ColumnMarks), string.Join(",", rows.Select(r => r.Mark).Distinct().OrderBy(m => m, StringComparer.Ordinal)));
        var offRoute = rows.Where(r => r.Route != MarkRowScheduleReader.MarkRoute.ScheduleBorder).Select(r => $"{r.Mark}[{r.Route}]").ToList();
        Assert.True(offRoute.Count == 0, $"{number} p{job.SchedulePage}: marks not read from the table's border: {string.Join(",", offRoute)}");
    }

    [Theory]
    [MemberData(nameof(JobNumbers))]
    public void FlatShearWallRowsAreTheBankedOnesAndWallShaped(string number)
    {
        var job = JobNamed(number);
        var page = VectorPageReader.ReadPage(PdfOf(job), job.SchedulePage);
        var rows = ScheduleGridReader.ReadFlatWallRows(page);

        string read = string.Join(",", rows
            .Select(r => $"{r.Mark}:{(int)Math.Round(r.ThicknessIn)}:{(r.StrengthMPa is double mpa ? (int)Math.Round(mpa) : 0)}")
            .OrderBy(s => s, StringComparer.Ordinal));
        Assert.Equal(Sorted(job.WallRows), read);
        var notWalls = rows.Select(r => r.Mark).Where(m => !WallShape.IsMatch(m)).ToList();
        Assert.True(notWalls.Count == 0, $"{number} p{job.SchedulePage}: read as wall marks and are not: {string.Join(",", notWalls)}");
    }

    /// <summary>
    /// The ledger's population is the page: on every banked page, the record's fates cover every
    /// path of the unthinned read exactly once. On 2026-09-08 the first ledger covered 4,129 of
    /// 45,515 on 31130 p13 and said nothing; this is the check that would have said it.
    /// </summary>
    [Theory]
    [MemberData(nameof(JobNumbers))]
    public void EveryPathOnEveryBankedPageHasExactlyOneFate(string number)
    {
        var job = JobNamed(number);
        var (options, _) = PdfIntakeOptions.For(null);
        using var doc = PdfDocument.Open(PdfOf(job));
        var facts = Kor.Operations.EngineeringTools.Intake.DocumentFacts.From(doc);
        foreach (var pageNo in job.CoverFloors.Keys)
        {
            var record = Kor.Operations.EngineeringTools.Intake.DrawingIntake.ReadSheet(
                doc, pageNo, new Kor.Operations.EngineeringTools.Intake.IntakeRequest(job.Scale, options), facts);
            var unthinned = VectorPageReader.ReadPage(doc.GetPage(pageNo), includeAnnotations: true, curveSegments: PdfToSafeConstants.BezierSegments);
            Assert.True(record.Content.Paths.Count == unthinned.Paths.Count,
                $"{number} p{pageNo}: the record's population is {record.Content.Paths.Count} paths; the unthinned read has {unthinned.Paths.Count}");
            Assert.True(record.PathFates.Count == record.Content.Paths.Count,
                $"{number} p{pageNo}: {record.PathFates.Count} fates for {record.Content.Paths.Count} paths");
            Assert.Equal(record.Content.Paths.Count, record.PathFates.Select(f => f.PathIndex).Distinct().Count());
            Assert.True(record.PathFates.Any(f => f.Reason == Kor.Operations.EngineeringTools.Intake.PathReason.BecameColumnByShape
                                                  || f.Reason == Kor.Operations.EngineeringTools.Intake.PathReason.BecameColumnByDeclaredSize),
                $"{number} p{pageNo}: no path became a column, on a page the self-check banks columns for");
        }
    }

    /// <summary>
    /// The walls the intake reads off each job's schedule page, banked 2026-09-08 from the census of
    /// the thirteen baseline DXFs after brief 15 (31130 p11 11, 31168 p11 25, 31138 p9 41, 31065 p14
    /// 36, 31202 p17 19). Exact, not a floor: a wall count that moves either way is a change in what
    /// the classifier calls a wall, and the commit that moves it says why.
    /// </summary>
    [Theory]
    [MemberData(nameof(JobNumbers))]
    public void WallsOnTheSchedulePageAreTheBankedCount(string number)
    {
        var job = JobNamed(number);
        var (options, _) = PdfIntakeOptions.For(null);
        var geo = PdfPlanReader.Read(PdfOf(job), job.Scale, job.SchedulePage, options, annotationsOnly: false);
        Assert.True(job.WallCount == geo.Walls.Count,
            $"{number} p{job.SchedulePage}: {geo.Walls.Count} walls read, {job.WallCount} banked");
        Assert.All(geo.Walls, w => Assert.True(w.ThicknessMm >= options.MinWallThicknessMm - 12.7 && w.ThicknessMm <= options.MaxWallThicknessMm + 12.7,
            $"{number}: a wall {w.ThicknessMm:0} mm thick is outside the banked limits"));
    }

    /// <summary>
    /// Footings the intake reads as dashed outlines of a scheduled size on the schedule page, exact,
    /// and the spread-footing marks the plan places there (the schedule reader's own count). The gap
    /// between them — 1 on 31130 p11, 5 on 31065 p14 — is the measured shortfall of the chaining, and
    /// closing it moves this number, with a sentence saying why.
    /// </summary>
    [Theory]
    [MemberData(nameof(JobNumbers))]
    public void FootingsOnTheSchedulePageAreTheBankedCount(string number)
    {
        var job = JobNamed(number);
        var (options, _) = PdfIntakeOptions.For(null);
        string pdf = PdfOf(job);
        var geo = PdfPlanReader.Read(pdf, job.Scale, job.SchedulePage, options, annotationsOnly: false);
        Assert.True(job.FootingCount == geo.Footings.Count,
            $"{number} p{job.SchedulePage}: {geo.Footings.Count} footings read, {job.FootingCount} banked");
        var page = VectorPageReader.ReadPage(pdf, job.SchedulePage);
        var (types, box) = FootingScheduleReader.ReadSchedule(page);
        int placed = types.Count == 0 ? 0
            : FootingScheduleReader.CountPlacements(page, types, box).Where(kv => types.Any(t => t.Mark == kv.Key && t.IsSpread)).Sum(kv => kv.Value);
        Assert.True(job.FootingMarksPlaced == placed,
            $"{number} p{job.SchedulePage}: the plan places {placed} spread-footing marks, {job.FootingMarksPlaced} banked");
        Assert.All(geo.Footings, f => Assert.Contains(types, t => t.Mark == f.Mark && t.IsSpread));
    }

    [Theory]
    [MemberData(nameof(JobNumbers))]
    public void PlanSelfCheckHoldsItsFloorAndUnplacedAreMarkShaped(string number)
    {
        var job = JobNamed(number);
        string pdf = PdfOf(job);
        var (options, _) = PdfIntakeOptions.For(null);

        using var doc = PdfDocument.Open(pdf);
        foreach (var (pageNo, floor) in job.CoverFloors)
        {
            var geo = PdfPlanReader.Read(doc, job.Scale, pageNo, options, annotationsOnly: false);
            var page = VectorPageReader.ReadPage(doc.GetPage(pageNo));
            var declared = ColumnScheduleReader.ReadSchedule(page);
            Assert.True(declared.Count > 0 && geo.Columns.Count > 0,
                $"{number} p{pageNo}: no self-check possible — {declared.Count} declared marks, {geo.Columns.Count} columns emitted (older build, or the page read nothing)");

            var check = PlanAgreesWithItsSchedule.Check(geo, declared, page, options.AgreementToleranceMm, options.AgreementLabelReachMm);
            Assert.True(check.MatchedToTheirOwnMark >= floor,
                $"{number} p{pageNo}: coverage {check.MatchedToTheirOwnMark}/{check.LabelsOnThePlan} fell below its floor of {floor}");
            var notMarks = check.MarksDeclaredButNeverFound.Where(m => !MarkShape.IsMatch(m)).ToList();
            Assert.True(notMarks.Count == 0, $"{number} p{pageNo}: unplaced tokens that are not marks: {string.Join(",", notMarks)}");
        }
    }
}
