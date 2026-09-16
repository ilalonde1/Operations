#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// STOREYS NAMED AS THE SET NAMES THEM (step 84, WP6a item 7): the classer behind `corpus-query storeys`, which
/// says over the corpus which names are the ladder's own, which are roofs and sub-levels by word, which carry an
/// elevation, and which are garbage. Every name below is one a built set carried on 2026-09-16 (57 names outside
/// the ladder's shapes across 30 sets); the plan's finish line for item 7 is the garbage count at zero.
/// WHAT IT DOES NOT: the reader that produces the names; whether a roof by word is the right storey.
/// </summary>
public sealed class AStoreyIsNamedAsTheSetNamesItTests
{
    [Theory]
    [InlineData("L7", StoreyNameClass.Kind.Ladder)]
    [InlineData("P3", StoreyNameClass.Kind.Ladder)]
    [InlineData("B-L12", StoreyNameClass.Kind.Ladder)]
    [InlineData("A-P2", StoreyNameClass.Kind.Ladder)]
    [InlineData("ROOF", StoreyNameClass.Kind.Ladder)]
    [InlineData("C-ROOF", StoreyNameClass.Kind.Ladder)]
    [InlineData("Base", StoreyNameClass.Kind.Ladder)]
    [InlineData("MAIN ROOF", StoreyNameClass.Kind.RoofByWord)]
    [InlineData("LOW ROOF", StoreyNameClass.Kind.RoofByWord)]
    [InlineData("MECH. ROOF", StoreyNameClass.Kind.RoofByWord)]
    [InlineData("ELEV. ROOF", StoreyNameClass.Kind.RoofByWord)]
    [InlineData("ROOF LEVEL", StoreyNameClass.Kind.RoofByWord)]
    [InlineData("PENTHOUSE", StoreyNameClass.Kind.RoofByWord)]
    [InlineData("T.O.CORE", StoreyNameClass.Kind.RoofByWord)]
    [InlineData("CANOPY", StoreyNameClass.Kind.RoofByWord)]
    [InlineData("L1M", StoreyNameClass.Kind.SubLevel)]
    [InlineData("P1M", StoreyNameClass.Kind.SubLevel)]
    [InlineData("L4B", StoreyNameClass.Kind.SubLevel)]
    [InlineData("L16R", StoreyNameClass.Kind.SubLevel)]
    [InlineData("P1(P1A)", StoreyNameClass.Kind.SubLevel)]
    [InlineData("L0/P1", StoreyNameClass.Kind.SubLevel)]
    [InlineData("B1", StoreyNameClass.Kind.LetterAndCount)]
    [InlineData("C4", StoreyNameClass.Kind.LetterAndCount)]
    [InlineData("R19", StoreyNameClass.Kind.LetterAndCount)]
    [InlineData("LEVEL +2.0", StoreyNameClass.Kind.Elevation)]
    [InlineData("LEVEL", StoreyNameClass.Kind.Garbage)]
    [InlineData("LEVEL -", StoreyNameClass.Kind.Garbage)]
    [InlineData("DESIGN", StoreyNameClass.Kind.Garbage)]
    [InlineData("VERTS.", StoreyNameClass.Kind.Garbage)]
    [InlineData("AMANITY", StoreyNameClass.Kind.Garbage)]
    [InlineData("LEVEL (L37)", StoreyNameClass.Kind.Garbage)]   // step 84 normalises this to L37 before it reaches a model
    [InlineData("LEVEL -5", StoreyNameClass.Kind.Garbage)]      // and this to P5
    public void EveryNameACorpusModelCarriedIsClassed(string name, StoreyNameClass.Kind kind)
    {
        Assert.Equal(kind, StoreyNameClass.Classify(name));
    }
}
