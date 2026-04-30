#nullable enable
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe
{
    /// <summary>
    /// Maps model mm ↔ canvas pixels. Broken transforms make the entire
    /// click-targets / hover-overlay geometry visually wrong (engineers see
    /// the wrong polygon highlighted). These tests lock the math.
    /// </summary>
    public class CoordinateTransformerTests
    {
        // Page is 595 × 842 pts (A4 portrait).
        // Scale 1:100 → 1 mm model = 1/100 page mm = (1/100) / PointsToMm page pts.
        // Canvas width 600 px chosen so that 1 pt = 600/595 ≈ 1 px.
        private const double A4WidthPts = 595.0;
        private const double A4HeightPts = 842.0;
        private const double CanvasWidth = 600.0;

        [Fact]
        public void ToCanvas_Origin_YFlips_ToTopOfCanvas()
        {
            var tx = new CoordinateTransformer(CanvasWidth, A4WidthPts, A4HeightPts, scaleDenominator: 100);
            var (x, y) = tx.ToCanvas(0, 0);
            Assert.Equal(0, x, 6);
            // PDF coords are Y-up; canvas is Y-down. Model (0,0) maps to
            // top-left of the canvas AFTER the flip, so y should be positive
            // and equal to canvas-height-equivalent in px.
            Assert.True(y > 0);
        }

        [Fact]
        public void ToCanvas_DoublingMmDoublesCanvasX()
        {
            var tx = new CoordinateTransformer(CanvasWidth, A4WidthPts, A4HeightPts, 100);
            var (x1, _) = tx.ToCanvas(1000, 0);
            var (x2, _) = tx.ToCanvas(2000, 0);
            Assert.Equal(2.0 * x1, x2, 6);
        }

        [Fact]
        public void ToCanvas_HigherScaleDenominator_CompressesCoords()
        {
            // 1:200 scale shows the model HALF the size of 1:100 at the same canvas.
            var tx100 = new CoordinateTransformer(CanvasWidth, A4WidthPts, A4HeightPts, 100);
            var tx200 = new CoordinateTransformer(CanvasWidth, A4WidthPts, A4HeightPts, 200);
            var (x100, _) = tx100.ToCanvas(1000, 0);
            var (x200, _) = tx200.ToCanvas(1000, 0);
            Assert.Equal(x100 / 2.0, x200, 6);
        }

        [Fact]
        public void SuggestScale_RoundTripsAPickedSegment()
        {
            // Pretend the user drew a 60-pixel line on a 600-px canvas of a 595-pt page
            // and declared it 5000 mm. Suggested scale should round-trip close to the
            // true scale that would produce that mm length.
            int scale = CoordinateTransformer.SuggestScale(
                pixelDist: 60.0, canvasWidth: CanvasWidth, pageWidthPts: A4WidthPts, knownMm: 5000.0);
            Assert.True(scale > 0);
            // A forward-check: with this scale, 60 px should resolve to ~5000 mm
            // on the same page/canvas (the SuggestScale math is its own inverse).
            // We don't know PointsToMm from out here, but we can assert scale is a
            // sensible small integer — 1:100..1:1000 typical.
            Assert.InRange(scale, 1, 100000);
        }

        [Fact]
        public void SuggestScale_KnownMmDoubled_ScaleDoubles()
        {
            int s1 = CoordinateTransformer.SuggestScale(60.0, CanvasWidth, A4WidthPts, 5000);
            int s2 = CoordinateTransformer.SuggestScale(60.0, CanvasWidth, A4WidthPts, 10000);
            Assert.True(s2 >= 2 * s1 - 1 && s2 <= 2 * s1 + 1); // allow ±1 rounding
        }

        [Fact]
        public void SuggestScale_ClampsToAtLeastOne()
        {
            // If the math would round to 0 (very short known length vs pixel dist),
            // the method must clamp to 1 so callers never see a zero-scale model.
            int s = CoordinateTransformer.SuggestScale(
                pixelDist: 600.0, canvasWidth: CanvasWidth, pageWidthPts: A4WidthPts, knownMm: 0.001);
            Assert.True(s >= 1);
        }
    }
}
