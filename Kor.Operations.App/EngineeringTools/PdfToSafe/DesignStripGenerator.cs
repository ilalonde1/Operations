#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// A single design strip defined by its centreline and half-width (all in mm,
    /// model coordinates - i.e. already centroid-shifted).
    /// </summary>
    internal sealed record DesignStrip(
        string Name,
        bool IsAlongX,
        double X1,
        double Y1,
        double X2,
        double Y2,
        double HalfWidth);

    /// <summary>
    /// Generates a uniform design strip grid from slab polygon extents.
    /// Strips tile the slab bounding box with no gaps and no overlaps:
    /// half-width = spacing / 2, one half-width buffer outside the extents.
    /// </summary>
    internal static class DesignStripGenerator
    {
        /// <param name="slabPolygons">
        /// Centroid-shifted slab polygons in model mm coordinates.
        /// </param>
        /// <param name="spacingMm">Centre-to-centre strip spacing (mm).</param>
        /// <param name="stripAAlongX">
        /// When true, SA### strips have horizontal centrelines (direction along X-axis)
        /// and SB### strips have vertical centrelines (direction along Y-axis).
        /// Reversed when false.
        /// </param>
        public static List<DesignStrip> Generate(
            IEnumerable<IEnumerable<(double X, double Y)>> slabPolygons,
            double spacingMm,
            bool stripAAlongX)
        {
            if (spacingMm <= 0)
                return new List<DesignStrip>();

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;

            foreach (var poly in slabPolygons)
                foreach (var (x, y) in poly)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }

            if (minX == double.MaxValue || maxX <= minX || maxY <= minY)
                return new List<DesignStrip>();

            double hw = spacingMm / 2.0;
            var strips = new List<DesignStrip>();

            void AddHorizontal(string prefix)
            {
                int count = (int)System.Math.Ceiling((maxY + hw - (minY - hw)) / spacingMm) + 1;
                for (int i = 0; i < count; i++)
                {
                    double y = minY - hw + i * spacingMm;
                    if (y > maxY + hw + 1e-6) break;
                    strips.Add(new DesignStrip($"{prefix}{(i + 1):D3}",
                        IsAlongX: true,
                        X1: minX - hw, Y1: y,
                        X2: maxX + hw, Y2: y,
                        HalfWidth: hw));
                }
            }

            void AddVertical(string prefix)
            {
                int count = (int)System.Math.Ceiling((maxX + hw - (minX - hw)) / spacingMm) + 1;
                for (int i = 0; i < count; i++)
                {
                    double x = minX - hw + i * spacingMm;
                    if (x > maxX + hw + 1e-6) break;
                    strips.Add(new DesignStrip($"{prefix}{(i + 1):D3}",
                        IsAlongX: false,
                        X1: x, Y1: minY - hw,
                        X2: x, Y2: maxY + hw,
                        HalfWidth: hw));
                }
            }

            if (stripAAlongX)
            {
                AddHorizontal("SA");
                AddVertical("SB");
            }
            else
            {
                AddVertical("SA");
                AddHorizontal("SB");
            }

            return strips;
        }
    }
}
