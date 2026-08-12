using System.Globalization;
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// The two real projects and the pieces of them the audits need, in one place: generate the model
/// once, then read it back as text the way ETABS would.
/// </summary>
internal static class GeneratedModel
{
    private const string Residential = @"\\Kor-fs01\Projects\Projects\03 Residential";

    internal sealed record Project(string Name, string DxfFolder, string Reference);

    internal static readonly Project Langara = new(
        "31168 YMCA Langara",
        $@"{Residential}\31168-01 (YMCA Langara Vancouver)\02 Engineering\02 Lateral Design\01 ETABS Models\_DXF-plans-for-rebuild",
        $@"{Residential}\31168-01 (YMCA Langara Vancouver)\02 Engineering\02 Lateral Design\01 ETABS Models\31168-reference.e2k");

    internal static readonly Project WestFirst = new(
        "31138 2170 W 1st",
        $@"{Residential}\31138-01 (2170 W 1st Ave Vancouver BC)\02 Engineering\02 Lateral Design\_DXF-plans-for-rebuild",
        $@"{Residential}\31138-01 (2170 W 1st Ave Vancouver BC)\02 Engineering\02 Lateral Design\01 ETABS Models\31138-reference-from-Andrea-gravity.e2k");

    internal static TheoryData<string> Projects => new() { Langara.Name, WestFirst.Name };

    internal static Project For(string name) => name.StartsWith("31168") ? Langara : WestFirst;

    /// <summary>
    /// Drawn members that are read and then neither modelled nor already present. Ratchets — these
    /// may only ever come down. What remains is what the drawings genuinely do not resolve.
    /// </summary>
    internal const int LangaraLostCeiling = 7;
    internal const int WestFirstLostCeiling = 24;

    /// <summary>
    /// Generated columns whose size or shape does not match the footprint they were drawn from.
    /// Ratchets, same rule: down only.
    /// </summary>
    internal const int LangaraMissizedCeiling = 0;
    internal const int WestFirstMissizedCeiling = 0;

    /// <summary>A section as the model declares it: a circle has no second dimension.</summary>
    internal sealed record SectionShape(double D, double B, bool IsRound);

    /// <summary>A column footprint as the drawing gives it, in model coordinates.</summary>
    internal sealed record DrawnColumn(DxfPoint At, double Width, double Depth, bool IsRound);

    internal sealed record Built(string[] Lines, DxfToEtabsReport Report);

    private static readonly Dictionary<string, Built> Cache = new();
    private static readonly Dictionary<string, PlanGeometrySet?> Classified = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Builds the model once per test run; null when the share is unreachable.</summary>
    internal static Built? BuildOrSkip(Project project)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(project.Name, out var cached)) return cached;
            if (!Directory.Exists(project.DxfFolder) || !File.Exists(project.Reference)) return null;

            string output = Path.Combine(Path.GetTempPath(), $"kor-coverage-{Guid.NewGuid():N}.e2k");
            try
            {
                var report = DxfToEtabsService.Run(new DxfToEtabsRequest
                {
                    DxfFolder = project.DxfFolder,
                    ReferenceE2k = project.Reference,
                    OutputE2k = output,
                });
                var built = new Built(File.ReadAllLines(output), report);
                Cache[project.Name] = built;
                return built;
            }
            finally
            {
                if (File.Exists(output)) File.Delete(output);
            }
        }
    }

    /// <summary>Re-reads and classifies one sheet, the same way the service does.</summary>
    internal static PlanGeometrySet? Classify(Project project, string fileName)
    {
        string path = Path.Combine(project.DxfFolder, fileName);
        lock (Classified)
        {
            if (Classified.TryGetValue(path, out var cached)) return cached;
            PlanGeometrySet? geometry = null;
            if (File.Exists(path))
                geometry = StructuralPlanClassifier.Classify(DxfPlanReader.ReadSegments(path));
            Classified[path] = geometry;
            return geometry;
        }
    }

    /// <summary>Storey names in the order the file lists them: top first, base last.</summary>
    internal static List<string> StoreysTopToBottom(string[] lines)
    {
        var storeys = new List<string>();
        foreach (string line in lines)
        {
            var m = Regex.Match(line.Trim(), @"^STORY\s+""([^""]+)""\s+HEIGHT");
            if (m.Success) storeys.Add(m.Groups[1].Value);
        }
        return storeys;
    }

    /// <summary>Every storey carrying at least one generated member, and how many of each.</summary>
    internal static Dictionary<string, (int Walls, int Columns, int Plates)> MembersByStorey(string[] lines)
    {
        var kind = new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines)
        {
            string t = line.Trim();
            var area = Regex.Match(t, @"^AREA\s+""(K[WFSO]\d+)""\s+(PANEL|FLOOR|AREA)");
            if (area.Success) kind[area.Groups[1].Value] = area.Groups[1].Value[1];
            var col = Regex.Match(t, @"^LINE\s+""(KC\d+)""\s+COLUMN");
            if (col.Success) kind[col.Groups[1].Value] = 'C';
        }

        var byStorey = new Dictionary<string, (int, int, int)>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines)
        {
            var m = Regex.Match(line.Trim(), @"^(?:AREA|LINE)ASSIGN\s+""(K\w+)""\s+""([^""]+)""");
            if (!m.Success || !kind.TryGetValue(m.Groups[1].Value, out char k)) continue;

            // Headers and openings ride on other members; a storey carrying only those is not
            // independently populated, so they do not count towards a storey being occupied.
            if (k is 'S' or 'O') continue;

            byStorey.TryGetValue(m.Groups[2].Value, out var c);
            byStorey[m.Groups[2].Value] = k switch
            {
                'W' => (c.Item1 + 1, c.Item2, c.Item3),
                'C' => (c.Item1, c.Item2 + 1, c.Item3),
                _   => (c.Item1, c.Item2, c.Item3 + 1),
            };
        }
        return byStorey;
    }

    /// <summary>Plan position of every joint the file declares.</summary>
    internal static Dictionary<string, DxfPoint> Joints(string[] lines)
    {
        var joints = new Dictionary<string, DxfPoint>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines)
        {
            var m = Regex.Match(line.Trim(), @"^POINT\s+""([^""]+)""\s+(-?[\d.]+)\s+(-?[\d.]+)");
            if (m.Success)
                joints[m.Groups[1].Value] = new DxfPoint(
                    double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                    double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
        }
        return joints;
    }

    /// <summary>Every storey each generated object is assigned to.</summary>
    internal static Dictionary<string, List<string>> AssignedStoreys(string[] lines)
    {
        var assigns = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines)
        {
            var m = Regex.Match(line.Trim(), @"^(?:AREA|LINE)ASSIGN\s+""(K\w+)""\s+""([^""]+)""");
            if (!m.Success) continue;
            if (!assigns.TryGetValue(m.Groups[1].Value, out var list)) assigns[m.Groups[1].Value] = list = new List<string>();
            if (!list.Contains(m.Groups[2].Value, StringComparer.OrdinalIgnoreCase)) list.Add(m.Groups[2].Value);
        }
        return assigns;
    }

    /// <summary>
    /// Where the drawings put walls and columns, by storey: the union of every sheet placed on that
    /// storey, in model coordinates.
    /// </summary>
    internal static Dictionary<string, List<DxfPoint>> DrawnByStorey(Project project, DxfToEtabsReport report)
    {
        var (ox, oy) = report.AppliedOffset;
        var byStorey = new Dictionary<string, List<DxfPoint>>(StringComparer.OrdinalIgnoreCase);

        foreach (var sheet in report.Sheets)
        {
            if (sheet.Stories.Count == 0) continue;
            var geometry = Classify(project, sheet.File);
            if (geometry is null) continue;

            var points = new List<DxfPoint>(geometry.Walls.Count + geometry.Columns.Count);
            foreach (var w in geometry.Walls)
                points.Add(new DxfPoint((w.Start.X + w.End.X) / 2 + ox, (w.Start.Y + w.End.Y) / 2 + oy));
            foreach (var c in geometry.Columns)
                points.Add(new DxfPoint(c.Center.X + ox, c.Center.Y + oy));

            foreach (string storey in sheet.Stories)
            {
                if (!byStorey.TryGetValue(storey, out var list)) byStorey[storey] = list = new List<DxfPoint>();
                list.AddRange(points);
            }
        }
        return byStorey;
    }

    /// <summary>Every frame section the file declares, by name.</summary>
    internal static Dictionary<string, SectionShape> Sections(string[] lines)
    {
        var sections = new Dictionary<string, SectionShape>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines)
        {
            var m = Regex.Match(line.Trim(), @"^FRAMESECTION\s+""(.+?)""\s+.*?SHAPE\s+""([^""]+)""");
            if (!m.Success) continue;

            bool round = m.Groups[2].Value.Contains("Circle", StringComparison.OrdinalIgnoreCase);
            var d = Regex.Match(line, @"\sD\s+([\d.]+)");
            var b = Regex.Match(line, @"\sB\s+([\d.]+)");
            if (!d.Success) continue;

            sections[m.Groups[1].Value] = new SectionShape(
                double.Parse(d.Groups[1].Value, CultureInfo.InvariantCulture),
                b.Success ? double.Parse(b.Groups[1].Value, CultureInfo.InvariantCulture) : 0.0,
                round);
        }
        return sections;
    }

    /// <summary>The section each generated member is assigned.</summary>
    internal static Dictionary<string, string> SectionOfMember(string[] lines)
    {
        var section = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines)
        {
            var m = Regex.Match(line.Trim(), @"^(?:AREA|LINE)ASSIGN\s+""(K\w+)""\s+""[^""]+""\s+SECTION\s+""(.+?)""(?:\s|$)");
            if (m.Success) section.TryAdd(m.Groups[1].Value, m.Groups[2].Value);
        }
        return section;
    }

    /// <summary>
    /// The RAW column linework each storey's sheets carry, in model coordinates — segments off the
    /// column layers, straight from the reader, with the arc flag the DXF entity type gives.
    ///
    /// Deliberately not the classifier's own PlanGeometrySet. Comparing a model built from the
    /// classifier against the classifier's own idea of what it read compares a thing to itself and
    /// can only ever agree: it passes unchanged with round-column detection disabled entirely.
    /// Shape has to be judged against the drawing.
    /// </summary>
    internal static Dictionary<string, List<DxfSegment>> ColumnLineworkByStorey(Project project, DxfToEtabsReport report)
    {
        var (ox, oy) = report.AppliedOffset;
        var byStorey = new Dictionary<string, List<DxfSegment>>(StringComparer.OrdinalIgnoreCase);

        foreach (var sheet in report.Sheets)
        {
            if (sheet.Stories.Count == 0) continue;
            string path = Path.Combine(project.DxfFolder, sheet.File);
            if (!File.Exists(path)) continue;

            var columnLines = DxfPlanReader.ReadSegments(path)
                .Where(s => s.Layer.Contains("_COL", StringComparison.OrdinalIgnoreCase))
                .Select(s => new DxfSegment(s.Layer,
                            new DxfPoint(s.Start.X + ox, s.Start.Y + oy),
                            new DxfPoint(s.End.X + ox, s.End.Y + oy)) { FromCurve = s.FromCurve })
                .ToList();
            if (columnLines.Count == 0) continue;

            foreach (string storey in sheet.Stories)
            {
                if (!byStorey.TryGetValue(storey, out var list)) byStorey[storey] = list = new List<DxfSegment>();
                list.AddRange(columnLines);
            }
        }
        return byStorey;
    }

    /// <summary>The engineer's own walls and columns in the reference model, by storey.</summary>
    internal static Dictionary<string, List<DxfPoint>> ExistingByStorey(Project project)
    {
        var byStorey = new Dictionary<string, List<DxfPoint>>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(project.Reference)) return byStorey;

        var doc = E2kDocument.Load(project.Reference);
        var geometry = E2kGeometryReader.Read(doc);

        void Add(string storey, DxfPoint at)
        {
            if (!byStorey.TryGetValue(storey, out var list)) byStorey[storey] = list = new List<DxfPoint>();
            list.Add(at);
        }

        foreach (var w in geometry.Walls)
            Add(w.Story, w.Midpoint);
        foreach (var c in geometry.Columns)
            Add(c.Story, c.At);

        return byStorey;
    }
}
