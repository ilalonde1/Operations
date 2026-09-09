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
/// is missing; characters a file name refuses. WHAT IT DOES NOT: a title block the reader did not
/// read at all, which is the fallback's case and the ledger's business.
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

    [Fact]
    public void WhatAFileNameRefusesBecomesASpace()
    {
        Assert.Equal("S2.01_1_LEVEL 3 4 PLAN.dxf", SheetDxfName.For("S2.01", Block(("SHEET TITLE", "LEVEL 3/4  PLAN")), "x"));
    }
}
