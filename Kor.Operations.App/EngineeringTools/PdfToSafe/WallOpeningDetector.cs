#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// Detects rectangular slab openings formed by closed loops of wall
    /// segments (elevator shafts, stair cores, mech shafts). KOR engineers
    /// draw wall outlines in burgundy around shaft perimeters; without
    /// emitting matching FLOOR OBJECT OPENINGS rows the SAFE model treats
    /// the slab as continuous through the shaft and produces meaningless
    /// deflection / reinforcement results inside the core.
    ///
    /// Walls reach this stage as 2-point centerlines (one per wall polygon
    /// reduced via <see cref="PolygonProcessor.ReducePolygonToWallCenterline"/>).
    /// Their endpoints do NOT exactly meet at shaft corners because each
    /// centerline runs along the long axis through the midpoint of the short
    /// axis — adjacent perpendicular walls' centerlines are offset by half of
    /// each wall's thickness at the corner.
    ///
    /// Strategy: project centerlines until they intersect. Pair 2 horizontal
    /// + 2 vertical wall bounding boxes whose ranges bracket each other, and
    /// take the rectangle formed by the centerline intersections as the
    /// opening polygon. SAFE expects openings cut at the wall centerline (the
    /// wall is modelled as a beam along that centerline), so the centerline
    /// rectangle is the correct opening geometry.
    ///
    /// Non-rectangular shafts (L, T, U-shape, three-sided open cores) are not
    /// detected; engineers can add them in SAFE post-import.
    /// </summary>
    internal static class WallOpeningDetector
    {
        /// <summary>
        /// Returns (parentSlabIndex, openingPolygon) pairs. Openings are
        /// 4-point CCW rectangles whose centroid lies inside the parent slab.
        /// </summary>
        /// <param name="lines">All polylines from the prepared geometry.</param>
        /// <param name="wallSectionHints">Parallel to <paramref name="lines"/>;
        /// non-null entries mark wall-classified lines (carry W×D hint from
        /// the reclassifier). Beam lines have null entries and are ignored.</param>
        /// <param name="slabs">Slab polygons (CCW). Used to locate the
        /// containing slab and to reject openings that exceed the slab area.</param>
        /// <param name="colinearToleranceMm">Slack (mm) for matching wall
        /// bounding-box edges to the rectangle. 200 mm covers typical
        /// half-wall-thickness offsets at corners (KOR shafts: 200–300 mm wall).</param>
        /// <param name="minOpeningAreaMm2">Reject openings below this area
        /// (filters out pen-thickness artifacts). Default 1 m².</param>
        public static List<(int ParentSlabIndex, List<(double X, double Y)> Polygon)>
            DetectRectangularOpenings(
                IReadOnlyList<List<(double X, double Y)>> lines,
                IReadOnlyList<(double WidthMm, double DepthMm)?> wallSectionHints,
                IReadOnlyList<List<(double X, double Y)>> slabs,
                double colinearToleranceMm = 200.0,
                double minOpeningAreaMm2 = 1.0e6)
        {
            var result = new List<(int, List<(double X, double Y)>)>();
            if (lines is null || lines.Count == 0 || slabs is null || slabs.Count == 0)
                return result;

            var walls = new List<(int Index, double MinX, double MinY, double MaxX, double MaxY, bool IsHoriz)>();
            for (int i = 0; i < lines.Count; i++)
            {
                if (wallSectionHints is null
                    || i >= wallSectionHints.Count
                    || !wallSectionHints[i].HasValue)
                    continue;
                var pts = lines[i];
                if (pts is null || pts.Count < 2) continue;
                double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
                double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
                bool isHoriz = (maxX - minX) >= (maxY - minY);
                walls.Add((i, minX, minY, maxX, maxY, isHoriz));
            }

            var hWalls = walls.Where(w => w.IsHoriz).ToList();
            var vWalls = walls.Where(w => !w.IsHoriz).ToList();
            if (hWalls.Count < 2 || vWalls.Count < 2) return result;

            var seenRects = new HashSet<string>();

            for (int h1i = 0; h1i < hWalls.Count; h1i++)
            for (int h2i = h1i + 1; h2i < hWalls.Count; h2i++)
            {
                var h1 = hWalls[h1i];
                var h2 = hWalls[h2i];
                double y1c = 0.5 * (h1.MinY + h1.MaxY);
                double y2c = 0.5 * (h2.MinY + h2.MaxY);
                if (Math.Abs(y1c - y2c) < colinearToleranceMm) continue;
                double yLow = Math.Min(y1c, y2c), yHigh = Math.Max(y1c, y2c);

                double overlapX = Math.Min(h1.MaxX, h2.MaxX) - Math.Max(h1.MinX, h2.MinX);
                if (overlapX < colinearToleranceMm) continue;

                for (int v1i = 0; v1i < vWalls.Count; v1i++)
                for (int v2i = v1i + 1; v2i < vWalls.Count; v2i++)
                {
                    var v1 = vWalls[v1i];
                    var v2 = vWalls[v2i];
                    double x1c = 0.5 * (v1.MinX + v1.MaxX);
                    double x2c = 0.5 * (v2.MinX + v2.MaxX);
                    if (Math.Abs(x1c - x2c) < colinearToleranceMm) continue;
                    double xL = Math.Min(x1c, x2c), xR = Math.Max(x1c, x2c);

                    // V walls' Y extents must bracket the H centerlines.
                    double vMinY = Math.Min(v1.MinY, v2.MinY);
                    double vMaxY = Math.Max(v1.MaxY, v2.MaxY);
                    if (vMinY > yLow + colinearToleranceMm) continue;
                    if (vMaxY < yHigh - colinearToleranceMm) continue;

                    // H walls' X extents must bracket the V centerlines.
                    if (h1.MinX > xL + colinearToleranceMm) continue;
                    if (h2.MinX > xL + colinearToleranceMm) continue;
                    if (h1.MaxX < xR - colinearToleranceMm) continue;
                    if (h2.MaxX < xR - colinearToleranceMm) continue;

                    // V centerline X must fall within both H walls' spans.
                    if (x1c < h1.MinX - colinearToleranceMm || x1c > h1.MaxX + colinearToleranceMm) continue;
                    if (x2c < h2.MinX - colinearToleranceMm || x2c > h2.MaxX + colinearToleranceMm) continue;
                    if (x1c < h2.MinX - colinearToleranceMm || x1c > h2.MaxX + colinearToleranceMm) continue;
                    if (x2c < h1.MinX - colinearToleranceMm || x2c > h1.MaxX + colinearToleranceMm) continue;

                    // H centerline Y must fall within both V walls' spans.
                    if (y1c < v1.MinY - colinearToleranceMm || y1c > v1.MaxY + colinearToleranceMm) continue;
                    if (y2c < v2.MinY - colinearToleranceMm || y2c > v2.MaxY + colinearToleranceMm) continue;
                    if (y1c < v2.MinY - colinearToleranceMm || y1c > v2.MaxY + colinearToleranceMm) continue;
                    if (y2c < v1.MinY - colinearToleranceMm || y2c > v1.MaxY + colinearToleranceMm) continue;

                    double area = (xR - xL) * (yHigh - yLow);
                    if (area < minOpeningAreaMm2) continue;

                    double cx = 0.5 * (xL + xR);
                    double cy = 0.5 * (yLow + yHigh);

                    int parent = -1;
                    double parentArea = double.PositiveInfinity;
                    for (int si = 0; si < slabs.Count; si++)
                    {
                        if (slabs[si].Count < 3) continue;
                        if (!PolygonProcessor.PointInPolygon((cx, cy), slabs[si])) continue;
                        double sArea = PolygonProcessor.PolygonAreaMm2(slabs[si]);
                        if (sArea <= area * 1.1) continue;
                        if (sArea < parentArea) { parentArea = sArea; parent = si; }
                    }
                    if (parent < 0) continue;

                    string key = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "{0:0}_{1:0}_{2:0}_{3:0}_p{4}",
                        Math.Round(xL / 10), Math.Round(yLow / 10),
                        Math.Round(xR / 10), Math.Round(yHigh / 10), parent);
                    if (!seenRects.Add(key)) continue;

                    var poly = new List<(double X, double Y)>
                    {
                        (xL, yLow), (xR, yLow), (xR, yHigh), (xL, yHigh)
                    };
                    result.Add((parent, poly));
                }
            }
            return result;
        }
    }
}
