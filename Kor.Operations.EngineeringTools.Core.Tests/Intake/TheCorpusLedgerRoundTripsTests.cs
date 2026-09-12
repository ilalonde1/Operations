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
                "2026-09-10", true, 34254675, 63, 45, 71, 0, 18, 0, 65, true, null, 29, 63, 1127, 2497, 36, 34, 356.2, null);
            var sheet = new CorpusAnalyzer.SheetRow(run, "31168-01", 2, "S2.02", "plan", "S2.02 - LEVEL P3 PLAN / FOUNDATIONS PLAN, BLDG A & B", "P3", "1/8\" = 1'-0\"", 96,
                0, 71, 35, 13641, "S2.02_1_LEVEL P3 PLAN FOUNDATIONS PLAN BLDG A - & B.dxf", null, false, null, "walls: 6 outline(s) would not close; 7 wall panel(s) read", null);
            string sets = Path.Combine(root, "set.csv"), sheets = Path.Combine(root, "sheets.csv");
            CorpusAnalyzer.WriteSetCsv(sets, [set]);
            CorpusAnalyzer.WriteSheetCsv(sheets, [sheet]);

            // the readers are private to the analyzer; the CSV parser they share is not, and its inverse is the writer's quoting
            var fields = CorpusAnalyzer.Csv.Parse(File.ReadAllLines(sets)[1]);
            Assert.Equal(38, fields.Count);                                     // 27 of the set, 11 of its yardstick
            Assert.Equal(set.Pdf, fields[6]);                                     // the quotes and comma inside the path survive
            Assert.Equal("", fields[18]);                                         // a null is an empty field
            Assert.Equal("356.2", fields[25]);
            var sf = CorpusAnalyzer.Csv.Parse(File.ReadAllLines(sheets)[1]);
            Assert.Equal(19, sf.Count);
            Assert.Equal(sheet.Title, sf[5]);
            Assert.Equal("1/8\" = 1'-0\"", sf[7]);
            Assert.Equal("False", sf[15]);
            Assert.Equal("", sf[14]);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
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
