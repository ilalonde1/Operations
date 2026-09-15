#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Dxf;

/// <summary>
/// WALL FACES IN DIFFERENT OPEN CHAINS PAIR ONLY WHERE THE ROUTE ASKS (step 75, 2026-09-15; the audit's finding 1).
/// Two parallel open lines on a wall layer, 8 in apart and 5 m long, are one wall to the Revit route (31065's
/// exterior wall arrives as nineteen open chains) and nothing to the PDF route, whose reader already paired every
/// face it could with the fill in hand. Until step 75 the branch's 18-inch cap was a literal, so in a millimetre
/// drawing the cap was 18 mm and the branch never fired; brought alive on the corpus it added 32 walls on 9 of 296
/// sets, two of them at hatching on the architect's set. WHAT THIS COVERS: the pair made in inches and in
/// millimetres with PairOpenFaces on; nothing made with it off; the cap converted (a 20-inch gap never pairs).
/// WHAT IT DOES NOT: the PDF route's own face pairing (the reader's, tested with it); which sets it changes (the
/// experiment ledger ledger-sets-2026-09-15-exp-open-face-pairs.csv).
/// </summary>
public sealed class OpenFacesPairOnlyWhereTheRouteAsksTests
{
    // staggered, so their ends are 100 in apart and no bridge can close them into a rectangle: only the pairing can make a wall
    private static IEnumerable<DxfSegment> TwoFaces(double unitsPerInch, double gapInches)
    {
        double length = 200 * unitsPerInch, stagger = 100 * unitsPerInch, gap = gapInches * unitsPerInch;
        yield return new DxfSegment("KOR_V-WALL", new DxfPoint(0, 0), new DxfPoint(length, 0));
        yield return new DxfSegment("KOR_V-WALL", new DxfPoint(stagger + length, gap), new DxfPoint(stagger, gap));
    }

    [Theory]
    [InlineData(1.0)]         // inches, a Revit DXF
    [InlineData(25.4)]        // millimetres, the PDF route's unit
    public void TwoOpenFacesEightInchesApartAreOneWallWhenAskedAndNothingWhenNot(double unitsPerInch)
    {
        var options = new PlanClassificationOptions().InUnitOf(1.0 / unitsPerInch) with { ConnectWalls = false, FloorFromPerimeterWall = false };

        var paired = StructuralPlanClassifier.Classify(TwoFaces(unitsPerInch, 8), options);
        var wall = Assert.Single(paired.Walls);
        Assert.Equal(8 * unitsPerInch, wall.Thickness, 3);

        var pdfRoute = StructuralPlanClassifier.Classify(TwoFaces(unitsPerInch, 8), options with { PairOpenFaces = false });
        Assert.Empty(pdfRoute.Walls);

        // the cap is a length in the drawing's unit: a 20-inch gap is a corridor, not a wall, in either unit
        Assert.Empty(StructuralPlanClassifier.Classify(TwoFaces(unitsPerInch, 20), options).Walls);
    }
}
