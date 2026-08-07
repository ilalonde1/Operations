using System.Globalization;

namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// A deliberately small DXF reader: it pulls LINE, ARC, LWPOLYLINE and POLYLINE
/// entities out of the ENTITIES section and returns them as straight segments.
///
/// This is not a general DXF library. It handles what Revit's "export to CAD"
/// actually emits for a structural plan, and ignores everything else (text,
/// hatches, dimensions, blocks), because only the geometry layers matter here.
/// </summary>
public static class DxfPlanReader
{
    /// <summary>Chord tolerance for turning an arc into segments, in drawing units.</summary>
    public const double ArcChordTolerance = 0.25;

    public static IReadOnlyList<DxfSegment> ReadSegments(string path)
        => ReadSegments(File.ReadLines(path));

    public static IReadOnlyList<DxfSegment> ReadSegments(IEnumerable<string> rawLines)
    {
        var lines = rawLines as IList<string> ?? rawLines.ToList();
        var segments = new List<DxfSegment>();

        int i = 0;
        bool inEntities = false;

        while (i < lines.Count - 1)
        {
            string code = lines[i].Trim();
            string value = lines[i + 1].Trim();

            if (code == "0")
            {
                if (value == "SECTION")
                {
                    // Look ahead for the "2 / <name>" pair that names the section.
                    for (int j = i + 2; j < Math.Min(i + 8, lines.Count - 1); j += 2)
                    {
                        if (lines[j].Trim() == "2")
                        {
                            inEntities = lines[j + 1].Trim() == "ENTITIES";
                            break;
                        }
                    }
                }
                else if (value == "ENDSEC")
                {
                    inEntities = false;
                }
                else if (inEntities)
                {
                    switch (value)
                    {
                        case "LINE":
                            i = ReadLine(lines, i + 2, segments);
                            continue;
                        case "ARC":
                            i = ReadArc(lines, i + 2, segments);
                            continue;
                        case "LWPOLYLINE":
                            i = ReadLwPolyline(lines, i + 2, segments);
                            continue;
                        case "POLYLINE":
                            i = ReadPolyline(lines, i + 2, segments);
                            continue;
                    }
                }
            }

            i += 2;
        }

        return segments;
    }

    private static bool TryNumber(string s, out double value)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>Reads group-code pairs until the next entity marker, returning the index of that marker.</summary>
    private static int ScanEntity(IList<string> lines, int start, Action<string, string> onPair)
    {
        int i = start;
        while (i < lines.Count - 1)
        {
            string code = lines[i].Trim();
            if (code == "0") return i;
            onPair(code, lines[i + 1].Trim());
            i += 2;
        }
        return i;
    }

    private static int ReadLine(IList<string> lines, int start, List<DxfSegment> into)
    {
        string layer = string.Empty;
        double x1 = 0, y1 = 0, x2 = 0, y2 = 0;
        bool hasStart = false, hasEnd = false;

        int next = ScanEntity(lines, start, (code, value) =>
        {
            switch (code)
            {
                case "8": layer = value; break;
                case "10": if (TryNumber(value, out x1)) hasStart = true; break;
                case "20": TryNumber(value, out y1); break;
                case "11": if (TryNumber(value, out x2)) hasEnd = true; break;
                case "21": TryNumber(value, out y2); break;
            }
        });

        if (hasStart && hasEnd)
        {
            var a = new DxfPoint(x1, y1);
            var b = new DxfPoint(x2, y2);
            if (a.DistanceTo(b) > 1e-9) into.Add(new DxfSegment(layer, a, b));
        }
        return next;
    }

    private static int ReadArc(IList<string> lines, int start, List<DxfSegment> into)
    {
        string layer = string.Empty;
        double cx = 0, cy = 0, radius = 0, startAngle = 0, endAngle = 0;

        int next = ScanEntity(lines, start, (code, value) =>
        {
            switch (code)
            {
                case "8": layer = value; break;
                case "10": TryNumber(value, out cx); break;
                case "20": TryNumber(value, out cy); break;
                case "40": TryNumber(value, out radius); break;
                case "50": TryNumber(value, out startAngle); break;
                case "51": TryNumber(value, out endAngle); break;
            }
        });

        if (radius > 1e-9)
        {
            foreach (var seg in TessellateArc(layer, cx, cy, radius, startAngle, endAngle))
                into.Add(seg);
        }
        return next;
    }

    public static IEnumerable<DxfSegment> TessellateArc(
        string layer, double cx, double cy, double radius, double startDeg, double endDeg)
    {
        double sweep = endDeg - startDeg;
        while (sweep <= 0) sweep += 360.0;

        // Enough segments that the chord never deviates from the arc by more than the tolerance.
        double maxStep = radius <= ArcChordTolerance
            ? 90.0
            : 2.0 * Math.Acos(1.0 - ArcChordTolerance / radius) * 180.0 / Math.PI;
        int count = Math.Max(2, (int)Math.Ceiling(sweep / Math.Max(maxStep, 1e-6)));

        DxfPoint At(double deg)
        {
            double r = deg * Math.PI / 180.0;
            return new DxfPoint(cx + radius * Math.Cos(r), cy + radius * Math.Sin(r));
        }

        var previous = At(startDeg);
        for (int k = 1; k <= count; k++)
        {
            var current = At(startDeg + sweep * k / count);
            if (previous.DistanceTo(current) > 1e-9)
                yield return new DxfSegment(layer, previous, current) { FromCurve = true };
            previous = current;
        }
    }

    private static int ReadLwPolyline(IList<string> lines, int start, List<DxfSegment> into)
    {
        string layer = string.Empty;
        bool closed = false;
        var points = new List<DxfPoint>();
        double pendingX = 0;
        bool hasPendingX = false;

        int next = ScanEntity(lines, start, (code, value) =>
        {
            switch (code)
            {
                case "8": layer = value; break;
                case "70": if (int.TryParse(value, out int flags)) closed = (flags & 1) == 1; break;
                case "10":
                    if (TryNumber(value, out double x)) { pendingX = x; hasPendingX = true; }
                    break;
                case "20":
                    if (hasPendingX && TryNumber(value, out double y))
                    {
                        points.Add(new DxfPoint(pendingX, y));
                        hasPendingX = false;
                    }
                    break;
            }
        });

        EmitPolyline(layer, points, closed, into);
        return next;
    }

    private static int ReadPolyline(IList<string> lines, int start, List<DxfSegment> into)
    {
        string layer = string.Empty;
        bool closed = false;
        var points = new List<DxfPoint>();

        int i = start;
        // Header pairs of the POLYLINE itself.
        i = ScanEntity(lines, i, (code, value) =>
        {
            switch (code)
            {
                case "8": layer = value; break;
                case "70": if (int.TryParse(value, out int flags)) closed = (flags & 1) == 1; break;
            }
        });

        // Then a run of VERTEX entities, terminated by SEQEND.
        while (i < lines.Count - 1 && lines[i].Trim() == "0")
        {
            string type = lines[i + 1].Trim();
            if (type == "VERTEX")
            {
                double vx = 0, vy = 0;
                bool hasX = false, hasY = false;
                i = ScanEntity(lines, i + 2, (code, value) =>
                {
                    switch (code)
                    {
                        case "10": if (TryNumber(value, out vx)) hasX = true; break;
                        case "20": if (TryNumber(value, out vy)) hasY = true; break;
                    }
                });
                if (hasX && hasY) points.Add(new DxfPoint(vx, vy));
            }
            else if (type == "SEQEND")
            {
                i = ScanEntity(lines, i + 2, static (_, _) => { });
                break;
            }
            else
            {
                break;
            }
        }

        EmitPolyline(layer, points, closed, into);
        return i;
    }

    private static void EmitPolyline(string layer, List<DxfPoint> points, bool closed, List<DxfSegment> into)
    {
        for (int k = 0; k < points.Count - 1; k++)
            if (points[k].DistanceTo(points[k + 1]) > 1e-9)
                into.Add(new DxfSegment(layer, points[k], points[k + 1]));

        if (closed && points.Count > 2 && points[^1].DistanceTo(points[0]) > 1e-9)
            into.Add(new DxfSegment(layer, points[^1], points[0]));
    }
}
