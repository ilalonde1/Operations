using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Kor.Operations.EngineeringTools.ColumnDesign;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// The schedule that answers "what did we design this column for" without opening ninety files, and
/// says where those files contradict each other.
/// </summary>
public sealed class ColumnDemandScheduleTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir;

    public ColumnDemandScheduleTests(ITestOutputHelper output)
    {
        _out = output;
        _dir = Path.Combine(Path.GetTempPath(), "KorOpsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* a temp folder */ }
    }

    /// <summary>One S-Concrete file with the demands given, written the way the real ones are.</summary>
    private string File_(string name, params (string Storey, string Mark, string Case, double Nf)[] demands)
    {
        var lines = new List<string>
        {
            "@Object@S-CONCRETE Sectional Loads@",
            "@Table@14@",
            "LC\tNf\tTf\tVfz\tMfy\tCmy\tVfy\tMfz\tCmz\tPdistr\tCheckLC\tLoad Type\tComment\tAutoGen",
        };

        int lc = 1;
        foreach (var d in demands)
            lines.Add($"{lc++}\t{d.Nf}\t0\t10\t100\t1\t5\t50\t1\t0\t1\t1\t"
                + $"{d.Storey}  {d.Mark} -> {d.Case}, 12X30, 45Mpa, kl 8.85,Cm-1\t0");

        lines.Add("@EndTable@");

        string path = Path.Combine(_dir, name);
        System.IO.File.WriteAllLines(path, lines);
        return path;
    }

    [Fact]
    public void OneColumnPerStoreyAndMarkWithItsWorstDemand()
    {
        var f = File_("a.SCO",
            ("L02", "C7", "Grav1", -100),
            ("L02", "C7", "EQX1", -250),
            ("L02", "C7", "EQX2", 40));      // net tension under reversal

        var r = ColumnDemandSchedule.Read(new[] { f });

        var col = Assert.Single(r.Columns);
        Assert.Equal("L02", col.Storey);
        Assert.Equal("C7", col.Mark);
        Assert.Equal(3, col.Cases);
        Assert.Equal(250, col.MaxCompression, 3);   // S-Concrete writes compression negative
        Assert.Equal(40, col.MaxTension, 3);
    }

    [Fact]
    public void TwoFilesAgreeingIsNotAConflict()
    {
        var a = File_("a.SCO", ("L02", "C7", "Grav1", -100));
        var b = File_("b.SCO", ("L02", "C7", "Grav1", -100));

        Assert.Empty(ColumnDemandSchedule.Read(new[] { a, b }).Conflicts);
    }

    [Fact]
    public void TwoFilesDisagreeingIsReportedWithHowFarApart()
    {
        var a = File_("a.SCO", ("L02", "C7", "Grav1", -100));
        var b = File_("b.SCO", ("L02", "C7", "Grav1", -180));

        var c = Assert.Single(ColumnDemandSchedule.Read(new[] { a, b }).Conflicts);
        Assert.Equal("C7", c.Mark);
        Assert.Equal(80, c.Difference, 3);
        Assert.Equal(44.4, c.Percent, 1);       // 80 / 180
    }

    [Fact]
    public void RoundingIsSeparatedFromSomethingWorthChasing()
    {
        // A count of disagreements is alarming and unusable. The size of them is what says whether
        // a person has to go and look.
        var a = File_("a.SCO", ("L02", "C7", "Grav1", -100.000), ("L02", "C8", "Grav1", -100));
        var b = File_("b.SCO", ("L02", "C7", "Grav1", -100.05), ("L02", "C8", "Grav1", -180));

        var r = ColumnDemandSchedule.Read(new[] { a, b });

        Assert.Equal(2, r.Conflicts.Count);
        Assert.Equal(1, r.TrivialConflicts);
        Assert.Equal(1, r.MaterialConflicts);
        Assert.Equal("C8", r.Conflicts[0].Mark);   // worst first
    }

    [Fact]
    public void TheFilePairIsNamedBecauseThatIsWhatSomebodyGoesAndSettles()
    {
        // On 30961-01 the worst pair is two filenames one character apart covering the same four
        // columns. Ninety separate rows say nothing; "these two files" is a job.
        var a = File_("14X36 (L02-L8).SCO", ("L03", "C18", "Grav1", -100), ("L04", "C19", "Grav1", -100));
        var b = File_("14X36 (L2-L8).SCO", ("L03", "C18", "Grav1", -180), ("L04", "C19", "Grav1", -160));

        var pair = Assert.Single(ColumnDemandSchedule.Read(new[] { a, b }).ConflictingPairs);

        Assert.Equal(2, pair.Demands);
        Assert.Equal(44.4, pair.WorstPercent, 1);
        Assert.Contains("14X36", pair.FileA);
        Assert.Contains("14X36", pair.FileB);
    }

    [Fact]
    public void ColumnMarksSortLikeNumbersNotLikeText()
    {
        var f = File_("a.SCO",
            ("L02", "C10", "Grav1", -100),
            ("L02", "C9", "Grav1", -100),
            ("L02", "C2", "Grav1", -100));

        var marks = ColumnDemandSchedule.Read(new[] { f }).Columns.Select(c => c.Mark).ToList();

        Assert.Equal(new[] { "C2", "C9", "C10" }, marks);
    }

    [Fact]
    public void TheWorkbookSaysWhatItFoundAndOpensAsAWorkbook()
    {
        var a = File_("a.SCO", ("L02", "C7", "Grav1", -100));
        var b = File_("b.SCO", ("L02", "C7", "Grav1", -180));

        var report = ColumnDemandSchedule.Read(new[] { a, b });
        using var ms = new MemoryStream(ColumnDemandSchedule.BuildXlsx(report, "31999"));
        using var wb = new XLWorkbook(ms);

        Assert.Contains("Column Demands", wb.Worksheets.Select(w => w.Name));
        Assert.Contains("Disagreements", wb.Worksheets.Select(w => w.Name));

        string demands = string.Join("\n", wb.Worksheet("Column Demands").CellsUsed().Select(c => c.GetString()));
        Assert.Contains("31999", demands);
        Assert.Contains("C7", demands);

        string conflicts = string.Join("\n", wb.Worksheet("Disagreements").CellsUsed().Select(c => c.GetString()));
        Assert.Contains("Files that disagree with each other", conflicts);
        Assert.Contains("a.SCO", conflicts);
    }

    [Fact]
    public void AColumnWhoseEffectiveLengthTheFileDoesNotRecordSaysSoInTheWorkbook()
    {
        // S-Concrete truncates the comment near sixty characters, so a long section name pushes kl
        // off the end. A blank cell would read as zero; it has to read as "not recorded".
        string path = Path.Combine(_dir, "d18.SCO");
        System.IO.File.WriteAllLines(path, new[]
        {
            "@Object@S-CONCRETE Sectional Loads@",
            "@Table@14@",
            "LC\tNf\tTf\tVfz\tMfy\tCmy\tVfy\tMfz\tCmz\tPdistr\tCheckLC\tLoad Type\tComment\tAutoGen",
            "1\t-100\t0\t10\t100\t1\t5\t50\t1\t0\t1\t1\tL03  C68 -> Grav1, 15.9520846581496X15.9520846581496, 55Mpa,\t0",
            "@EndTable@",
        });

        var report = ColumnDemandSchedule.Read(new[] { path });

        Assert.Single(report.Truncated);
        Assert.Null(Assert.Single(report.Columns).EffectiveLength);

        using var ms = new MemoryStream(ColumnDemandSchedule.BuildXlsx(report, "31999"));
        using var wb = new XLWorkbook(ms);
        Assert.Contains("not recorded",
            string.Join("\n", wb.Worksheet("Column Demands").CellsUsed().Select(c => c.GetString())));
    }
}
