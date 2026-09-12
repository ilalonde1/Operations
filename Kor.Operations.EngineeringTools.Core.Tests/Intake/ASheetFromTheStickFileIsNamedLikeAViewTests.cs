#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.Intake;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A sheet from the stick file is named the way the office's export names a view (intake step 15),
/// so the DXF-to-ETABS reader takes its storeys from the name as it does for every other sheet.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: the name from the sheet number and the title block's SHEET TITLE; that
/// PlanSheetNaming reads the parkade level and a level range out of it; the fallback when either
/// is missing; characters a file name refuses; the title on the page as the third source when the
/// block's field and the bookmark name no level (step 46). WHAT IT DOES NOT: a title block the
/// reader did not read at all, which is the fallback's case and the ledger's business.
/// </remarks>
[Collection(SheetNamingVocabularyCollection.Name)]   // PlanSheetNaming.Vocabulary is a mutable static another class rewrites
public sealed class ASheetFromTheStickFileIsNamedLikeAViewTests
{
    private static IReadOnlyDictionary<string, string> Block(params (string, string)[] fields)
        => fields.ToDictionary(f => f.Item1, f => f.Item2, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void TheNameIsTheSheetNumberAViewIndexAndTheTitle()
    {
        string name = SheetDxfName.For("S2.01", Block(("SHEET TITLE", "LEVEL P3 PLAN - FOUNDATION PLAN - BLDG A & B")), "31168-p11");
        Assert.Equal("S2.01_1_LEVEL P3 PLAN - FOUNDATION PLAN - BLDG A & B.dxf", name);
        var info = PlanSheetNaming.Parse(name);
        Assert.Equal(new[] { 3 }, info.ParkadeLevels);

        string typical = SheetDxfName.For("S2.20.1", Block(("SHEET TITLE", "BLDG A - LEVEL 3 AND LEVEL 4 (L4-L14) PLAN - CONCRETE OUTLINE")), "x");
        Assert.Equal(Enumerable.Range(4, 11), PlanSheetNaming.Parse(typical).Levels);
    }

    [Fact]
    public void WithoutANumberOrATitleThePageNumberNamesIt()
    {
        Assert.Equal("31168-p11.dxf", SheetDxfName.For(null, Block(("SHEET TITLE", "LEVEL P3 PLAN")), "31168-p11"));
        Assert.Equal("31168-p11.dxf", SheetDxfName.For("S2.01", Block(("SCALE", "1/8\" = 1'-0\"")), "31168-p11"));
        Assert.Equal("S2.01_1_LEVEL P3 PLAN.dxf", SheetDxfName.For(null, Block(("SHEET NUMBER", "S2.01"), ("SHEET TITLE", "LEVEL P3 PLAN")), "31168-p11"));
    }

    /// <summary>
    /// The title written on the page is the third statement of a sheet's name (intake step 46): a
    /// sheet with a number and a level the storey reader read, but no title-block field and no
    /// bookmark, was named by the PDF's stem and page, and a name that says nothing places nothing
    /// (298 of 486 views on the 72 sets the corpus left without a ladder, 2026-09-11).
    /// </summary>
    [Fact]
    public void TheTitleOnThePageNamesTheSheetWhenTheBlockAndTheBookmarkDoNot()
    {
        // no field, no bookmark, the page says LEVEL 2 PLAN - CONCRETE OUTLINE
        Assert.Equal("S2.03.1_1_LEVEL 2 PLAN - CONCRETE OUTLINE.dxf",
            SheetDxfName.For("S2.03.1", Block(("SCALE", "1/8\" = 1'-0\"")), "31009-p18", null, "LEVEL 2 PLAN - CONCRETE OUTLINE"));
        // the page's title carrying its own sheet number loses it, as a bookmark's does
        Assert.Equal("S2.03.1_1_LEVEL 2 PLAN.dxf", SheetDxfName.For("S2.03.1", Block(), "x", null, "S2.03.1 - LEVEL 2 PLAN"));
        // a field that names a level still wins over the page; a field that names none loses to a page that does
        Assert.Equal("S2.03.1_1_LEVEL 3 PLAN.dxf", SheetDxfName.For("S2.03.1", Block(("SHEET TITLE", "LEVEL 3 PLAN")), "x", null, "LEVEL 2 PLAN"));
        Assert.Equal("S2.03.1_1_LEVEL 2 PLAN.dxf", SheetDxfName.For("S2.03.1", Block(("SHEET TITLE", "GENERAL NOTES")), "x", null, "LEVEL 2 PLAN"));
        // and with no number nothing names it, whatever the page says
        Assert.Equal("31009-p18.dxf", SheetDxfName.For(null, Block(), "31009-p18", null, "LEVEL 2 PLAN"));
    }

    [Fact]
    public void WhatAFileNameRefusesBecomesASpace()
    {
        Assert.Equal("S2.01_1_LEVEL 3 4 PLAN.dxf", SheetDxfName.For("S2.01", Block(("SHEET TITLE", "LEVEL 3/4  PLAN")), "x"));
    }

    // ---------------------------------------------------------------------------------------
    // A sheet's title is whichever of its statements names a level (intake step 30)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The architect's title block field reads "-" and the PDF's bookmark reads "A101-LEVEL P1
    /// PLAN": the bookmark names the level, so it is the title, with its own sheet number dropped.
    /// 31170's set, and 31202's — both built nothing while the field alone was read.
    /// </summary>
    [Fact]
    public void TheBookmarkNamesTheSheetWhenTheTitleBlockDoesNot()
    {
        Assert.Equal("A101_1_LEVEL P1 PLAN.dxf", SheetDxfName.For("A101", Block(("SHEET TITLE", "-")), "x", "A101-LEVEL P1 PLAN"));
        Assert.Equal("A203_1_LEVEL 2 SLAB PLAN.dxf", SheetDxfName.For("A203", Block(), "x", "A203 - LEVEL 2 SLAB PLAN"));
    }

    /// <summary>And where the title block's field names the level itself, it is kept over the bookmark, as before.</summary>
    [Fact]
    public void TheTitleBlocksFieldIsKeptWhenItNamesTheLevel()
    {
        Assert.Equal("S2.01_1_LEVEL P3 PLAN.dxf", SheetDxfName.For("S2.01", Block(("SHEET TITLE", "LEVEL P3 PLAN")), "x", "S2.01 - something else"));
    }

    /// <summary>Neither names a level: the field is still preferred, then the bookmark, then the fallback.</summary>
    [Fact]
    public void WithNoLevelInEitherTheFieldThenTheBookmarkThenTheFallback()
    {
        Assert.Equal("A000_1_COVER.dxf", SheetDxfName.For("A000", Block(("SHEET TITLE", "COVER")), "x", "A000-GENERAL"));
        Assert.Equal("A000_1_GENERAL.dxf", SheetDxfName.For("A000", Block(), "x", "A000-GENERAL"));
        Assert.Equal("x.dxf", SheetDxfName.For("A000", Block(), "x", null));
    }
}
