using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// Shapes an engineer's model can legally contain that this tool never writes itself.
///
/// The reference model is read to learn what is already built, so a shape the reader mishandles
/// costs a duplicated member — worse than a missing one, because a count cannot see it. These
/// shapes were found by measuring the portfolio rather than by imagining them.
/// </summary>
public class ReferenceModelShapeTests
{
    [Fact]
    public void ReferenceDiaphragmsAssignedAcrossStoreysAreReportedFromAreaAssigns()
    {
        var doc = E2kDocument.Parse(new[]
        {
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"F1\"  \"LEVEL 3\"  SECTION \"Rvt-Floor0\"  DIAPH  \"D1\"  OBJMESHTYPE \"DEFAULT\"",
            "  AREAASSIGN  \"F2\"  \"LEVEL 4\"  SECTION \"Rvt-Floor0\"  DIAPH  \"D1\"  OBJMESHTYPE \"DEFAULT\"",
            "  AREAASSIGN  \"F3\"  \"LEVEL 5\"  SECTION \"Rvt-Floor0\"  DIAPH  \"D1\"  OBJMESHTYPE \"DEFAULT\"",
            "  AREAASSIGN  \"F4\"  \"LEVEL 6\"  SECTION \"Rvt-Floor0\"  DIAPH  \"D1\"  OBJMESHTYPE \"DEFAULT\"",
            "  AREAASSIGN  \"F5\"  \"LEVEL 7\"  SECTION \"Rvt-Floor0\"  DIAPH  \"D1\"  OBJMESHTYPE \"DEFAULT\"",
            "  AREAASSIGN  \"F6\"  \"LEVEL 8\"  SECTION \"Rvt-Floor0\"  DIAPH  \"D1\"  OBJMESHTYPE \"DEFAULT\"",
            "  AREAASSIGN  \"F7\"  \"LEVEL 9\"  SECTION \"Rvt-Floor0\"  DIAPH  \"D1\"  OBJMESHTYPE \"DEFAULT\"",
        });

        var sectionLines = doc.LinesOf("AREA ASSIGNS");
        var warnings = DxfToEtabsService.ReferenceDiaphragmWarnings(doc);

        Assert.Equal(7, sectionLines.Count(line => line.Contains("DIAPH", StringComparison.OrdinalIgnoreCase)));
        var warning = Assert.Single(warnings);
        Assert.Contains("diaphragm \"D1\"", warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LEVEL 3", warning, StringComparison.Ordinal);
        Assert.Contains("LEVEL 9", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferenceRulesReadsSpandrelsFromTheSameAreaAssignsSectionShape()
    {
        var lines = new List<string>
        {
            "$ STORIES - IN SEQUENCE FROM TOP",
        };
        for (int i = 0; i < 8; i++)
            lines.Add($"  STORY \"LEVEL {i + 1}\"  HEIGHT 118");
        lines.Add("  STORY \"Base\"  ELEV 0");

        lines.Add("$ POINT COORDINATES");
        for (int i = 0; i < 8; i++)
        {
            lines.Add($"  POINT \"P{i}A\"  0 0 30");
            lines.Add($"  POINT \"P{i}B\"  100 0 0");
        }

        lines.Add("$ AREA CONNECTIVITIES");
        for (int i = 0; i < 8; i++)
            lines.Add($"  AREA \"S{i + 1}\"  PANEL  4  \"P{i}A\"  \"P{i}B\"  \"P{i}B\"  \"P{i}A\"  0  0  0  0");

        lines.Add("$ AREA ASSIGNS");
        for (int i = 0; i < 8; i++)
            lines.Add($"  AREAASSIGN  \"S{i + 1}\"  \"LEVEL {i + 1}\"  SECTION \"Rvt-Wall0\"  SPANDREL  \"SP{i + 1}\"  OBJMESHTYPE \"DEFAULT\"");

        var doc = E2kDocument.Parse(lines);
        var derived = ReferenceRules.OpeningHeight(doc, fallback: 84.0);

        Assert.Equal(8, doc.LinesOf("AREA ASSIGNS").Count(line => line.Contains("SPANDREL", StringComparison.OrdinalIgnoreCase)));
        Assert.True(derived.FromReference);
        Assert.Equal(88.0, derived.Value);
    }

    /// <summary>
    /// A panel whose corners sit at different storeys: `PANEL 4 "a" "b" "b" "a" 1 0 0 1`. 136 of
    /// them across the portfolio. This tool only ever writes `n n 0 0`, so the shape is untested
    /// by everything else in this suite, and the gap register claimed the reader would misparse it.
    /// Measured here rather than assumed either way: the reader takes plan corners and the storey
    /// from the assignment, so the trailing integers never enter the plan footprint at all.
    /// </summary>
    [Fact]
    public void ASkewedPanelIsStillReadAtItsDrawnPlanPositionAndStorey()
    {
        var doc = E2kDocument.Parse(new[]
        {
            "$ STORIES - IN SEQUENCE FROM TOP",
            "  STORY \"L2\"  HEIGHT 120 ",
            "  STORY \"L1\"  HEIGHT 120 ",
            "  STORY \"Base\"  ELEV 0 ",
            "$ POINT COORDINATES",
            "  POINT \"A\"  0 0",
            "  POINT \"B\"  240 0",
            "$ AREA CONNECTIVITIES",
            // Flat, as this tool writes them.
            "  AREA \"W-FLAT\"  PANEL  4  \"A\"  \"B\"  \"B\"  \"A\"  0  0  0  0",
            // Skewed: one end reaches a storey higher than the other.
            "  AREA \"W-SKEW\"  PANEL  4  \"A\"  \"B\"  \"B\"  \"A\"  1  0  0  1",
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"W-FLAT\"  \"L1\"  SECTION \"W12\"",
            "  AREAASSIGN  \"W-SKEW\"  \"L2\"  SECTION \"W12\"",
        });

        var geometry = E2kGeometryReader.Read(doc);

        var skew = Assert.Single(geometry.Walls, w => w.Name == "W-SKEW");
        Assert.Equal("L2", skew.Story);

        var flat = Assert.Single(geometry.Walls, w => w.Name == "W-FLAT");
        Assert.Equal(flat.A.X, skew.A.X, 3);
        Assert.Equal(flat.B.X, skew.B.X, 3);
        Assert.Equal(240, Math.Abs(skew.B.X - skew.A.X), 3);
    }
}
