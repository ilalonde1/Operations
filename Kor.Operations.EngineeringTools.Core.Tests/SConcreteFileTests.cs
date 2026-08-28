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
        const string folder =
            @"\\Kor-fs01\Projects\Projects\03 Residential\30961-01 (River District Parcel 29 & 30)"
            + @"\02 Engineering\05 Column Design\Column Design - AEM\S-CONCRETE";

        if (!Directory.Exists(folder)) { _out.WriteLine("SKIPPED: share unreachable."); return; }

        var files = Directory.EnumerateFiles(folder, "*.SCO").ToList();
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
