using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// What happens when the inputs are not the two well-formed drawing sets everything was measured
/// against. A real job eventually contains a truncated export, a file that is not a DXF, a
/// coordinate nobody checked, or a reference model that turns out to be this tool's own output.
///
/// The failure that matters is not the crash. It is the run that fails and still leaves something
/// behind that looks like an answer.
/// </summary>
public class HostileInputTests
{
    [Fact]
    public void GeometryFarFromTheOriginIsFlaggedRatherThanWrittenQuietly()
    {
        // One wall at 1,000,000,000,000 inches generated cleanly and exited zero: right count,
        // fifteen million miles from the building. Everything upstream is relative -- a Revit
        // export sits thousands of inches out and that is normal -- so nothing had any reason to
        // care about absolute magnitude, and nothing did.
        var near = Warn(0);
        Assert.DoesNotContain(near, w => w.Contains("from the model origin", StringComparison.Ordinal));

        var far = Warn(1_000_000_000_000d);
        string flag = Assert.Single(far, w => w.Contains("from the model origin", StringComparison.Ordinal));
        Assert.Contains("different units", flag, StringComparison.Ordinal);

        // The members are still written where they were drawn. Moving them would hide the fault
        // and invent a position nobody drew.
        Assert.Contains("nothing was moved", flag, StringComparison.Ordinal);
    }

    /// <summary>The warnings a one-wall plan produces with its geometry pushed <paramref name="at"/> from origin.</summary>
    private static IReadOnlyList<string> Warn(double at)
    {
        var geometry = new PlanGeometrySet();
        geometry.Walls.Add(new WallAxis(
            new DxfPoint(at, at), new DxfPoint(at + 120, at), 8, "JBP_V-WALL"));

        return DxfToEtabsService.FarFromOriginWarnings(new[] { geometry }, (0, 0));
    }
}
