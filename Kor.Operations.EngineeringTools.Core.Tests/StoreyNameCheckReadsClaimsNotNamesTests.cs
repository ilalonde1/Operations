using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// The storey-name check must fire on a question about a storey the model does not have, and stay
/// quiet on a drawing that merely has a storey-shaped name.
/// </summary>
/// <remarks>
/// It reported 69 findings across four models that were all correct, in three shapes: a banked
/// rule's provenance, a list of drawings named by their label, and the sheet table's truncated name
/// column. Noise that is wrong every time it fires is worse than no check, because it trains you to
/// scroll past the time it is right — so each shape is pinned here, along with the one case the
/// check exists for.
/// </remarks>
public class StoreyNameCheckReadsClaimsNotNamesTests
{
    /// <summary>A model whose storeys are named the way the reference names them: L21, P1, Mezz.</summary>
    private static string[] Model() =>
    [
        "$ STORIES - IN SEQUENCE FROM TOP",
        "  STORY \"L21\"  HEIGHT 120",
        "  STORY \"L01\"  HEIGHT 120",
        "  STORY \"P1\"  HEIGHT 120",
        "  STORY \"Base\"  HEIGHT 0",
    ];

    private static IReadOnlyList<ModelViolation> Named(IReadOnlyList<string>? report, IReadOnlyList<string>? workbook)
        => ShippedModelInvariants.Check(Model(), 0.05, null, null, null, report, workbook)
            .Where(v => v.Rule == "storey-name-not-in-file")
            .ToList();

    /// <summary>
    /// The sheet table pads or truncates the drawing name to 52 characters, so a long name loses
    /// the ".dxf" the suffix strip keys on and the row was read as a claim.
    /// </summary>
    [Fact]
    public void ATruncatedSheetTableRowIsADrawingName()
    {
        string name = "Str-Structural Plan - DXF-S2-36_2_LEVEL 21 AMENIT...";
        Assert.Equal(52, name.Length);

        var found = Named([$"{name}     1        1      0     3      1"], null);

        Assert.Empty(found);
    }

    /// <summary>The same row, padded rather than truncated, still ends in .dxf and was always fine.</summary>
    [Fact]
    public void APaddedSheetTableRowIsADrawingNameToo()
    {
        var found = Named(["Plan - LEVEL 21.dxf".PadRight(52) + "     1        1      0     3      1"], null);

        Assert.Empty(found);
    }

    /// <summary>
    /// The check exists for this: a question that hands her a storey her file does not contain.
    /// </summary>
    [Fact]
    public void AQuestionAboutAStoreyTheModelLacksIsStillReported()
    {
        var found = Named(null, ["S4 | NEEDS YOU | HOW MANY SEPARATE SLABS ARE ON LEVEL 28?"]);

        var one = Assert.Single(found);
        Assert.Equal("LEVEL 28", one.Where);
        Assert.Contains("workbook names 'LEVEL 28'", one.What, StringComparison.Ordinal);
        Assert.Equal(ModelViolationSeverity.Advisory, one.Severity);
    }

    /// <summary>
    /// A sentence whose subject is absence names what is absent on purpose, and must keep its name.
    /// </summary>
    [Fact]
    public void ASentenceAboutWhatIsMissingMayNameIt()
    {
        var found = Named(["2 drawing(s) carry structure that is not in this model: LEVEL 28."], null);

        Assert.Empty(found);
    }

    /// <summary>
    /// A row on a sheet that is not about this model never reaches the check, because ClaimLines
    /// does not hand it over. This pins the sheet list itself: the rules ledger cites the job a
    /// value was measured on, and job 31138's workbook naming 31168's C-LEVEL 3 is correct.
    /// </summary>
    [Fact]
    public void OnlyTheSheetsThatSaySomethingAboutThisModelAreRead()
    {
        string path = Path.Combine(Path.GetTempPath(), $"kor-claimlines-{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                workbook.Worksheets.Add("Rules in force").Cell(1, 1).Value =
                    "dxf.donor-plate-likeness-margin | 0.98 | sending 31168 C-LEVEL 3 six storeys up";
                workbook.Worksheets.Add("Sheets read").Cell(1, 1).Value =
                    "Str-Structural Plan - DXF-S2-36_2_LEVEL 21 AMENITY PLAN Copy 1.dxf";
                workbook.Worksheets.Add("Questions").Cell(1, 1).Value =
                    "S4 | NEEDS YOU | HOW MANY SEPARATE SLABS ARE ON LEVEL 28?";
                workbook.SaveAs(path);
            }

            var claims = ModelQuestionnaire.ClaimLines(path);

            Assert.DoesNotContain(claims, l => l.Contains("C-LEVEL 3", StringComparison.Ordinal));
            Assert.DoesNotContain(claims, l => l.Contains("LEVEL 21 AMENITY", StringComparison.Ordinal));
            Assert.Contains(claims, l => l.Contains("LEVEL 28", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
