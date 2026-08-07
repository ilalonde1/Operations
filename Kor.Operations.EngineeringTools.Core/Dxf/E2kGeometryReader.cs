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

        // One object can be assigned to several storeys, each on its own line. Keeping only the
        // last would hide every other storey it stands on.
        var storyOf = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string header in new[] { "AREA ASSIGNS", "LINE ASSIGNS" })
            foreach (string raw in doc.LinesOf(header))
            {
                var m = AssignRegex().Match(raw.Trim());
                if (!m.Success) continue;

                string objectName = m.Groups[2].Value;
                if (!storyOf.TryGetValue(objectName, out var stories)) storyOf[objectName] = stories = new List<string>();
                if (!stories.Contains(m.Groups[3].Value, StringComparer.OrdinalIgnoreCase))
                    stories.Add(m.Groups[3].Value);
            }

        IReadOnlyList<string> StoriesFor(string objectName)
            => storyOf.TryGetValue(objectName, out var s) ? s : new List<string> { string.Empty };

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
            foreach (string story in StoriesFor(name))
                geometry.Walls.Add(new E2kWall(name, story, distinct[0], distinct[1]));
        }

        foreach (string raw in doc.LinesOf("LINE CONNECTIVITIES"))
        {
            var m = LineRegex().Match(raw.Trim());
            if (!m.Success || !m.Groups[2].Value.Equals("COLUMN", StringComparison.OrdinalIgnoreCase)) continue;

            string name = m.Groups[1].Value;
            if (!points.TryGetValue(m.Groups[3].Value, out var at)) continue;
            foreach (string story in StoriesFor(name))
                geometry.Columns.Add(new E2kColumn(name, story, at));
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
    (double MinX, double MaxX, double MinY, double MaxY) CandidateExtents)
{
    /// <summary>Nearest generated column to each reference column, in inches.</summary>
    public double MedianColumnDistance { get; init; } = double.NaN;

    public int ColumnsWithin6in { get; init; }
}

/// <summary>
/// Measures how closely one model's geometry reproduces another's on a given storey.
/// Used to prove that geometry built from drawings lands where the imported model
/// already says the building is, rather than trusting a visual check.
/// </summary>
public sealed record AlignmentResult(double OffsetX, double OffsetY, int Inliers, int ReferencePoints, double MedianResidual);

/// <summary>
/// Finds the translation that best places one model's geometry onto another's.
///
/// Every pairing of a reference column with a generated one implies a translation; the
/// true one is implied by many pairs at once, so the offsets are quantised and voted on
/// and the most supported wins. Anchoring on columns rather than bounding boxes means a
/// drawing that covers part of a site still lands correctly.
/// </summary>
public static class E2kGeometryAligner
{
    public static AlignmentResult Solve(
        E2kModelGeometry reference, E2kModelGeometry candidate,
        string? candidatePrefix = null, double bucketInches = 2.0, double inlierInches = 6.0)
    {
        var refPoints = reference.Columns.Select(c => c.At).ToList();
        var candPoints = candidate.Columns
            .Where(c => candidatePrefix is null || c.Name.StartsWith(candidatePrefix, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.At)
            .ToList();

        if (refPoints.Count == 0 || candPoints.Count == 0)
            return new AlignmentResult(0, 0, 0, refPoints.Count, double.NaN);

        var votes = new Dictionary<(long, long), int>();
        foreach (var r in refPoints)
            foreach (var c in candPoints)
            {
                var key = ((long)Math.Round((r.X - c.X) / bucketInches), (long)Math.Round((r.Y - c.Y) / bucketInches));
                votes[key] = votes.GetValueOrDefault(key) + 1;
            }

        var best = votes.OrderByDescending(v => v.Value).First();
        double offsetX = best.Key.Item1 * bucketInches;
        double offsetY = best.Key.Item2 * bucketInches;

        var residuals = new List<double>();
        int inliers = 0;
        foreach (var r in refPoints)
        {
            double closest = double.MaxValue;
            foreach (var c in candPoints)
            {
                double dx = r.X - (c.X + offsetX), dy = r.Y - (c.Y + offsetY);
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d < closest) closest = d;
            }
            residuals.Add(closest);
            if (closest <= inlierInches) inliers++;
        }

        residuals.Sort();
        return new AlignmentResult(offsetX, offsetY, inliers, refPoints.Count, residuals[residuals.Count / 2]);
    }
}

public static class E2kGeometryComparer
{
    /// <param name="candidatePrefix">
    /// Only objects whose name starts with this are treated as the candidate's own. The
    /// generated model is the reference with geometry added, so without this the reference's
    /// objects would be compared against themselves and always agree perfectly.
    /// </param>
    public static GeometryAgreement Compare(
        E2kModelGeometry reference, E2kModelGeometry candidate, string story, string? candidatePrefix = null)
    {
        var refWalls = reference.Walls.Where(w => Same(w.Story, story)).ToList();
        var candWalls = candidate.Walls
            .Where(w => Same(w.Story, story))
            .Where(w => candidatePrefix is null || w.Name.StartsWith(candidatePrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

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

        var refColumns = reference.Columns.Where(c => Same(c.Story, story)).ToList();
        var candColumns = candidate.Columns
            .Where(c => Same(c.Story, story))
            .Where(c => candidatePrefix is null || c.Name.StartsWith(candidatePrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var columnDistances = new List<double>();
        foreach (var r in refColumns)
        {
            double best = double.MaxValue;
            foreach (var c in candColumns)
            {
                double d = r.At.DistanceTo(c.At);
                if (d < best) best = d;
            }
            if (best < double.MaxValue) columnDistances.Add(best);
        }
        columnDistances.Sort();

        return new GeometryAgreement(
            story,
            refWalls.Count, candWalls.Count,
            refColumns.Count, candColumns.Count,
            median, max,
            distances.Count(d => d <= 12), distances.Count(d => d <= 36),
            Extents(refWalls), Extents(candWalls))
        {
            MedianColumnDistance = columnDistances.Count == 0 ? double.NaN : columnDistances[columnDistances.Count / 2],
            ColumnsWithin6in = columnDistances.Count(d => d <= 6),
        };
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
