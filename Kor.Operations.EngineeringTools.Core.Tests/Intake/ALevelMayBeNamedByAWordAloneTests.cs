#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;
using GP = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.GeomPath;
using PC = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.PageContent;
using TT = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.TextToken;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A level may be named by a word alone (intake step 41). "LEVEL 19" is a label and a value; "ROOF",
/// "HIGH ROOF", "PENTHOUSE ROOF" are a level's whole name, with nothing to the right but the level
/// line. KOR's sections name their roof levels this way and the ladder reader, wanting a value after
/// a label, read none of them — every roof plan landed stacked on the top numbered storey. Also: a
/// name written on two lines is one name, "ROOF LEVEL" is the level ROOF LEVEL and not a LEVEL
/// wanting a value, and a set has ONE base — a detail's own ladder is reported, not chained.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a ladder LEVEL 1, LEVEL 2, ROOF giving the storey ROOF over L2 at the drawn
/// height; "ROOF LEVEL" read as one name; "PENTHOUSE" over "ROOF" on two lines read as PENTHOUSE
/// ROOF at the lower line; a second ladder chaining from a base of its own reported as unchained and
/// not placed. WHAT IT DOES NOT: the real sets (31065 ROOF at 67,919 and 31138 ROOF at 85,905 read;
/// 31202's PENTHOUSE stated twice, 2026-09-10); the KorStandards row `dxf.level.name-words`, not
/// seeded; a roof word that is a caption on a plan (the ladder wants three rows).
/// </remarks>
public sealed class ALevelMayBeNamedByAWordAloneTests
{
    private const double W = 3024, H = 2160;
    private static TT Tok(string text, double x, double y) => new(text, x, y, x - 10, y - 4, x + 10, y + 4);
    private const string Scale = "1/8\" = 1'-0\"";                                     // 100 pt = 3,387 mm

    [Fact]
    public void ARoofWordAloneIsTheTopLevelOfTheLadder()
    {
        var words = new List<TT>
        {
            Tok("LEVEL", 2400, 700), Tok("1", 2440, 700),
            Tok("LEVEL", 2400, 800), Tok("2", 2440, 800),
            Tok("ROOF", 2400, 900),
        };
        var storeys = StoreyLadder.Read(new PC(1, W, H, words, new List<GP>()), Scale, []);
        Assert.Equal(2, storeys.Count);
        Assert.Equal("ROOF", storeys[0].Level); Assert.Equal("L2", storeys[0].LevelBelow);
        Assert.Equal(3387, storeys[0].HeightMm, 0);
    }

    [Fact]
    public void RoofLevelIsOneNameAndATwoLineNameIsOneName()
    {
        var words = new List<TT>
        {
            Tok("LEVEL", 2400, 700), Tok("12", 2440, 700),
            Tok("LEVEL", 2400, 800), Tok("13", 2440, 800),
            Tok("ROOF", 2380, 900), Tok("LEVEL", 2410, 900),                           // "ROOF LEVEL": the level ROOF LEVEL
            Tok("PENTHOUSE", 2400, 1000), Tok("ROOF", 2400, 991),                      // "PENTHOUSE" written over "ROOF": one name at ROOF's line
        };
        var storeys = StoreyLadder.Read(new PC(1, W, H, words, new List<GP>()), Scale, []);
        var names = storeys.Select(s => s.Level).ToList();
        Assert.Contains("ROOF LEVEL", names);
        Assert.Contains("PENTHOUSE ROOF", names);
        Assert.DoesNotContain("PENTHOUSE", names);
        Assert.DoesNotContain(storeys, s => s.Level == "ROOF" && s.LevelBelow == "ROOF LEVEL");   // no phantom level a line above
        var top = Assert.Single(storeys, s => s.Level == "PENTHOUSE ROOF");
        Assert.Equal("ROOF LEVEL", top.LevelBelow);
        Assert.Equal(3082, top.HeightMm, 0);                                            // 91 pt: ROOF's line at 991, not PENTHOUSE's at 1000
    }

    [Fact]
    public void ASetHasOneBaseAndADetailsOwnLadderIsReportedNotChained()
    {
        var storeys = new List<StoreyLadder.Storey>
        {
            new("L2", "L1", 3000, 0), new("L3", "L2", 3000, 0), new("ROOF", "L3", 3000, 0),
            new("HIGH ROOF", "LOW ROOF", 1219, 0),                                     // a parapet detail's two lines
        };
        var chain = SetStoreys.Levels(SetStoreys.Reconcile([(1, storeys)], 1));
        Assert.Equal(["L1"], chain.Bases);
        Assert.Equal(["L1", "L2", "L3", "ROOF"], chain.Levels.Select(l => l.Name));
        Assert.Contains(chain.Unchained, u => u.StartsWith("HIGH ROOF over LOW ROOF", StringComparison.Ordinal));
    }
}
