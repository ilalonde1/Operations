using System.Globalization;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.Dxf;

public sealed record E2kWall(string Name, string Story, DxfPoint A, DxfPoint B)
{
    public DxfPoint Midpoint => new((A.X + B.X) / 2.0, (A.Y + B.Y) / 2.0);
    public double Length => A.DistanceTo(B);
}

public sealed record E2kColumn(string Name, string Story, DxfPoint At);

public sealed class E2kModelGeometry
{
    public List<E2kWall> Walls { get; } = new();
    public List<E2kColumn> Columns { get; } = new();
}

/// <summary>
/// Reads back the geometry of an .e2k — the inverse of the composer, so a generated
/// model can be measured against one that is trusted instead of eyeballed.
/// </summary>
public static partial class E2kGeometryReader
{
    [GeneratedRegex(@"^POINT\s+""([^""]+)""\s+(\S+)\s+(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex PointRegex();

    [GeneratedRegex(@"^AREA\s+""([^""]+)""\s+(PANEL|FLOOR)\b(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex AreaRegex();

    [GeneratedRegex(@"^LINE\s+""([^""]+)""\s+(COLUMN|BEAM|BRACE)\s+""([^""]+)""\s+""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex LineRegex();

    [GeneratedRegex(@"^(AREAASSIGN|LINEASSIGN)\s+""([^""]+)""\s+""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex AssignRegex();

    [GeneratedRegex(@"""([^""]+)""")]
    private static partial Regex QuotedRegex();

    public static E2kModelGeometry Read(E2kDocument doc)
    {
        var points = new Dictionary<string, DxfPoint>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in doc.LinesOf("POINT COORDINATES"))
        {
            var m = PointRegex().Match(raw.Trim());
            if (!m.Success) continue;
            if (TryNum(m.Groups[2].Value, out double x) && TryNum(m.Groups[3].Value, out double y))
                points[m.Groups[1].Value] = new DxfPoint(x, y);
        }

        var storyOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string header in new[] { "AREA ASSIGNS", "LINE ASSIGNS" })
            foreach (string raw in doc.LinesOf(header))
            {
                var m = AssignRegex().Match(raw.Trim());
                if (m.Success) storyOf[m.Groups[2].Value] = m.Groups[3].Value;
            }

        var geometry = new E2kModelGeometry();

        foreach (string raw in doc.LinesOf("AREA CONNECTIVITIES"))
        {
            var m = AreaRegex().Match(raw.Trim());
            if (!m.Success || !m.Groups[2].Value.Equals("PANEL", StringComparison.OrdinalIgnoreCase)) continue;

            string name = m.Groups[1].Value;
            var corners = QuotedRegex().Matches(m.Groups[3].Value)
                .Select(q => q.Groups[1].Value)
                .Where(points.ContainsKey)
                .Select(p => points[p])
                .ToList();

            // A wall panel is a vertical rectangle: in plan it collapses to two corners.
            var distinct = new List<DxfPoint>();
            foreach (var c in corners)
                if (!distinct.Any(d => d.DistanceTo(c) < 0.5))
                    distinct.Add(c);

            if (distinct.Count < 2) continue;
            geometry.Walls.Add(new E2kWall(name, storyOf.GetValueOrDefault(name, string.Empty), distinct[0], distinct[1]));
        }

        foreach (string raw in doc.LinesOf("LINE CONNECTIVITIES"))
        {
            var m = LineRegex().Match(raw.Trim());
            if (!m.Success || !m.Groups[2].Value.Equals("COLUMN", StringComparison.OrdinalIgnoreCase)) continue;

            string name = m.Groups[1].Value;
            if (!points.TryGetValue(m.Groups[3].Value, out var at)) continue;
            geometry.Columns.Add(new E2kColumn(name, storyOf.GetValueOrDefault(name, string.Empty), at));
        }

        return geometry;
    }

    private static bool TryNum(string s, out double value)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}

public sealed record GeometryAgreement(
    string Story,
    int ReferenceWalls, int CandidateWalls,
    int ReferenceColumns, int CandidateColumns,
    double MedianWallDistance, double MaxWallDistance,
    int WallsWithin12in, int WallsWithin36in,
    (double MinX, double MaxX, double MinY, double MaxY) ReferenceExtents,
    (double MinX, double MaxX, double MinY, double MaxY) CandidateExtents);

/// <summary>
/// Measures how closely one model's geometry reproduces another's on a given storey.
/// Used to prove that geometry built from drawings lands where the imported model
/// already says the building is, rather than trusting a visual check.
/// </summary>
public static class E2kGeometryComparer
{
    public static GeometryAgreement Compare(E2kModelGeometry reference, E2kModelGeometry candidate, string story)
    {
        var refWalls = reference.Walls.Where(w => Same(w.Story, story)).ToList();
        var candWalls = candidate.Walls.Where(w => Same(w.Story, story)).ToList();

        var distances = new List<double>();
        foreach (var r in refWalls)
        {
            double best = double.MaxValue;
            foreach (var c in candWalls)
            {
                double d = r.Midpoint.DistanceTo(c.Midpoint);
                if (d < best) best = d;
            }
            if (best < double.MaxValue) distances.Add(best);
        }

        distances.Sort();
        double median = distances.Count == 0 ? double.NaN : distances[distances.Count / 2];
        double max = distances.Count == 0 ? double.NaN : distances[^1];

        return new GeometryAgreement(
            story,
            refWalls.Count, candWalls.Count,
            reference.Columns.Count(c => Same(c.Story, story)),
            candidate.Columns.Count(c => Same(c.Story, story)),
            median, max,
            distances.Count(d => d <= 12), distances.Count(d => d <= 36),
            Extents(refWalls), Extents(candWalls));
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static (double, double, double, double) Extents(IReadOnlyList<E2kWall> walls)
    {
        if (walls.Count == 0) return (0, 0, 0, 0);
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        foreach (var w in walls)
            foreach (var p in new[] { w.A, w.B })
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }
        return (minX, maxX, minY, maxY);
    }
}
