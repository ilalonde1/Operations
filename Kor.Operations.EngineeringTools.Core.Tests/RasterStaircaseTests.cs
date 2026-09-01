using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// 31168's LEVEL 2 went to the engineer with a diagonal slab edge drawn as 33 six-inch stair steps
/// — 114 vertices where the drawing has about twenty — and every count in the report was right.
/// She sent a picture of it on 31 August. This is the check that should have found it first.
/// </summary>
public class RasterStaircaseTests
{
    private static string[] ModelWith(params string[] ring)
    {
        var lines = new List<string>
        {
            "$ STORIES - IN SEQUENCE FROM TOP",
            "  STORY \"LEVEL 2\"  HEIGHT 120",
            "  STORY \"Base\"  HEIGHT 0",
            "",
            "$ POINT COORDINATES",
        };
        lines.AddRange(ring);
        return lines.ToArray();
    }

    /// <summary>A diagonal edge traced at a 6 in cell: the shape the engineer was sent.</summary>
    private static string[] Staircase()
    {
        var pts = new List<string>();
        var names = new List<string>();

        // Up the diagonal in 6 in steps, the raster's own shape.
        int n = 0;
        for (int i = 0; i < 20; i++)
        {
            pts.Add($"  POINT \"KP{++n}\"  {i * 6} {i * 6}");
            names.Add($"\"KP{n}\"");
            pts.Add($"  POINT \"KP{++n}\"  {i * 6 + 6} {i * 6}");
            names.Add($"\"KP{n}\"");
        }

        // Back around the outside in long runs.
        pts.Add($"  POINT \"KP{++n}\"  600 -400");
        names.Add($"\"KP{n}\"");
        pts.Add($"  POINT \"KP{++n}\"  0 -400");
        names.Add($"\"KP{n}\"");

        var model = new List<string>(ModelWith(pts.ToArray()))
        {
            "$ AREA CONNECTIVITIES",
            $"  AREA \"KF1\"  FLOOR  {names.Count}  {string.Join("  ", names)}  0 0 0 0",
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"KF1\"  \"LEVEL 2\"  SECTION \"x\"",
        };
        return model.ToArray();
    }

    /// <summary>The same ground, drawn the way the drawing draws it.</summary>
    private static string[] Straight()
    {
        var model = new List<string>(ModelWith(
            "  POINT \"KP1\"  0 0",
            "  POINT \"KP2\"  120 120",
            "  POINT \"KP3\"  600 120",
            "  POINT \"KP4\"  600 -400",
            "  POINT \"KP5\"  0 -400"))
        {
            "$ AREA CONNECTIVITIES",
            "  AREA \"KF1\"  FLOOR  5  \"KP1\"  \"KP2\"  \"KP3\"  \"KP4\"  \"KP5\"  0 0 0 0",
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"KF1\"  \"LEVEL 2\"  SECTION \"x\"",
        };
        return model.ToArray();
    }

    [Fact]
    [Trait("Speed", "Fast")]
    public void APlateTracedAsAStaircaseIsRefused()
    {
        var breaches = ShippedModelInvariants.Check(Staircase(), 0.05, Array.Empty<string>());

        var stair = Assert.Single(breaches, b => b.Rule == "outline-is-a-raster-staircase");
        Assert.True(stair.BlocksPublishing, "a staircase edge must stop the publish, not be noted");
        Assert.Contains("stair step", stair.What, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Speed", "Fast")]
    public void AnOutlineDrawnTheWayTheDrawingDrawsItPasses()
    {
        var breaches = ShippedModelInvariants.Check(Straight(), 0.05, Array.Empty<string>());

        Assert.DoesNotContain(breaches, b => b.Rule == "outline-is-a-raster-staircase");
    }
}
