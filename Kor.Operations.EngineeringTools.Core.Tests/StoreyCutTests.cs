using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// Cutting a site model down to the part of the building somebody asked for.
///
/// The shape of 31168, which is what these fixtures are: a shared parkade and podium, a mid-rise
/// on top of part of it, and two towers that rise past everything. The towers' upper floors carry
/// an A- or B- prefix; their floors lower down carry no prefix at all; and their GROUND floors
/// carry a prefix while sitting at grade inside the podium.
///
/// That last detail is why there are two filters and not one. "Building C only" and "nothing above
/// the mid-rise roof" are different questions, and the engineer asking for a YMCA and a parkade
/// without towers is asking the second one.
/// </summary>
public class StoreyCutTests
{
    // Elevations in inches. C-ROOF sits between LEVEL 10 and LEVEL 11, exactly as it does on 31168.
    private static E2kDocument SiteModel() => E2kDocument.Parse(new[]
    {
        "$ CONTROLS",
        "  UNITS  \"Kip\"  \"in\"",
        "$ STORIES - IN SEQUENCE FROM TOP",
        "  STORY \"B-LEVEL 28\"  HEIGHT 120",
        "  STORY \"A-LEVEL 27\"  HEIGHT 120",
        "  STORY \"LEVEL 12\"  HEIGHT 120",
        "  STORY \"LEVEL 11\"  HEIGHT 120",
        "  STORY \"C-ROOF\"  HEIGHT 120",
        "  STORY \"LEVEL 10\"  HEIGHT 120",
        "  STORY \"C-LEVEL 9\"  HEIGHT 120",
        "  STORY \"LEVEL 2\"  HEIGHT 120",
        "  STORY \"B-LEVEL 1\"  HEIGHT 120",
        "  STORY \"A-LEVEL 1\"  HEIGHT 120",
        "  STORY \"LEVEL P1\"  HEIGHT 120",
        "  STORY \"Base\"  ELEV 0",
    });

    private static IReadOnlyList<string> Names(E2kDocument doc)
        => doc.ReadStories().Select(s => s.Name).ToList();

    [Fact]
    public void CuttingAboveTheMidRiseRoofKeepsGradeAndTheParkade()
    {
        // The engineer's actual words: "I just need the YMCA building (mid-rise) ... parking +
        // YMCA above grade, but not towers above grade", and then "You should model L1 entirely".
        var doc = SiteModel();
        var dropped = doc.KeepStoreysUpTo("C-ROOF");
        var kept = Names(doc);

        // Both towers' GROUND floors survive: they are at grade, inside the podium she wants.
        Assert.Contains("A-LEVEL 1", kept);
        Assert.Contains("B-LEVEL 1", kept);
        Assert.Contains("LEVEL P1", kept);
        Assert.Contains("C-ROOF", kept);
        Assert.Contains("C-LEVEL 9", kept);

        // Everything above the mid-rise roof goes, prefixed or not.
        Assert.DoesNotContain("LEVEL 11", kept);
        Assert.DoesNotContain("LEVEL 12", kept);
        Assert.DoesNotContain("A-LEVEL 27", kept);
        Assert.DoesNotContain("B-LEVEL 28", kept);

        Assert.Equal(4, dropped.Count);
    }

    [Fact]
    public void TheNameFilterCannotExpressIt_WhichIsWhyTheElevationOneExists()
    {
        // Guards the reason this feature exists. KeepOnlyTower("C") gets BOTH halves wrong on this
        // shape: it keeps the unprefixed tower floors and throws away the ground floors that are
        // wanted. If someone ever "simplifies" the two filters into one, this fails.
        var doc = SiteModel();
        doc.KeepOnlyTower("C");
        var kept = Names(doc);

        Assert.Contains("LEVEL 11", kept);          // tower floor, no prefix, survives
        Assert.Contains("LEVEL 12", kept);
        Assert.DoesNotContain("A-LEVEL 1", kept);   // wanted at grade, removed
        Assert.DoesNotContain("B-LEVEL 1", kept);
    }

    [Fact]
    public void AStoreyAtTheSameHeightAsTheCutIsKept()
    {
        // Two towers interleaving at one elevation is normal on a site model. Cutting one of a
        // pair because it sorted second would remove real structure with nothing said.
        var doc = E2kDocument.Parse(new[]
        {
            "$ CONTROLS",
            "  UNITS  \"Kip\"  \"in\"",
            "$ STORIES - IN SEQUENCE FROM TOP",
            "  STORY \"ABOVE\"  HEIGHT 120",
            "  STORY \"TWIN-B\"  HEIGHT 0",
            "  STORY \"TWIN-A\"  HEIGHT 120",
            "  STORY \"Base\"  ELEV 0",
        });

        var dropped = doc.KeepStoreysUpTo("TWIN-A");
        var kept = Names(doc);

        Assert.Contains("TWIN-A", kept);
        Assert.Contains("TWIN-B", kept);            // same elevation, kept
        Assert.Equal(new[] { "ABOVE" }, dropped);
    }

    [Fact]
    public void AnUnknownStoreyNameIsRefusedAndListsWhatThereIs()
    {
        // Silently keeping every storey because a name was mistyped would hand back a site model
        // when somebody asked for one building, and nothing would say so.
        var doc = SiteModel();
        var before = Names(doc);
        var ex = Assert.Throws<InvalidOperationException>(() => doc.KeepStoreysUpTo("C-ROOFF"));

        Assert.Contains("C-ROOFF", ex.Message, StringComparison.Ordinal);
        Assert.Contains("C-ROOF", ex.Message, StringComparison.Ordinal);
        Assert.Contains("A-LEVEL 27", ex.Message, StringComparison.Ordinal);

        // Compared with what was there, not a number typed in: the point is that a refusal cuts
        // nothing, and a literal here only asserts what I guessed ReadStories counts.
        Assert.Equal(before, Names(doc));
    }
}
