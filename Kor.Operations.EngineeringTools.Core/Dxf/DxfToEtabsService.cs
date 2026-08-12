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
    /// Cut the model down to one tower: its storeys and the shared podium ones, with the other
    /// towers' storeys removed so none of them stands empty.
    /// </summary>
    public string? TowerOnly { get; init; }

    /// <summary>
    /// Translation applied to drawing coordinates. Defaults to none: the CAD export and the
    /// model both come out of the same Revit project, so the drawings already sit in the
    /// model's coordinate system — on 31168 the core walls land on grid lines 15 and 16 to
    /// within 0.05". Set <see cref="CentreOnGrid"/> only for a drawing that does not share it.
    /// </summary>
    public (double X, double Y)? Offset { get; init; }

    /// <summary>Fall back to centring the drawings on the model's grid extents.</summary>
    public bool CentreOnGrid { get; init; }

    /// <summary>
    /// Add parkade storeys the drawings have and the model does not. On by the engineer's
    /// instruction — "the model needs to go to P5" — where her model stopped at P3.
    /// </summary>
    public bool AddMissingParkadeStoreys { get; init; } = true;

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

        // Before anything else: the storey list is what ETABS builds from, and an export parks the
        // base a thousand feet under the building with the whole distance folded into the lowest
        // storey. Left alone, every member down there is extruded that far on import.
        bool baseNormalised = doc.NormaliseBaseStorey();

        // A model of one tower carries only that tower's storeys. On a site model the others stand
        // empty, which is what the engineer saw: "some levels don't exist, they're blank."
        var droppedStoreys = request.TowerOnly is null
            ? Array.Empty<string>()
            : doc.KeepOnlyTower(request.TowerOnly).ToArray();

        // Drafting can issue parkade levels the model has never had. On 31138 the drawings go to
        // LEVEL P5 and the model stopped at P3, so two whole floors were read and placed nowhere —
        // "the model needs to go to P5". The storeys are added below the lowest parkade level at the
        // height that parkade already uses, and the base drops by the same amount.
        var addedStoreys = Array.Empty<string>();
        if (request.AddMissingParkadeStoreys)
        {
            var wanted = Directory.EnumerateFiles(request.DxfFolder, "*.dxf", SearchOption.TopDirectoryOnly)
                .SelectMany(f => PlanSheetNaming.Parse(f).ParkadeLevels)
                .Distinct()
                .ToList();
            addedStoreys = doc.AddParkadeStoreysBelow(wanted).ToArray();
        }

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
                sheet.BuildingTags.Count > 0 &&
                !sheet.BuildingTags.Contains(request.BuildingTag, StringComparer.OrdinalIgnoreCase))
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

        if (baseNormalised)
        {
            var lowest = doc.ReadStories().OrderBy(s => s.Elevation).First();
            summary = summary with
            {
                Flags = summary.Flags.Append(
                    $"The lowest storey ({lowest.Name}) was exported {480 / 12:0}ft-plus tall, because ETABS " +
                    "parks the base far below the building and folds the distance into it. Its height has been " +
                    $"set to a typical storey and the base raised to match, so the storey now spans " +
                    $"{(lowest.Elevation - lowest.ElevationBelow) / 12:0.0}ft. No other storey moved.").ToList(),
            };
        }

        if (addedStoreys.Length > 0)
        {
            summary = summary with
            {
                Flags = summary.Flags.Append(
                    $"ADDED TO YOUR STOREY LIST: {string.Join(", ", addedStoreys)}. The drawings carry these " +
                    "parkade levels and the model did not, so their geometry had nowhere to go. Each was given " +
                    "the height your parkade already uses and the base dropped by the same total, so every " +
                    "storey above them is exactly where it was.").ToList(),
            };
        }

        if (droppedStoreys.Length > 0)
        {
            // The reference's own members on the towers we removed would otherwise point at
            // storeys that no longer exist.
            int orphaned = doc.DropAssignsForMissingStoreys();
            summary = summary with
            {
                Flags = summary.Flags.Append(
                    $"Tower {request.TowerOnly} only: {droppedStoreys.Length} storey(s) belonging to other towers " +
                    $"were removed from the storey list, along with {orphaned} assign(s) that stood on them. " +
                    $"Removed: {string.Join(", ", droppedStoreys.Take(8))}{(droppedStoreys.Length > 8 ? ", …" : "")}.").ToList(),
            };
        }

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
            // Flags about the model as a whole — storeys left without a plate, members already in
            // the engineer's model — are the ones worth acting on, and there are only a handful.
            // Listed with the per-sheet flags they fell off the end of a 766-line truncation.
            var wholeModel = report.Summary.Flags.Where(f => !f.Contains(".dxf:", StringComparison.OrdinalIgnoreCase)).ToList();
            var perSheet = report.Summary.Flags.Where(f => f.Contains(".dxf:", StringComparison.OrdinalIgnoreCase)).ToList();

            if (wholeModel.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("About the model as a whole:");
                foreach (string f in wholeModel) sb.AppendLine("  - " + f);
            }

            sb.AppendLine();
            sb.AppendLine($"Flags ({perSheet.Count}) — outlines that needed judgement:");
            foreach (string f in perSheet.Take(40)) sb.AppendLine("  - " + f);
            if (perSheet.Count > 40) sb.AppendLine($"  ... and {perSheet.Count - 40} more");
        }

        return sb.ToString();
    }
}
