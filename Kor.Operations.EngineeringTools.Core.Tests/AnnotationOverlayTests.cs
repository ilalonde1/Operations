using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// Carrying the drawing's words onto geometry exported without them.
///
/// The numbers here are the ones measured on 31168: the bridge export sits 90 degrees round
/// from the set this tool builds from, and one transform fits every sheet to 0.3 in.
/// </summary>
public class AnnotationOverlayTests
{
    /// <summary>The transform measured on 31168, in inches.</summary>
    private const double OffsetX = 29691.4;
    private const double OffsetY = -36405.7;

    /// <summary>
    /// Write these columns the way the ANNOTATED export writes them, so that Solve has to
    /// recover a +90 turn to bring them back. That is minus ninety, not plus: the transform
    /// under test runs annotated -> geometry, and a fixture that turns the same way as the
    /// answer proves nothing.
    /// </summary>
    private static List<DxfPoint> AsAnnotated(IEnumerable<DxfPoint> ps, double dx, double dy)
        => ps.Select(p => new DxfPoint(p.Y - dy, dx - p.X)).ToList();

    [Fact]
    public void FindsTheRotationBetweenTwoExportsOfOneBuilding()
    {
        // ASYMMETRIC ON PURPOSE. A square grid of columns fits a quarter turn and a
        // three-quarter turn equally well, so the answer is ambiguous and the test proves
        // nothing -- which is exactly what it did until this shape replaced it. A real floor
        // plate is L-shaped or worse, and only one turn brings it back.
        var geometry = new List<DxfPoint>
        {
            new(0, 0), new(240, 0), new(480, 0), new(720, 0),
            new(0, 300), new(240, 300),
            new(0, 600), new(240, 600),
            new(0, 900),
        };

        // The same columns as the annotated export writes them: turned, and a long way away.
        var annotated = AsAnnotated(geometry, OffsetX, OffsetY);

        var frame = AnnotationOverlay.Solve(annotated, geometry);

        Assert.NotNull(frame);
        Assert.Equal(90.0, frame!.Value.RotationDegrees);
        Assert.Equal(OffsetX, frame.Value.OffsetX, 1);
        Assert.Equal(OffsetY, frame.Value.OffsetY, 1);
    }

    [Fact]
    public void CarriesATagOntoTheGeometryItDescribes()
    {
        var geometry = new List<DxfPoint>
        {
            new(0, 0), new(240, 0), new(0, 300), new(240, 300), new(480, 600),
        };
        var annotated = AsAnnotated(geometry, OffsetX, OffsetY);

        var frame = AnnotationOverlay.Solve(annotated, geometry);
        Assert.NotNull(frame);

        // A 14in slab call-out sitting at the middle of the bay, in the annotated frame.
        var middle = new DxfPoint(120, 150);
        var asWritten = AsAnnotated(new[] { middle }, OffsetX, OffsetY)[0];

        var carried = AnnotationOverlay.Carry(
            new[] { new DxfPositionedTag("14\" SLAB", asWritten, "A-FLOR-IDEN", "14\" SLAB") },
            frame!.Value);

        var landed = Assert.Single(carried).Point;
        Assert.Equal(middle.X, landed.X, 1);
        Assert.Equal(middle.Y, landed.Y, 1);
        Assert.Equal("14\" SLAB", carried[0].Text);
    }

    /// <summary>
    /// The two exports do not carry identical column counts -- LEVEL P2 has 445 against 455 --
    /// and the odd ones out must not break the fit. This is why the score is a median.
    /// </summary>
    [Fact]
    public void ToleratesColumnsPresentInOnlyOneExport()
    {
        // Asymmetric, for the same reason as above: a long wing plus a short one.
        var geometry = new List<DxfPoint>();
        for (int i = 0; i < 30; i++) geometry.Add(new DxfPoint(i * 120, 0));
        for (int i = 1; i < 10; i++) geometry.Add(new DxfPoint(0, i * 300));

        var annotated = AsAnnotated(geometry, OffsetX, OffsetY);
        // LEVEL P2 really does differ by ten columns out of 455 -- extras standing WITH
        // the building, not off in space.
        annotated.Add(new DxfPoint(annotated[0].X + 60, annotated[0].Y + 60));
        annotated.Add(new DxfPoint(annotated[1].X - 60, annotated[1].Y - 60));

        var frame = AnnotationOverlay.Solve(annotated, geometry);

        Assert.NotNull(frame);
        Assert.Equal(90.0, frame!.Value.RotationDegrees);

        // The offset comes from centroids, so a few unmatched columns pull it a little. Within
        // a few feet is what matters: a call-out lands inside the right plate either way, and
        // the median residual is what proves the fit rather than the offset itself.
        Assert.True(Math.Abs(frame.Value.OffsetX - OffsetX) < 120,
            $"offset drifted {Math.Abs(frame.Value.OffsetX - OffsetX):0} in with two extra columns");
    }

    /// <summary>
    /// FAILS CLOSED. A wrong frame would put one storey's thickness onto another storey's slab
    /// without saying so, which is worse than carrying no tags at all.
    /// </summary>
    [Fact]
    public void RefusesTwoCloudsThatAreNotTheSameBuilding()
    {
        var geometry = new List<DxfPoint>
        {
            new(0, 0), new(240, 0), new(0, 300), new(240, 300),
        };
        var unrelated = new List<DxfPoint>
        {
            new(0, 0), new(1700, 60), new(90, 2400), new(3300, 3300),
        };

        Assert.Null(AnnotationOverlay.Solve(unrelated, geometry));
    }

    [Fact]
    public void RefusesWhenEitherExportHasNoColumns()
    {
        var some = new List<DxfPoint> { new(0, 0), new(240, 0) };
        Assert.Null(AnnotationOverlay.Solve(new List<DxfPoint>(), some));
        Assert.Null(AnnotationOverlay.Solve(some, new List<DxfPoint>()));
    }
}
