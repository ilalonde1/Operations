#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;
using GP = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.GeomPath;
using PC = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.PageContent;
using TT = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.TextToken;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// An assembly schedule is a legend of cards, and a card is read whole (intake step 32).
///
/// An architect's set states its wall and floor types as cards: "CODE - NAME" with the code drawn
/// again in its symbol beside it, the build-up one line per layer, F.R.R. and S.T.C. with the value
/// provided and the reference, remarks. The plans tag walls with the codes. 31170's A005 carries 21
/// wall cards and A006 ten floor cards; the five KOR sets carry none, and read none.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: two cards side by side and one below, each read with its own layers, ratings,
/// material and thickness; a card whose symbol disagrees with its heading read anyway and the
/// disagreement recorded as a finding; a dashed row on a sheet with no confirmed card not being a
/// card; the material from the name before the layers; the thickest structural layer being the
/// thickness ("2" concrete pavers on a 12" slab"); a stud spacing "@ 400mm O.C." not being a
/// thickness; a decimal inch; the kind from the sheet title. WHAT IT DOES NOT: the real sheets
/// (`takeoff pdf-assemblies` on the six sets: 31 cards on 31170, 0 on the others, 2026-09-10); a
/// card whose lines the extractor wraps onto the next column; a vocabulary read from KorStandards
/// rather than the compiled defaults; and which wall on a plan carries which code.
/// </remarks>
public sealed class AnAssemblyScheduleIsALegendOfCardsTests
{
    private const double W = 3024, H = 2160;

    // tokens are placed in page-DOWN coordinates here and turned to PdfPig's bottom-up y, as the reader expects
    private static TT Tok(string text, double x, double yDown, double w = 0)
        => new(text, x + (w > 0 ? w / 2 : 10), H - yDown, x, H - yDown - 4, x + (w > 0 ? w : 20), H - yDown + 4);

    /// <summary>A card at (x, y): symbol, heading, layers, F.R.R./S.T.C. rows with a reference.</summary>
    private static IEnumerable<TT> Card(double x, double y, string code, string name, string[] layers, string? frr, string? stc, string symbol = "")
    {
        yield return Tok(symbol.Length > 0 ? symbol : code, x, y + 1, 20);        // in its symbol, a clear gap before the heading
        yield return Tok(code, x + 44, y, 30);
        yield return Tok("-", x + 78, y + 3, 8);
        yield return Tok(name, x + 90, y, 200);
        for (int i = 0; i < layers.Length; i++)
        {
            yield return Tok("-", x + 160, y + 27 + i * 12 + 3, 6);
            yield return Tok(layers[i], x + 170, y + 27 + i * 12, 300);
        }
        yield return Tok("PROVIDED", x + 77, y + 230, 40);
        yield return Tok("REFERENCE CODE", x + 162, y + 230, 70);
        yield return Tok("F.R.R.", x + 3, y + 248, 30);
        if (frr is not null) yield return Tok(frr, x + 85, y + 246, 20);
        yield return Tok("V.B.B.L. 2019 / DIV-B / TABLE D-2.1.1", x + 162, y + 247, 200);
        yield return Tok("S.T.C.", x + 3, y + 266, 30);
        if (stc is not null) yield return Tok(stc, x + 85, y + 264, 20);
    }

    private static PC Page(params IEnumerable<TT>[] cards)
        => new(1, W, H, cards.SelectMany(c => c).ToList(), new List<GP>());

    [Fact]
    public void CardsSideBySideAndBelowAreEachReadWhole()
    {
        var page = Page(
            Card(156, 80, "C16", "16\" C.I.P WALL", ["457mm (16\") TYP. CONCRETE WALL AS PER STRUCTURAL DRAWING", "PAINT FINISH"], "2HR", null),
            Card(822, 80, "S8.1", "STEEL STUD PARTY WALL", ["2 LAYERS OF 15.9mm (5/8\") TYPE-X G.W.B.", "41mmx92mm STEEL STUDS @ 400mm (16\") O.C."], "1HR", "55"),
            Card(156, 368, "B8", "8\" CMU", ["203mm (8\") CONCRETE BLOCK WALL"], "2HR", null));

        var cards = AssemblySchedule.Read(page, "A005-SCHEDULES - WALLS", 3);
        Assert.Equal(3, cards.Count);

        var c16 = cards.Single(c => c.Code == "C16");
        Assert.Equal("WALL", c16.Kind);
        Assert.Equal("16\" C.I.P WALL", c16.Name);
        Assert.Equal(2, c16.Layers.Count);
        Assert.Equal(AssemblySchedule.Material.Concrete, c16.Material);
        Assert.True(c16.IsStructural);
        Assert.Equal(406.4, c16.ThicknessMm!.Value, 1);
        Assert.Equal("2HR", c16.Ratings["F.R.R."]);
        Assert.Contains(c16.References, r => r.StartsWith("V.B.B.L.", StringComparison.Ordinal));

        var s81 = cards.Single(c => c.Code == "S8.1");
        Assert.Equal(AssemblySchedule.Material.Stud, s81.Material);
        Assert.False(s81.IsStructural);
        Assert.Equal("1HR", s81.Ratings["F.R.R."]);
        Assert.Equal("55", s81.Ratings["S.T.C."]);
        Assert.Equal(2, s81.Layers.Count);                                     // its own two, not C16's

        var b8 = cards.Single(c => c.Code == "B8");
        Assert.Equal(AssemblySchedule.Material.Masonry, b8.Material);
        Assert.True(b8.IsStructural);
        Assert.Single(b8.Layers);
    }

    [Fact]
    public void ACardWhoseSymbolDisagreesIsReadAndTheDisagreementRecorded()
    {
        // 31170's A005 draws "C13 - 13" C.I.P WALL" with the symbol C12 beside it: a copied card
        var page = Page(
            Card(156, 80, "C12", "12\" C.I.P WALL", ["305mm (12\") CONCRETE WALL"], "2HR", null),
            Card(822, 80, "C13", "13\" C.I.P WALL", ["330mm (13\") CONCRETE WALL"], "2HR", null, symbol: "C12"));
        var cards = AssemblySchedule.Read(page, "SCHEDULES - WALLS", 3);
        Assert.Equal(2, cards.Count);
        var c13 = cards.Single(c => c.Code == "C13");
        Assert.Contains(c13.Remarks, r => r.Contains("symbol beside this card reads C12", StringComparison.Ordinal));
        Assert.DoesNotContain(cards.Single(c => c.Code == "C12").Remarks, r => r.Contains("symbol", StringComparison.Ordinal));
    }

    [Fact]
    public void ADashedRowOnASheetWithNoConfirmedCardIsNotACard()
    {
        // a dowel schedule's rows read "DW1 - 4-30M3200 @ 400 DOWELS" (31065): no symbol beside any of them
        var page = new PC(1, W, H, new List<TT>
        {
            Tok("DW1", 200, 500, 30), Tok("-", 236, 503, 8), Tok("4-30M3200 @ 400 DOWELS", 250, 500, 200),
            Tok("DW2", 200, 520, 30), Tok("-", 236, 523, 8), Tok("9-30M3200 DOWELS", 250, 520, 200),
        }, new List<GP>());
        Assert.Empty(AssemblySchedule.Read(page, "DOWEL SCHEDULE", 9));
    }

    [Fact]
    public void TheNameDecidesTheMaterialBeforeTheLayers()
    {
        // a concrete wall with gypsum furring on it is a concrete wall
        Assert.Equal(AssemblySchedule.Material.Concrete, AssemblySchedule.MaterialOf("12\" C.I.P WALL", ["15.9mm G.W.B. ON FURRING"]));
        // a parapet whose name says nothing and whose core is 6" C.I.P is concrete
        Assert.Equal(AssemblySchedule.Material.Concrete, AssemblySchedule.MaterialOf("EXT. PARAPET WALL INSULATED", ["FIBRE-CEMENT PANEL", "METAL GIRTS", "152MM (6\") C.I.P AS PER STRUC. DWG"]));
        // a shaft wall of gypsum liner and C-H studs is a partition
        Assert.Equal(AssemblySchedule.Material.Stud, AssemblySchedule.MaterialOf("VERTICAL SHAFT WALL 1HR RATING", ["25mm GYPSUM LINER PANEL", "C-H STEEL STUDS @ 600mm O.C."]));
        Assert.Equal(AssemblySchedule.Material.Unknown, AssemblySchedule.MaterialOf("ROOF OVER ELEVATOR", ["MEMBRANE", "INSULATION"]));
    }

    [Fact]
    public void TheThicknessIsTheNamesElseTheThickestStructuralLayer()
    {
        Assert.Equal(190.5, AssemblySchedule.ThicknessOf("7.5\" SUSPENDED CONCRETE SLAB", [], AssemblySchedule.Material.Concrete)!.Value, 1);
        // 2" concrete pavers sit on a 12" concrete slab: the slab is the assembly
        Assert.Equal(305, AssemblySchedule.ThicknessOf("PAVERS OVER PARKADE",
            ["51mm (2\") CONCRETE PAVERS", "DRAINAGE MAT", "305mm (12\") SUSPENDED CONCRETE SLAB AS PER STRUC. DWG"], AssemblySchedule.Material.Concrete)!.Value, 1);
        // a stud spacing is not a thickness
        Assert.Equal(63.5, AssemblySchedule.ThicknessOf("VERTICAL SHAFT WALL",
            ["C-H 63.5mm (2.5\") DEEP STEEL STUDS FROM 0.627mm GALV. STEEL @ 600mm O.C."], AssemblySchedule.Material.Stud)!.Value, 1);
    }

    [Fact]
    public void TheKindIsTheSheetsLastWordSingular()
    {
        Assert.Equal("WALL", AssemblySchedule.KindOf("A005-SCHEDULES - WALLS"));
        Assert.Equal("FLOOR", AssemblySchedule.KindOf("A006-SCHEDULES - FLOORS"));
        Assert.Equal("ROOF", AssemblySchedule.KindOf("ROOF SCHEDULE"));
        Assert.Equal("ASSEMBLY", AssemblySchedule.KindOf("ROOF ASSEMBLY SCHEDULE"));   // the last word before SCHEDULE is what is scheduled
    }
}
