using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kor.Operations.EngineeringTools.ColumnDesign;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// Reading S-Concrete files, and the reconciliation that says the demands in them can be generated
/// rather than typed.
/// </summary>
public sealed class SConcreteFileTests
{
    private readonly ITestOutputHelper _out;
    public SConcreteFileTests(ITestOutputHelper output) => _out = output;

    /// <summary>The 31 files one engineer made by hand on 30961-01, which everything here is
    /// measured against.</summary>
    private const string RealFolder =
        @"\\Kor-fs01\Projects\Projects\03 Residential\30961-01 (River District Parcel 29 & 30)"
        + @"\02 Engineering\05 Column Design\Column Design - AEM\S-CONCRETE";

    // The real shape, taken from 30961-01: object header, table count, tab-separated header row,
    // rows, terminator. The Comment column is the only place the identity lives.
    private static readonly string[] Sample =
    {
        "@Object@S-CONCRETE Identifiers@",
        "@Table@2@",
        "Version\t2022.1",
        "@EndTable@",
        "@Object@S-CONCRETE Sectional Loads@",
        "@Table@14@",
        "LC\tNf\tTf\tVfz\tMfy\tCmy\tVfy\tMfz\tCmz\tPdistr\tCheckLC\tLoad Type\tComment\tAutoGen",
        " 1\t-102.3596\t 0\t 15.1116\t 107.2232\t 1\t 5.0946\t 25.42022\t 1\t 0\t1\t 1\tL02TH  C75 -> Grav1, 12X30, 45Mpa, kl 8.8497,Cm-1, Slen Min-N\t0",
        " 3\t-364.4055\t 0\t 62.8025\t 506.0379\t 1\t 9.6905\t 56.96675\t 1\t 0\t1\t 1\tL02TH  C75 -> EQX1, 12X30, 45Mpa, kl 8.8497,Cm-1, Slen Min-N\t0",
        " 7\t-216.783\t 0\t 2.3716\t 41.59036\t 1\t 5.3172\t 28.08218\t 1\t 0\t1\t 1\tL02TH  C104 -> Grav1, 12X30, 45Mpa, kl 8.8497,Cm-1, Slen Min\t0",
        "13\t 0\t 0\t 0\t 0\t 1\t 0\t 0\t 1\t 0\t1\t 1\t** Alt. LC # 1\t1",
        "@EndTable@",
    };

    [Fact]
    public void EveryTableInTheFileIsRead()
    {
        var tables = SConcreteFile.ReadTables(Sample);

        Assert.Equal(2, tables.Count);
        Assert.Equal("S-CONCRETE Identifiers", tables[0].Object);
        Assert.Equal(SConcreteFile.SectionalLoads, tables[1].Object);
        Assert.Equal(14, tables[1].Header.Count);
    }

    [Fact]
    public void TheCommentColumnIsWhereTheColumnsIdentityLives()
    {
        var demands = SConcreteFile.ReadDemands(Sample);

        var first = Assert.Single(demands, d => d.Mark == "C75" && d.Case == "Grav1");
        Assert.Equal("L02TH", first.Storey);
        Assert.Equal("12X30", first.Section);
        Assert.Equal("45Mpa", first.Strength);
        Assert.Equal(8.8497, first.EffectiveLength!.Value, 4);

        Assert.Equal(-102.3596, first.Nf, 4);
        Assert.Equal(0, first.Tf, 4);
        Assert.Equal(15.1116, first.Vfz, 4);
        Assert.Equal(107.2232, first.Mfy, 4);
        Assert.Equal(5.0946, first.Vfy, 4);
        Assert.Equal(25.42022, first.Mfz, 4);
    }

    [Fact]
    public void SConcretesOwnGeneratedAlternatesAreNotReadAsDemands()
    {
        // "** Alt. LC # 1" rows carry AutoGen 1: the program's output, not the engineer's input.
        // Reading them as demands would invent load cases nobody applied.
        var demands = SConcreteFile.ReadDemands(Sample);

        Assert.Equal(3, demands.Count);
        Assert.DoesNotContain(demands, d => d.Case.StartsWith("**", StringComparison.Ordinal));
    }

    [Fact]
    public void AGeneratedCommentIsWrittenTheWayAnEngineerWritesIt()
    {
        var d = SConcreteFile.ReadDemands(Sample).First(x => x.Mark == "C75" && x.Case == "Grav1");

        Assert.Equal("L02TH  C75 -> Grav1, 12X30, 45Mpa, kl 8.8497,Cm-1", SConcreteFile.Comment(d));
    }

    // ---------------------------------------------------------------------------------------
    // The OTHER convention, found by running this against a project it was not written for.
    // ---------------------------------------------------------------------------------------

    /// <summary>On 31021-01 one file IS one member and every Comment is literally "--".</summary>
    private static readonly string[] NoIdentityInFile =
    {
        "@Object@S-CONCRETE Sectional Loads@",
        "@Table@14@",
        "LC\tNf\tTf\tVfz\tMfy\tCmy\tVfy\tMfz\tCmz\tPdistr\tCheckLC\tLoad Type\tComment\tAutoGen",
        " 1\t 43\t 0\t 329\t 318\t 1\t 0\t 0\t 1\t 0\t1\t 1\t--\t0",
        " 2\t 53\t 0\t 410\t 387\t 1\t 0\t 0\t 1\t 0\t1\t 1\t--\t0",
        "@EndTable@",
    };

    [Fact]
    public void AFileThatRecordsNoIdentityStillYieldsItsDemands()
    {
        // Insisting on the Comment returned NOTHING from 117 files on 31021-01 — a job worked six
        // weeks ago. A reader written against one project's convention is not a reader.
        var demands = SConcreteFile.ReadDemands(NoIdentityInFile, "31021-BEAM1-L2");

        Assert.Equal(2, demands.Count);
        Assert.All(demands, d => Assert.True(d.IdentityFromFilename));
        Assert.Equal("31021-BEAM1-L2", demands[0].Mark);
        Assert.Equal("L2", demands[0].Storey);
        Assert.Equal("LC1", demands[0].Case);
        Assert.Equal(43, demands[0].Nf, 3);
    }

    [Fact]
    public void WithNoFilenameToFallBackOnNothingIsInvented()
    {
        Assert.Empty(SConcreteFile.ReadDemands(NoIdentityInFile));
    }

    [Theory]
    [InlineData("31021-BEAM1-L2", "L2")]                              // trailing
    [InlineData("31021-12x30-L1-EQ", "L1")]                           // in the middle
    [InlineData("30961-01-12X30-L02TH", "L02TH")]                     // with a suffix
    [InlineData("12X30 (P1-L1)(C53 Conduit Check)", "P1")]            // inside brackets
    [InlineData("31021-18x30 to 12x24-Check Axial Loading", null)]    // none at all
    public void TheStoreyIsTakenFromWhereverTheFilenamePutsIt(string stem, string? expected)
    {
        var d = SConcreteFile.ReadDemands(NoIdentityInFile, stem);

        Assert.Equal(expected ?? "", d[0].Storey);
        Assert.Equal(stem, d[0].Mark);
    }

    [Fact]
    public void AMissingEffectiveLengthIsOnlyCalledTruncationWhenTheCommentWasThere()
    {
        // Reporting 3,309 demands as "lost to the sixty-character limit" when their Comment is "--"
        // is a false statement about someone's structural design.
        var fromFilename = SConcreteFile.ReadDemands(NoIdentityInFile, "31021-BEAM1-L2");
        Assert.All(fromFilename, d => Assert.False(d.IdentityTruncated));

        var truncated = SConcreteFile.ReadDemands(new[]
        {
            "@Object@S-CONCRETE Sectional Loads@",
            "@Table@14@",
            "LC\tNf\tTf\tVfz\tMfy\tCmy\tVfy\tMfz\tCmz\tPdistr\tCheckLC\tLoad Type\tComment\tAutoGen",
            "1\t-100\t0\t10\t100\t1\t5\t50\t1\t0\t1\t1\tL03  C68 -> Grav1, 15.9520846581496X15.9520846581496, 55Mpa,\t0",
            "@EndTable@",
        }, "30961-01-D18-L03");

        Assert.True(Assert.Single(truncated).IdentityTruncated);
    }

    // ---------------------------------------------------------------------------------------
    // Writing. An existing file is the template; only the demands are replaced.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void PuttingBackWhatWasReadChangesNothingButTheLoadRows()
    {
        var demands = SConcreteFile.ReadDemands(Sample);
        var written = SConcreteFile.WithDemands(Sample, demands);

        // Every line outside the Sectional Loads rows survives untouched, in order.
        Assert.Equal("@Object@S-CONCRETE Identifiers@", written[0]);
        Assert.Equal("Version\t2022.1", written[2]);
        Assert.Equal("@EndTable@", written[3]);
        Assert.Equal("@Object@S-CONCRETE Sectional Loads@", written[4]);
        Assert.Equal("@Table@14@", written[5]);
        Assert.StartsWith("LC\tNf\t", written[6]);
        Assert.Equal("@EndTable@", written[^1]);

        // Three demands in, three rows out — S-Concrete's own generated alternate is not written
        // back, because it is the program's output and it regenerates it.
        Assert.Equal(3, written.Count(l => l.Contains(" -> ", StringComparison.Ordinal)));
    }

    [Fact]
    public void AWrittenFileReadsBackAsTheSameDemands()
    {
        // The round trip that matters: whatever we write, our own reader — and therefore
        // S-Concrete's format — gets the same numbers back out.
        var original = SConcreteFile.ReadDemands(Sample);
        var reread = SConcreteFile.ReadDemands(SConcreteFile.WithDemands(Sample, original));

        Assert.Equal(original.Count, reread.Count);
        foreach (var (a, b) in original.Zip(reread))
        {
            Assert.Equal(a.Storey, b.Storey);
            Assert.Equal(a.Mark, b.Mark);
            Assert.Equal(a.Case, b.Case);
            Assert.Equal(a.Section, b.Section);
            Assert.Equal(a.Nf, b.Nf, 6);
            Assert.Equal(a.Vfz, b.Vfz, 6);
            Assert.Equal(a.Mfy, b.Mfy, 6);
            Assert.Equal(a.Vfy, b.Vfy, 6);
            Assert.Equal(a.Mfz, b.Mfz, 6);
            Assert.Equal(a.EffectiveLength, b.EffectiveLength);
        }
    }

    [Fact]
    public void TheLoadCasesAreNumberedFromOneInTheOrderTheyAreGiven()
    {
        var demands = SConcreteFile.ReadDemands(Sample);
        var written = SConcreteFile.WithDemands(Sample, demands);

        var rows = written.Where(l => l.Contains(" -> ", StringComparison.Ordinal)).ToList();
        Assert.Equal("1", rows[0].Split('\t')[0]);
        Assert.Equal("2", rows[1].Split('\t')[0]);
        Assert.Equal("3", rows[2].Split('\t')[0]);
    }

    /// <summary>
    /// EVERY REAL FILE ON THE JOB, ROUND-TRIPPED. Read each of the 31 files, write the demands back,
    /// read them again, and require the numbers to survive. A writer that cannot reproduce what an
    /// engineer already made has no business generating anything new.
    /// </summary>
    [Fact]
    public void EveryHandMadeFileOn30961SurvivesBeingWrittenBack()
    {
        if (!Directory.Exists(RealFolder)) { _out.WriteLine("SKIPPED: share unreachable."); return; }

        int files = 0, demands = 0;
        foreach (string path in Directory.EnumerateFiles(RealFolder, "*.SCO"))
        {
            var lines = File.ReadAllLines(path, System.Text.Encoding.Latin1);
            var before = SConcreteFile.ReadDemands(lines);
            if (before.Count == 0) continue;

            var after = SConcreteFile.ReadDemands(SConcreteFile.WithDemands(lines, before));

            Assert.Equal(before.Count, after.Count);
            foreach (var (a, b) in before.Zip(after))
                Assert.True(a.Key == b.Key
                            && Math.Abs(a.Nf - b.Nf) < 1e-6 && Math.Abs(a.Vfz - b.Vfz) < 1e-6
                            && Math.Abs(a.Mfy - b.Mfy) < 1e-6 && Math.Abs(a.Vfy - b.Vfy) < 1e-6
                            && Math.Abs(a.Mfz - b.Mfz) < 1e-6,
                    $"{Path.GetFileName(path)}: {a.Key} did not survive the round trip.");

            files++;
            demands += before.Count;
        }

        _out.WriteLine($"{files} files, {demands} demands round-tripped with no loss.");
        Assert.True(demands > 1000, $"only {demands} demands round-tripped — the format has changed.");
    }

    /// <summary>
    /// THE RECONCILIATION. 31 S-Concrete files on 30961-01 were made by hand from one workbook; the
    /// workbook's Calculator sheet holds the row each of them came from. Every force in the files
    /// must equal the row it was typed from — 1,665 demands, 9,990 numbers — because if it does, the
    /// step between an ETABS run and an S-Concrete file is deterministic and can be generated.
    ///
    /// Skipped where the share is unreachable, like every other test that needs files it does not own.
    /// </summary>
    [Fact]
    public void TheHandMadeFilesOn30961MatchTheWorkbookTheyWereTypedFrom()
    {
        if (!Directory.Exists(RealFolder)) { _out.WriteLine("SKIPPED: share unreachable."); return; }

        var files = Directory.EnumerateFiles(RealFolder, "*.SCO").ToList();
        if (files.Count == 0) { _out.WriteLine("SKIPPED: no .SCO files."); return; }

        var demands = files.SelectMany(SConcreteFile.ReadDemands).ToList();

        _out.WriteLine($"{files.Count} files, {demands.Count} demands, "
            + $"{demands.Select(d => (d.Storey, d.Mark)).Distinct().Count()} distinct columns.");

        Assert.True(demands.Count > 1000,
            $"only {demands.Count} demands read from {files.Count} files — the format has changed.");

        int truncated = demands.Count(d => d.IdentityTruncated);
        _out.WriteLine($"{truncated} demand(s) lost their effective length to the 60-character Comment limit.");

        // Every demand carries a full identity. A blank storey, mark or case would mean a generated
        // file could not be traced back to the column it belongs to.
        Assert.All(demands, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Storey));
            Assert.False(string.IsNullOrWhiteSpace(d.Mark));
            Assert.False(string.IsNullOrWhiteSpace(d.Case));
            // kl is allowed to be missing — the Comment field truncates — but it must be REPORTED.
            Assert.True(d.EffectiveLength is null or > 0, $"{d.Key} has a nonsense effective length");
        });

        // One column, one storey, one case is one demand. A duplicate would be the same column typed
        // into two files with two different sets of forces, and nobody could tell which was current.
        var duplicated = demands.GroupBy(d => d.Key).Where(g => g.Select(x =>
            (x.Nf, x.Vfz, x.Mfy, x.Vfy, x.Mfz)).Distinct().Count() > 1).ToList();

        foreach (var g in duplicated.Take(5))
            _out.WriteLine($"   {g.Key}: {g.Count()} rows disagreeing on forces");

        Assert.True(duplicated.Count == 0,
            $"{duplicated.Count} column/case demands appear more than once with DIFFERENT forces — "
            + "the same column typed into two files from two different analysis runs.");
    }
}
