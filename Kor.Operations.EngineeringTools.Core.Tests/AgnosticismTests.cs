using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// What this tool assumes about drawings it has never seen.
///
/// Every rule in it was measured against two buildings drafted by one office to one convention.
/// That is enough to prove it works on those two and nothing more, and the failures it hides are
/// the quiet kind: not a wrong member, an absent one, with every count agreeing because nothing
/// was read. These tests use layer names KOR does not use, on purpose.
/// </summary>
public class AgnosticismTests
{
    // A US National CAD Standard set. Note what happens with each against KOR's defaults:
    // "WALL" is a generous pattern and matches this wall layer by luck, while "_COL" misses
    // S-COLS on the underscore and "SLABEDG" misses S-SLAB-EDGE on the hyphen. Partial matching
    // is the worst outcome available -- the model comes back with walls and no columns or floors,
    // which looks like a building rather than like a failure.
    private const string ForeignWall = "S-CONC-WALL-NEW";
    private const string ForeignColumn = "S-COLS";
    private const string ForeignSlab = "S-SLAB-EDGE";

    [Fact]
    public void GeometryItCannotReadIsNamedEvenOnLayersItDoesNotRecognise()
    {
        // The failure this exists for: unreadable entities were reported only once their layer had
        // already matched WALL, _COL or SLABEDG. So the one drawing set that most needed telling --
        // one whose layer names this tool does not know -- was the one it stayed silent about. A
        // hatch on S-CONC produced an empty model, no geometry, and a report naming neither.
        string[] dxf =
        [
            "0", "SECTION", "2", "ENTITIES",
            "0", "HATCH", "8", "S-CONC",
            "0", "HATCH", "8", "S-CONC",
            "0", "TEXT", "8", "A-ANNO-TEXT",
            "0", "ENDSEC", "0", "EOF",
        ];

        var unread = DxfPlanReader.UnsupportedStructuralEntities(dxf, new PlanClassificationOptions());

        var hatch = Assert.Single(unread, e => e.EntityType.Equals("HATCH", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("S-CONC", hatch.Layer);
        Assert.Equal(2, hatch.Count);

        // Annotation is not reported. "Everything unreadable" would bury the hatch under the
        // dimensions and text of a real sheet, which is its own kind of silence.
        Assert.DoesNotContain(unread, e => e.EntityType.Equals("TEXT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LayerRolesComeFromTheRulesSoAnotherFirmsNamesCanBeRead()
    {
        // With the patterns compiled in, this was unreachable without editing C# and redeploying.
        var kor = new PlanClassificationOptions();
        Assert.Null(kor.RoleOf(ForeignColumn));
        Assert.Null(kor.RoleOf(ForeignSlab));
        Assert.Equal("walls", kor.RoleOf(ForeignWall));   // matched by luck, which is the trap

        var theirs = kor with
        {
            WallLayerPatterns = ["CONC-WALL"],
            ColumnLayerPatterns = ["-COLS"],
            SlabLayerPatterns = ["SLAB-EDGE"],
        };

        Assert.Equal("walls", theirs.RoleOf(ForeignWall));
        Assert.Equal("columns", theirs.RoleOf(ForeignColumn));
        Assert.Equal("slab edges", theirs.RoleOf(ForeignSlab));
    }

    [Fact]
    public void ColumnsAreTestedBeforeWallsWhateverTheNamesAre()
    {
        // A layer can satisfy both patterns. Order is the rule, not an accident of which list was
        // consulted first, and it has to survive the patterns becoming data.
        var options = new PlanClassificationOptions
        {
            WallLayerPatterns = ["WALL"],
            ColumnLayerPatterns = ["COL"],
            SlabLayerPatterns = ["SLABEDG"],
        };

        Assert.Equal("columns", options.RoleOf("V_COL-WALL"));
    }

    [Fact]
    public void EveryPlaceThatAsksWhatALayerIsGivesTheSameAnswer()
    {
        // The classifier, the ledger and the unread-entity report each carried their own copy of
        // this rule. They agreed only because someone kept them agreeing by hand.
        var options = new PlanClassificationOptions
        {
            WallLayerPatterns = ["CONC-WALL"],
            ColumnLayerPatterns = ["-COLS"],
            SlabLayerPatterns = ["SLAB-EDGE"],
        };

        var ledger = LayerLedger.Build(
            [[new DxfSegment(ForeignWall, new DxfPoint(0, 0), new DxfPoint(100, 0))],
             [new DxfSegment(ForeignSlab, new DxfPoint(0, 0), new DxfPoint(100, 0))]],
            options);

        Assert.Equal("walls", Assert.Single(ledger, e => e.Layer == ForeignWall).Role);
        Assert.Equal("slab edges", Assert.Single(ledger, e => e.Layer == ForeignSlab).Role);
        Assert.Empty(LayerLedger.RolesMissingWithGeometryUnclaimed(ledger));
    }

    [Fact]
    public void AnEmptyLayerListIsRefusedRatherThanMatchingNothing()
    {
        // A pattern list of nothing matches no layer, so the run reads every drawing, finds no
        // structure anywhere, and reports a building with no members as though that were the
        // answer. The fallback has to win over an empty rule.
        var settings = new Dictionary<string, RuleSetting>(StringComparer.OrdinalIgnoreCase)
        {
            ["dxf.wall-layer-patterns"] =
                new("dxf.wall-layer-patterns", double.NaN, RuleSettings.TextUnits, "replay-verified", "t", "t")
                { Text = "  ;  ; " },
        };

        Assert.Equal(["WALL"], settings.ListOr("dxf.wall-layer-patterns", ["WALL"]));
    }
}
