using System.Globalization;
using System.Text;

namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// A deliberately small DXF reader: it pulls LINE, ARC, LWPOLYLINE and POLYLINE
/// entities out of the ENTITIES section and returns them as straight segments,
/// and carries drawing text as positioned tags beside that geometry.
///
/// This is not a general DXF library. It handles what Revit's "export to CAD"
/// actually emits for a structural plan, and ignores everything else (hatches,
/// dimensions), because only geometry and annotation have model paths here.
/// </summary>
public static class DxfPlanReader
{
    private static readonly HashSet<string> SupportedEntityTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "LINE",
        "ARC",
        "LWPOLYLINE",
        "POLYLINE",
        "INSERT",
        "TEXT",
        "MTEXT",
        "ATTRIB",
    };

    /// <summary>
    /// Entity types that could be structure and cannot be read, as opposed to annotation nobody
    /// expects a model from.
    ///
    /// This list is what decides whether a thing is worth reporting, and it deliberately does NOT
    /// consult the layer name. The report used to name unreadable entities only on layers that had
    /// already matched a structural pattern, which meant the one drawing that most needed telling —
    /// a set whose layers this tool does not recognise — was the one it stayed quiet about. A hatch
    /// on S-CONC produced an empty model, no geometry, and a report that named neither the layer
    /// nor the hatch, because the gate that would have reported it was the same gate that had
    /// already rejected the layer.
    /// </summary>
    private static readonly HashSet<string> DrawableEntityTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "HATCH", "SOLID", "3DSOLID", "SPLINE", "CIRCLE", "ELLIPSE",
        "3DFACE", "REGION", "MESH", "POLYFACEMESH", "TRACE", "BODY", "SURFACE",
    };

    public sealed record UnsupportedEntity(string Layer, string EntityType, int Count);

    /// <summary>Chord tolerance for turning an arc into segments, in drawing units.</summary>
    public const double ArcChordTolerance = 0.25;

    public static IReadOnlyList<DxfSegment> ReadSegments(string path)
        => ReadSegments(File.ReadLines(path));

    public static IReadOnlyList<DxfPositionedTag> ReadPositionedTags(string path)
        => ReadPositionedTags(File.ReadLines(path));

    /// <summary>
    /// How long one drawing unit is, in inches, from the DXF's own <c>$INSUNITS</c> header.
    /// Null when the drawing does not say.
    ///
    /// Every threshold in this tool is a real length — a 48" wall, a 12" face, a 400 sq ft plate —
    /// so a drawing in millimetres or feet does not fail, it produces a building of entirely the
    /// wrong size and says nothing. All 90 sheets across the two jobs so far declare inches, which
    /// is exactly why this was never noticed.
    /// </summary>
    public static double? UnitInInches(string path) => UnitInInches(File.ReadLines(path));

    public static double? UnitInInches(IEnumerable<string> rawLines)
    {
        // The header sits before ENTITIES and can run to thousands of lines; stop at the first
        // entity rather than reading the whole drawing to find a number near the top.
        string? pending = null;
        bool inHeader = false;

        foreach (string raw in rawLines)
        {
            string line = raw.Trim();

            if (line.Equals("ENTITIES", StringComparison.OrdinalIgnoreCase)) break;
            if (line.Equals("HEADER", StringComparison.OrdinalIgnoreCase)) { inHeader = true; continue; }
            if (!inHeader) continue;

            if (line.Equals("$INSUNITS", StringComparison.OrdinalIgnoreCase)) { pending = line; continue; }
            if (pending is null) continue;

            // The value follows its group code, so skip the code and take the next number.
            if (line == "70") continue;
            if (!int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out int code)) { pending = null; continue; }

            return code switch
            {
                1 => 1.0,            // inches
                2 => 12.0,           // feet
                4 => 1.0 / 25.4,     // millimetres
                5 => 1.0 / 2.54,     // centimetres
                6 => 1000.0 / 25.4,  // metres
                _ => null,           // 0 is unitless; anything else is not a length this tool knows
            };
        }
        return null;
    }

    public static IReadOnlyList<DxfSegment> ReadSegments(IEnumerable<string> rawLines)
    {
        var lines = rawLines as IList<string> ?? rawLines.ToList();
        var segments = new List<DxfSegment>();

        // Drafting places repeated elements — a steel column, a round concrete column — as an
        // INSERT of a named block rather than as loose linework. Reading only the ENTITIES
        // section therefore misses them completely, and misses them SILENTLY: nothing was read,
        // so nothing was dropped, so no count moved and no flag fired. On 31138 that is 100
        // inserts on column layers and 75 columns absent from the model, whole floors at a time
        // — levels 5 and 6 lose all 22 each, the mech level and the roof all 15 each.
        var blocks = ReadBlocks(lines);

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
                        case "INSERT":
                            i = ReadInsert(lines, i + 2, blocks, segments);
                            continue;
                        case "TEXT":
                        case "MTEXT":
                        case "ATTRIB":
                            i = ScanEntity(lines, i + 2, static (_, _) => { });
                            continue;
                    }
                }
            }

            i += 2;
        }

        return segments;
    }

    public static IReadOnlyList<DxfPositionedTag> ReadPositionedTags(IEnumerable<string> rawLines)
    {
        var lines = rawLines as IList<string> ?? rawLines.ToList();
        var tags = new List<DxfPositionedTag>();

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
                        case "TEXT":
                        case "ATTRIB":
                            i = ReadTextTag(lines, i + 2, tags, stripMTextFormatting: false);
                            continue;
                        case "MTEXT":
                            i = ReadTextTag(lines, i + 2, tags, stripMTextFormatting: true);
                            continue;
                    }
                }
            }

            i += 2;
        }

        return tags;
    }

    public static IReadOnlyList<UnsupportedEntity> UnsupportedStructuralEntities(
        string path,
        PlanClassificationOptions options)
        => UnsupportedStructuralEntities(File.ReadLines(path), options);

    public static IReadOnlyList<UnsupportedEntity> UnsupportedStructuralEntities(
        IEnumerable<string> rawLines,
        PlanClassificationOptions options)
    {
        var lines = rawLines as IList<string> ?? rawLines.ToList();
        var counts = new Dictionary<(string Layer, string EntityType), int>();
        bool inEntities = false;

        for (int i = 0; i < lines.Count - 1; i += 2)
        {
            string code = lines[i].Trim();
            string value = lines[i + 1].Trim();

            if (code != "0") continue;

            if (value == "SECTION")
            {
                for (int j = i + 2; j < Math.Min(i + 8, lines.Count - 1); j += 2)
                {
                    if (lines[j].Trim() != "2") continue;
                    inEntities = lines[j + 1].Trim() == "ENTITIES";
                    break;
                }
                continue;
            }

            if (value == "ENDSEC") { inEntities = false; continue; }
            if (!inEntities || SupportedEntityTypes.Contains(value)) continue;
            if (!DrawableEntityTypes.Contains(value)) continue;

            string layer = EntityLayer(lines, i + 2);
            if (layer.Length == 0) continue;

            var key = (layer, value);
            counts[key] = counts.TryGetValue(key, out int already) ? already + 1 : 1;
        }

        return counts
            .Select(kv => new UnsupportedEntity(kv.Key.Layer, kv.Key.EntityType, kv.Value))
            .OrderByDescending(e => e.Count)
            .ThenBy(e => e.Layer, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.EntityType, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string EntityLayer(IList<string> lines, int start)
    {
        for (int i = start; i < lines.Count - 1; i += 2)
        {
            string code = lines[i].Trim();
            if (code == "0") return string.Empty;
            if (code == "8") return lines[i + 1].Trim();
        }

        return string.Empty;
    }

    private static string? RoleOf(string layer, PlanClassificationOptions options)
        => options.RoleOf(layer);

    /// <summary>
    /// The BLOCKS section, as the segments each named block draws in its own coordinates.
    ///
    /// Read with the same four entity readers as the drawing itself, so a block's geometry is
    /// understood exactly as loose linework would be — including the arc provenance that decides
    /// whether a column is round.
    /// </summary>
    private static Dictionary<string, List<DxfSegment>> ReadBlocks(IList<string> lines)
    {
        var blocks = new Dictionary<string, List<DxfSegment>>(StringComparer.OrdinalIgnoreCase);

        int i = 0;
        bool inBlocks = false;
        string? name = null;
        List<DxfSegment>? current = null;

        while (i < lines.Count - 1)
        {
            string code = lines[i].Trim();
            string value = lines[i + 1].Trim();

            if (code != "0") { i += 2; continue; }

            if (value == "SECTION")
            {
                for (int j = i + 2; j < Math.Min(i + 8, lines.Count - 1); j += 2)
                {
                    if (lines[j].Trim() == "2")
                    {
                        inBlocks = lines[j + 1].Trim() == "BLOCKS";
                        break;
                    }
                }
                i += 2;
                continue;
            }

            if (value == "ENDSEC") { inBlocks = false; name = null; current = null; i += 2; continue; }
            if (!inBlocks) { i += 2; continue; }

            if (value == "BLOCK")
            {
                current = new List<DxfSegment>();
                name = null;
                // The block's name is the "2" pair inside its own header.
                for (int j = i + 2; j < lines.Count - 1; j += 2)
                {
                    if (lines[j].Trim() == "0") break;
                    if (lines[j].Trim() == "2") { name = lines[j + 1].Trim(); break; }
                }
                if (name is not null) blocks[name] = current;
                i += 2;
                continue;
            }

            if (value == "ENDBLK") { name = null; current = null; i += 2; continue; }

            if (current is not null)
            {
                switch (value)
                {
                    case "LINE": i = ReadLine(lines, i + 2, current); continue;
                    case "ARC": i = ReadArc(lines, i + 2, current); continue;
                    case "LWPOLYLINE": i = ReadLwPolyline(lines, i + 2, current); continue;
                    case "POLYLINE": i = ReadPolyline(lines, i + 2, current); continue;
                }
            }

            i += 2;
        }

        return blocks;
    }

    /// <summary>
    /// One placement of a block: its geometry scaled, rotated and moved to where it sits.
    ///
    /// Geometry drawn on layer "0" inside a block takes the layer of the INSERT — that is the DXF
    /// rule, and it is how a generic column block lands on a column layer. Geometry with a layer
    /// of its own keeps it.
    /// </summary>
    private static int ReadInsert(
        IList<string> lines, int start,
        IReadOnlyDictionary<string, List<DxfSegment>> blocks,
        List<DxfSegment> segments)
    {
        string layer = string.Empty, blockName = string.Empty;
        double x = 0, y = 0, sx = 1, sy = 1, rotation = 0;

        int next = ScanEntity(lines, start, (code, value) =>
        {
            switch (code)
            {
                case "8": layer = value; break;
                case "2": blockName = value; break;
                case "10": TryNumber(value, out x); break;
                case "20": TryNumber(value, out y); break;
                case "41": if (TryNumber(value, out double a)) sx = a; break;
                case "42": if (TryNumber(value, out double b)) sy = b; break;
                case "50": TryNumber(value, out rotation); break;
            }
        });

        if (blockName.Length == 0 || !blocks.TryGetValue(blockName, out var body)) return next;

        // A zero scale would collapse the block onto a point; treat it as unset rather than
        // writing a stack of zero-length segments.
        if (sx == 0) sx = 1;
        if (sy == 0) sy = 1;

        double radians = rotation * Math.PI / 180.0;
        double cos = Math.Cos(radians), sin = Math.Sin(radians);

        DxfPoint Place(DxfPoint p)
        {
            double px = p.X * sx, py = p.Y * sy;
            return new DxfPoint(x + px * cos - py * sin, y + px * sin + py * cos);
        }

        foreach (var s in body)
        {
            string on = s.Layer is "0" or "" ? layer : s.Layer;
            segments.Add(new DxfSegment(on, Place(s.Start), Place(s.End)) { FromCurve = s.FromCurve });
        }

        return next;
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

    private static int ReadTextTag(
        IList<string> lines,
        int start,
        List<DxfPositionedTag> into,
        bool stripMTextFormatting)
    {
        string layer = string.Empty;
        var chunks = new List<string>();
        double x = 0, y = 0, height = 0;
        bool hasX = false, hasY = false;

        int next = ScanEntity(lines, start, (code, value) =>
        {
            switch (code)
            {
                case "8": layer = value; break;
                case "1": chunks.Add(value); break;
                case "3" when stripMTextFormatting: chunks.Add(value); break;
                case "10": if (TryNumber(value, out x)) hasX = true; break;
                case "20": if (TryNumber(value, out y)) hasY = true; break;
                case "40": TryNumber(value, out height); break;
            }
        });

        string raw = string.Concat(chunks);
        if (raw.Length > 0 && hasX && hasY)
        {
            string text = stripMTextFormatting ? PlainMText(raw) : raw;
            into.Add(new DxfPositionedTag(text, new DxfPoint(x, y), layer, raw) { Height = height });
        }

        return next;
    }

    private static string PlainMText(string raw)
    {
        var text = new StringBuilder(raw.Length);

        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c == '\\' && i + 1 < raw.Length)
            {
                char command = raw[++i];
                switch (command)
                {
                    case 'P':
                    case 'p':
                        text.Append('\n');
                        break;
                    case '{':
                        text.Append('{');
                        break;
                    case '}':
                        text.Append('}');
                        break;
                    case '\\':
                        text.Append('\\');
                        break;
                    case 'f':
                    case 'F':
                    case 'H':
                    case 'h':
                    case 'C':
                    case 'c':
                        if (!TrySkipMTextCommand(raw, ref i))
                        {
                            text.Append('\\');
                            text.Append(command);
                        }
                        break;
                    default:
                        text.Append('\\');
                        text.Append(command);
                        break;
                }
                continue;
            }

            if (c is '{' or '}') continue;
            text.Append(c);
        }

        return text.ToString().Trim();
    }

    private static bool TrySkipMTextCommand(string raw, ref int commandIndex)
    {
        int semicolon = raw.IndexOf(';', commandIndex + 1);
        if (semicolon < 0) return false;
        commandIndex = semicolon;
        return true;
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
