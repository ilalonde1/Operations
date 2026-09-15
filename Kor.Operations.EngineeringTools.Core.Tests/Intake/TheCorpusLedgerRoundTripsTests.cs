#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// The corpus ledger's rows survive their own CSV (the analyzer re-reads a set's last outcome from
/// it instead of rebuilding an unchanged set), and like failures group by the stable part of their
/// message so "no model, N sets: reason" is a count and not a list (completion plan WP1, 2026-09-11).
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a set row and a sheet row with nulls, quotes and commas in their text written
/// and read back equal (through the private readers the analyzer uses, exercised via a manifest hit);
/// the reason grouper stopping at the first number or quote. WHAT IT DOES NOT: the build itself
/// (the six-set bank and the verb's own output prove PdfOnlyBuild); the ledger tables (written only
/// where migration 083 has run; the writer's outcome is a printed line either way).
/// </remarks>
public sealed class TheCorpusLedgerRoundTripsTests
{
    [Fact]
    public void ASetRowAndItsSheetsComeBackFromTheirCsv()
    {
        string root = Path.Combine(Path.GetTempPath(), $"kor-ledger-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var run = Guid.NewGuid();
            var at = new DateTime(2026, 9, 11, 22, 0, 0, DateTimeKind.Utc);
            var set = new CorpusAnalyzer.SetRow(run, at, at.AddHours(-1), "31168-01", "03 Residential", "structural", @"\\Kor-fs01\Projects\x\31168-01 - 2026-09-10- YMCA, ""Langara"" - Stickfile.pdf",
                "2026-09-10", true, 34254675, 63, 45, 71, 0, 18, 0, 65, true, null, 29, 63, 1127, 2497, 36, 34, 356.2, null,
                // and a yardstick with its provenance (2026-09-14): the .EDB, the day it was written, and the days it predates the drawing
                Yardstick: "31168-01.e2k", YardstickStoreys: 60, SharedStoreys: 58, FrameFromGrids: false, FrameSupport: 1311,
                OursCompared: 1531, OursMedianMm: 10.9, OursWithin100: 1311, TheirsCompared: 1504, TheirsWithin100: 1300, YardstickNote: "frame from column registration",
                YardstickEdb: @"\\Kor-fs01\Projects\x\31168-01 Wind SLS_Model_SG_01.EDB", YardstickWritten: new DateOnly(2023, 9, 8), YardstickAgeDays: -2);
            var sheet = new CorpusAnalyzer.SheetRow(run, "31168-01", 2, "S2.02", "plan", "S2.02 - LEVEL P3 PLAN / FOUNDATIONS PLAN, BLDG A & B", "P3", "1/8\" = 1'-0\"", 96,
                0, 71, 35, 13641, "S2.02_1_LEVEL P3 PLAN FOUNDATIONS PLAN BLDG A - & B.dxf", null, false, null, "walls: 6 outline(s) would not close; 7 wall panel(s) read", null);
            string sets = Path.Combine(root, "set.csv"), sheets = Path.Combine(root, "sheets.csv");
            CorpusAnalyzer.WriteSetCsv(sets, [set]);
            CorpusAnalyzer.WriteSheetCsv(sheets, [sheet]);

            // the readers are private to the analyzer; the CSV parser they share is not, and its inverse is the writer's quoting
            var fields = CorpusAnalyzer.Csv.Parse(File.ReadAllLines(sets)[1]);
            Assert.Equal(41, fields.Count);                                     // 27 of the set, 11 of its yardstick, 3 of the yardstick's provenance
            Assert.Equal(set.Pdf, fields[6]);                                     // the quotes and comma inside the path survive
            Assert.Equal("", fields[18]);                                         // a null is an empty field
            Assert.Equal("356.2", fields[25]);
            var sf = CorpusAnalyzer.Csv.Parse(File.ReadAllLines(sheets)[1]);
            Assert.Equal(19, sf.Count);
            Assert.Equal(sheet.Title, sf[5]);
            Assert.Equal("1/8\" = 1'-0\"", sf[7]);
            Assert.Equal("False", sf[15]);
            Assert.Equal("", sf[14]);

            // and the TYPED readers the analyzer re-reads a kept set through give the records back equal (audit F25:
            // the first cut parsed fields and never asked the readers), under the run they are re-read into
            var run2 = Guid.NewGuid(); var at2 = at.AddDays(1);
            Assert.Equal(set with { RunId = run2, RunAtUtc = at2 }, CorpusAnalyzer.ReadSetRow(sets, run2, at2));
            Assert.Equal(sheet with { RunId = run2 }, Assert.Single(CorpusAnalyzer.ReadSheetRows(sheets, run2)));
            Assert.Equal(set, Assert.Single(CorpusAnalyzer.ReadSets(sets)));

            // A SET'S ROW IS WRITTEN THE MOMENT THE SET IS DONE (2026-09-14): the partial ledger a killed run leaves reads
            // back the same rows, header first, one row per append, whatever order the sets finished in
            string partial = Path.Combine(root, "ledger-sets.partial.csv");
            var gate = new object();
            CorpusAnalyzer.AppendSetRow(partial, set with { Job = "31169-01" }, gate);
            CorpusAnalyzer.AppendSetRow(partial, set, gate);
            var partialRows = CorpusAnalyzer.ReadSets(partial);
            Assert.Equal(2, partialRows.Count);
            Assert.Equal(set, partialRows[1]);
            Assert.Equal("31169-01", partialRows[0].Job);
            Assert.StartsWith("run_id,", File.ReadAllLines(partial)[0].TrimStart('﻿'), StringComparison.Ordinal);
            Assert.Equal(sheet, Assert.Single(CorpusAnalyzer.ReadSheets(sheets)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Audit F22 (2026-09-13): the writer joined a sheet's DXF files with " | " and corpus-query plan-titles
    /// split them on ";" — one view name with a ";" in it became two, and two files became one. Every reader
    /// of the column comes through the one splitter the writer's separator is declared beside.
    /// </summary>
    [Fact]
    public void ASheetsDxfFilesComeBackAsTheWriterJoinedThem()
    {
        string joined = string.Join(CorpusAnalyzer.DxfFileSeparator, ["S2.02_1_LEVEL 1 PLAN; BLDG A.dxf", "S2.02_2_LEVEL 1 PLAN, BLDG B.dxf"]);
        var files = CorpusAnalyzer.DxfFilesOf(joined);
        Assert.Equal(2, files.Count);
        Assert.Equal("S2.02_1_LEVEL 1 PLAN; BLDG A.dxf", files[0]);            // the ";" inside a name is not a separator
        Assert.Equal("S2.02_2_LEVEL 1 PLAN, BLDG B.dxf", files[1]);
        Assert.Empty(CorpusAnalyzer.DxfFilesOf(null));
        Assert.Empty(CorpusAnalyzer.DxfFilesOf(""));
    }

    [Fact]
    public void LikeFailuresGroupByTheStablePartOfTheirMessage()
    {
        Assert.Equal("Cannot build a model from these inputs: no plan sheet names a storey the levels file has (levels:",
            CorpusAnalyzer.Reason("Cannot build a model from these inputs: no plan sheet names a storey the levels file has (levels: 12, sheets: 3)"));
        Assert.Equal(CorpusAnalyzer.Reason("PDF not found 'a.pdf'"), CorpusAnalyzer.Reason("PDF not found 'b.pdf'"));
        Assert.Equal("(none)", CorpusAnalyzer.Reason(null));
        Assert.Equal("7 storeys", CorpusAnalyzer.Reason("7 storeys"));           // a message that opens with a number is kept whole
    }
}
