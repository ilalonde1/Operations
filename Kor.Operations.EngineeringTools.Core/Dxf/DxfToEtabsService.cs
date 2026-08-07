using System.Globalization;
using System.Text;

namespace Kor.Operations.EngineeringTools.Dxf;

public sealed record DxfToEtabsRequest
{
    public required string DxfFolder { get; init; }

    /// <summary>An .e2k that ETABS itself exported from the target model — the source of storeys, grids and materials.</summary>
    public required string ReferenceE2k { get; init; }

    public required string OutputE2k { get; init; }

    /// <summary>Restrict to one building's sheets, e.g. "B" for a "BLDG B" tower.</summary>
    public string? BuildingTag { get; init; }

    /// <summary>
    /// Translation applied to drawing coordinates. Defaults to none: the CAD export and the
    /// model both come out of the same Revit project, so the drawings already sit in the
    /// model's coordinate system — on 31168 the core walls land on grid lines 15 and 16 to
    /// within 0.05". Set <see cref="CentreOnGrid"/> only for a drawing that does not share it.
    /// </summary>
    public (double X, double Y)? Offset { get; init; }

    /// <summary>Fall back to centring the drawings on the model's grid extents.</summary>
    public bool CentreOnGrid { get; init; }

    public PlanClassificationOptions Classification { get; init; } = new();
    public ComposeOptions Compose { get; init; } = new();
}

public sealed record SheetOutcome(
    string File, string Label, string? Building, IReadOnlyList<int> Levels,
    IReadOnlyList<string> Stories, int Walls, int Columns, int Slabs, IReadOnlyList<string> Flags);

public sealed record DxfToEtabsReport(
    string OutputPath,
    int SheetsRead,
    int SheetsPlaced,
    int StoriesPopulated,
    ComposeSummary Summary,
    (double X, double Y) AppliedOffset,
    IReadOnlyList<SheetOutcome> Sheets,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Turns a folder of structural plan DXFs into an ETABS model: read, classify,
/// map each sheet onto the storeys it serves, and merge the result into a model
/// ETABS exported. One import in ETABS then carries the whole building.
/// </summary>
public static class DxfToEtabsService
{
    public static DxfToEtabsReport Run(DxfToEtabsRequest request)
    {
        var doc = E2kDocument.Load(request.ReferenceE2k);
        var stories = doc.ReadStories();
        if (stories.Count == 0)
            throw new InvalidOperationException("The reference model lists no storeys.");

        var storyNames = stories.Select(s => s.Name).ToList();
        var byName = stories.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

        var files = Directory.EnumerateFiles(request.DxfFolder, "*.dxf", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var warnings = new List<string>();
        var outcomes = new List<SheetOutcome>();
        var parsed = new List<(PlanSheetInfo Sheet, PlanGeometrySet Geometry, IReadOnlyList<string> Stories)>();

        foreach (string file in files)
        {
            var sheet = PlanSheetNaming.Parse(file);

            if (request.BuildingTag is not null &&
                sheet.BuildingTag is not null &&
                !string.Equals(sheet.BuildingTag, request.BuildingTag, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var segments = DxfPlanReader.ReadSegments(file);
            var geometry = StructuralPlanClassifier.Classify(segments, request.Classification);
            var matched = PlanSheetNaming.MatchStories(sheet, storyNames);

            outcomes.Add(new SheetOutcome(
                sheet.FileName, sheet.Label, sheet.BuildingTag, sheet.Levels, matched,
                geometry.Walls.Count, geometry.Columns.Count, geometry.Slabs.Count, geometry.Flags));

            if (matched.Count == 0)
            {
                warnings.Add(!sheet.HasPlacement
                    ? $"{sheet.FileName}: no level number in the sheet name — not placed."
                    : $"{sheet.FileName}: levels {string.Join(",", sheet.Levels.Select(l => l.ToString()).Concat(sheet.ParkadeLevels.Select(p => "P" + p)))} match no storey in the model — not placed.");
                continue;
            }

            if (geometry.Walls.Count == 0 && geometry.Columns.Count == 0 && geometry.Slabs.Count == 0)
            {
                warnings.Add($"{sheet.FileName}: no structural outlines found on the expected layers — not placed.");
                continue;
            }

            parsed.Add((sheet, geometry, matched));
        }

        var offset = request.Offset
            ?? (request.CentreOnGrid ? AutoOffset(doc, parsed.Select(p => p.Geometry)) : (0.0, 0.0));

        var placements = new List<StoryPlacement>();
        foreach (var (sheet, geometry, matched) in parsed)
            foreach (string storyName in matched)
                if (byName.TryGetValue(storyName, out var story))
                    placements.Add(new StoryPlacement(story, geometry, sheet.FileName));

        var composeOptions = request.Compose with { OffsetX = offset.X, OffsetY = offset.Y };
        var summary = E2kGeometryComposer.Compose(doc, placements, composeOptions);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.OutputE2k))!);
        doc.Save(request.OutputE2k);

        return new DxfToEtabsReport(
            request.OutputE2k, files.Count, parsed.Count,
            placements.Select(p => p.Story.Name).Distinct().Count(),
            summary, offset, outcomes, warnings);
    }

    /// <summary>
    /// Centres the drawings on the model's grid. Drafting exports carry Revit's
    /// project coordinates while ETABS models sit near their own origin, so without
    /// this the geometry lands thousands of inches away from the grid.
    /// </summary>
    private static (double X, double Y) AutoOffset(E2kDocument doc, IEnumerable<PlanGeometrySet> sets)
    {
        var (gMinX, gMaxX, gMinY, gMaxY) = ReadGridExtents(doc);

        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        void Take(DxfPoint p)
        {
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }

        foreach (var set in sets)
        {
            foreach (var wall in set.Walls) { Take(wall.Start); Take(wall.End); }
            foreach (var column in set.Columns) Take(column.Center);
            foreach (var slab in set.Slabs) foreach (var p in slab.Points) Take(p);
        }

        if (minX > maxX || double.IsInfinity(gMinX)) return (0, 0);

        double dxfCx = (minX + maxX) / 2.0, dxfCy = (minY + maxY) / 2.0;
        double modelCx = (gMinX + gMaxX) / 2.0, modelCy = (gMinY + gMaxY) / 2.0;
        return (modelCx - dxfCx, modelCy - dxfCy);
    }

    private static (double MinX, double MaxX, double MinY, double MaxY) ReadGridExtents(E2kDocument doc)
    {
        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;

        foreach (string raw in doc.LinesOf("GRIDS"))
        {
            string line = raw.Trim();
            if (!line.StartsWith("GRID ", StringComparison.OrdinalIgnoreCase)) continue;

            int dirAt = line.IndexOf("DIR \"", StringComparison.OrdinalIgnoreCase);
            int coordAt = line.IndexOf("COORD ", StringComparison.OrdinalIgnoreCase);
            if (dirAt < 0 || coordAt < 0) continue;

            char dir = char.ToUpperInvariant(line[dirAt + 5]);
            string tail = line[(coordAt + "COORD ".Length)..].Trim();
            string token = new(tail.TakeWhile(c => !char.IsWhiteSpace(c)).ToArray());
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) continue;

            if (dir == 'X') { minX = Math.Min(minX, value); maxX = Math.Max(maxX, value); }
            else if (dir == 'Y') { minY = Math.Min(minY, value); maxY = Math.Max(maxY, value); }
        }

        if (double.IsInfinity(minY)) { minY = 0; maxY = 0; }
        return (minX, maxX, minY, maxY);
    }

    /// <summary>A report an engineer can read in under a minute before opening the model.</summary>
    public static string FormatReport(DxfToEtabsReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Model written : {report.OutputPath}");
        sb.AppendLine($"Sheets read   : {report.SheetsRead}   placed: {report.SheetsPlaced}");
        sb.AppendLine($"Storeys built : {report.StoriesPopulated}");
        sb.AppendLine($"Walls         : {report.Summary.Walls}");
        sb.AppendLine($"Columns       : {report.Summary.Columns}");
        sb.AppendLine($"Floors        : {report.Summary.Floors}");
        sb.AppendLine($"Joints        : {report.Summary.Points}");
        sb.AppendLine($"Sections made : {string.Join(", ", report.Summary.Sections)}");
        sb.AppendLine($"Offset applied: {report.AppliedOffset.X:0.#}, {report.AppliedOffset.Y:0.#} in");
        sb.AppendLine();

        sb.AppendLine("Sheet                                                  Lvls  Storeys  Walls  Cols  Slabs");
        foreach (var s in report.Sheets.OrderBy(s => s.File, StringComparer.OrdinalIgnoreCase))
        {
            string name = s.File.Length <= 52 ? s.File.PadRight(52) : s.File[..49] + "...";
            sb.AppendLine($"{name}  {s.Levels.Count,4}  {s.Stories.Count,7}  {s.Walls,5}  {s.Columns,4}  {s.Slabs,5}");
        }

        if (report.Warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Not placed:");
            foreach (string w in report.Warnings) sb.AppendLine("  - " + w);
        }

        if (report.Summary.Flags.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Flags ({report.Summary.Flags.Count}) — outlines that needed judgement:");
            foreach (string f in report.Summary.Flags.Take(40)) sb.AppendLine("  - " + f);
            if (report.Summary.Flags.Count > 40) sb.AppendLine($"  ... and {report.Summary.Flags.Count - 40} more");
        }

        return sb.ToString();
    }
}
