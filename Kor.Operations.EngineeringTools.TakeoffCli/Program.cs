if (TakeoffCliHelp.IsHelpRequest(args))
{
    TakeoffCliHelp.WriteTo(Console.Out);
    return 0;
}

// EVERY VERB IN ITS OWN FILE (Verbs/*.cs), asked in the order Program.cs always asked them (TakeoffVerbs).
foreach (var (_, matches, run) in TakeoffVerbs.All)
    if (matches(args)) return run(args);

// Drives the REAL takeoff engine: two concrete-schedule CSVs -> issue delta -> xlsx + docx.
// Usage: takeoff <before.csv> <after.csv> <out-basepath> [wbs1] [name] [beforeLabel] [afterLabel]
if (args.Length < 3)
{
    Console.WriteLine("Usage: takeoff <before.csv> <after.csv> <out-basepath> [wbs1] [name] [beforeLabel] [afterLabel]");
    return 1;
}

string beforePath = args[0];
string afterPath  = args[1];
string outBase    = args[2];
string wbs1        = args.Length > 3 ? args[3] : string.Empty;
string projectName = args.Length > 4 ? args[4] : string.Empty;
string beforeLabel = args.Length > 5 ? args[5] : "Before";
string afterLabel  = args.Length > 6 ? args[6] : "After";

var densities = RebarDensityTable.Default;
var before = TakeoffCsvImporter.Import(File.ReadAllText(beforePath), densities);
var after  = TakeoffCsvImporter.Import(File.ReadAllText(afterPath), densities);

var diff = TakeoffDiffService.Compare(before, after);
var model = new TakeoffReportModel(wbs1, projectName, beforeLabel, afterLabel, DateTime.UtcNow, diff);

File.WriteAllBytes(outBase + ".xlsx", TakeoffReportGenerator.BuildXlsx(model));
File.WriteAllBytes(outBase + ".docx", TakeoffReportGenerator.BuildDocx(model));

Console.WriteLine($"Concrete delta: {diff.TotalConcreteDeltaM3:N1} m3   " +
                  $"Rebar delta: {diff.TotalRebarDeltaTonnes:N1} t   " +
                  $"Formwork delta: {diff.TotalFormworkDeltaM2:N1} m2");
if (diff.AddedLevels.Count > 0) Console.WriteLine("Added levels: " + string.Join(", ", diff.AddedLevels));
if (diff.RemovedLevels.Count > 0) Console.WriteLine("Removed levels: " + string.Join(", ", diff.RemovedLevels));
Console.WriteLine($"Wrote {outBase}.xlsx and {outBase}.docx");
return 0;

// Decodes a rasterized plan page (PNG) and crops a region into the pixel buffers the Core
// PlanGeometry engine consumes. Crop box is [x0,x1) × [y0,y1), clamped to the image.
// The CLI's concrete I/O for the Core engine: the Anthropic-backed vision calls and the ImageSharp
// raster decode. The WPF app will register its own equivalents — the engine depends on neither.

sealed class CliPlanVision : IPlanVision
{
    public Task<string> SynthesizePageAsync(string pageJson, CancellationToken ct = default)
        => PlanVisionClient.SynthesizePageAsync(pageJson);
    public Task<string> LocatePlateAsync(string pageJson, byte[] downscaledPng, CancellationToken ct = default, string? feedback = null)
        => PlanVisionClient.LocatePlateAsync(pageJson, downscaledPng, feedback);
    public Task<string> ApportionThicknessAsync(byte[] plateCropPng, IReadOnlyList<int> thicknessesIn, CancellationToken ct = default, string? feedback = null)
        => PlanVisionClient.ApportionThicknessJsonAsync(plateCropPng, thicknessesIn, feedback);
}

// --deterministic: no vision, no spend. Every method throws; the engine already treats a failed
// vision call as an unresolved unknown (peer-estimated or residual, flagged) — so this yields the
// honest free takeoff: everything the drawings give up deterministically, nothing invented.
sealed class NoPlanVision : IPlanVision
{
    private static Task<string> No() => Task.FromException<string>(
        new InvalidOperationException("vision disabled (--deterministic)"));
    public Task<string> SynthesizePageAsync(string pageJson, CancellationToken ct = default) => No();
    public Task<string> LocatePlateAsync(string pageJson, byte[] downscaledPng, CancellationToken ct = default, string? feedback = null) => No();
    public Task<string> ApportionThicknessAsync(byte[] plateCropPng, IReadOnlyList<int> thicknessesIn, CancellationToken ct = default, string? feedback = null) => No();
}

sealed class CliPlanRaster : IPlanRaster
{
    public (int Width, int Height) ImageSize(string path) => PlanRaster.ImageSize(path);
    public byte[] LoadDownscaledPng(string path, int maxEdge) => PlanRaster.LoadDownscaledPng(path, maxEdge);
    public byte[] LoadCropPng(string path, int x0, int y0, int x1, int y1, int maxEdge) => PlanRaster.LoadCropPng(path, x0, y0, x1, y1, maxEdge);
    public RasterCrop LoadCrop(string path, int x0, int y0, int x1, int y1)
    {
        var c = PlanRaster.LoadCrop(path, x0, y0, x1, y1);
        return new RasterCrop(c.Lum, c.Width, c.Height, c.R, c.G, c.B);   // RGB planes → gray-fill vertical measurement
    }
}

static class PlanRaster
{
    public readonly record struct Crop(byte[] Lum, byte[] R, byte[] G, byte[] B, int Width, int Height);

    public static Crop LoadCrop(string path, int x0, int y0, int x1, int y1)
    {
        using var img = Image.Load<Rgb24>(path);
        x0 = Math.Clamp(x0, 0, img.Width);
        x1 = Math.Clamp(x1, 0, img.Width);
        y0 = Math.Clamp(y0, 0, img.Height);
        y1 = Math.Clamp(y1, 0, img.Height);
        int w = x1 - x0, h = y1 - y0;
        if (w <= 0 || h <= 0) throw new ArgumentException($"Empty crop after clamping: {w}x{h}.");

        var lum = new byte[w * h];
        var r = new byte[w * h];
        var g = new byte[w * h];
        var b = new byte[w * h];
        img.ProcessPixelRows(accessor =>
        {
            for (int yy = 0; yy < h; yy++)
            {
                var srcRow = accessor.GetRowSpan(y0 + yy);
                for (int xx = 0; xx < w; xx++)
                {
                    Rgb24 px = srcRow[x0 + xx];
                    int i = yy * w + xx;
                    r[i] = px.R; g[i] = px.G; b[i] = px.B;
                    lum[i] = (byte)(0.299 * px.R + 0.587 * px.G + 0.114 * px.B);
                }
            }
        });
        return new Crop(lum, r, g, b, w, h);
    }

    public static TakeoffElementType ParseElement(string raw) => (raw ?? "").Trim().ToLowerInvariant() switch
    {
        "wall" => TakeoffElementType.Wall,
        "beam" or "framing" => TakeoffElementType.Beam,
        "column" => TakeoffElementType.Column,
        "foundation" or "footing" or "mat" => TakeoffElementType.Foundation,
        "droppanel" or "drop" => TakeoffElementType.DropPanel,
        _ => TakeoffElementType.Slab,
    };

    // Full image pixel dimensions (for mapping a vision normalized box back to full-res pixels).
    public static (int Width, int Height) ImageSize(string path)
    {
        var info = Image.Identify(path);
        return (info.Width, info.Height);
    }

    // A PNG re-encoded with its long edge capped at maxEdge — sent to the vision API to keep token
    // cost down. Measurement always runs on the full-res image; normalized boxes are resolution-free.
    public static byte[] LoadDownscaledPng(string path, int maxEdge)
    {
        using var img = Image.Load<Rgb24>(path);
        int longEdge = Math.Max(img.Width, img.Height);
        if (longEdge > maxEdge)
        {
            double s = (double)maxEdge / longEdge;
            img.Mutate(c => c.Resize(Math.Max(1, (int)(img.Width * s)), Math.Max(1, (int)(img.Height * s))));
        }
        using var ms = new MemoryStream();
        img.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    // A PNG of just one plate's pixel box (long edge capped at maxEdge) — a focused image for a targeted
    // vision call so the model sees that plate alone, not the whole sheet.
    public static byte[] LoadCropPng(string path, int x0, int y0, int x1, int y1, int maxEdge)
    {
        using var img = Image.Load<Rgb24>(path);
        x0 = Math.Clamp(x0, 0, img.Width); x1 = Math.Clamp(x1, 0, img.Width);
        y0 = Math.Clamp(y0, 0, img.Height); y1 = Math.Clamp(y1, 0, img.Height);
        int w = Math.Max(1, x1 - x0), h = Math.Max(1, y1 - y0);
        img.Mutate(c => c.Crop(new SixLabors.ImageSharp.Rectangle(x0, y0, w, h)));
        int longEdge = Math.Max(img.Width, img.Height);
        if (longEdge > maxEdge)
        {
            double s = (double)maxEdge / longEdge;
            img.Mutate(c => c.Resize(Math.Max(1, (int)(img.Width * s)), Math.Max(1, (int)(img.Height * s))));
        }
        using var ms = new MemoryStream();
        img.Save(ms, new PngEncoder());
        return ms.ToArray();
    }
}

// Pages config for `vision-estimate`: project metadata + the rasterized sheets to read.
sealed class VisionPagesConfig
{
    public string? Project { get; set; }
    public string? Name { get; set; }
    public string? Issue { get; set; }
    public string Profile { get; set; } = "BC-moderate";
    public double Dpi { get; set; } = 110;
    public string Scale { get; set; } = "1/8\"=1'-0\"";   // fallback if the title-block scale is illegible
    public string? PngDir { get; set; }
    public double StoreyHeightIn { get; set; }            // floor-to-floor height for wall/column concrete; 0 = unknown
    public double SogThicknessIn { get; set; }            // slab-on-grade thickness when not legible on the footings sheet; 0 = leave unresolved
    // Ordered building levels (top → bottom), e.g. ["LEVEL 46", … , "P7"]. When present, vertical
    // concrete is priced from the wall/column SCHEDULES over these levels instead of plan poché pixels.
    public List<string>? Levels { get; set; }
    public Dictionary<string, double>? StoreyHeightInByLevel { get; set; }  // per-level overrides (inches); else StoreyHeightIn
    public List<VisionPage> Pages { get; set; } = new();
}

sealed class VisionPage
{
    public string Png { get; set; } = "";
    public double? Dpi { get; set; }
    public double? StoreyHeightIn { get; set; }           // overrides config StoreyHeightIn for this sheet's level band
}

// A suspended-slab measurement held back until the whole building is read, so its floor count can be
// reconciled across overlapping/duplicate sheets (BuildingRollup.AssignSlabFloors). Co-located wall and
// column footprints ride along and inherit the slab's reconciled count, as do thickened zones (drop
// panels / built-up transfer) detected on the same sheet, which are priced as their depth ABOVE the
// nominal slab so the field slab is never double-counted. BoxX0..BoxY1 are full-sheet pixels of the
// plate outline, used to attach a sheet's thickenings to the slab whose box contains them.
sealed record PendingSlab(
    string Level, string? Variant, double AreaSqFt, double ThicknessIn, double Confidence,
    bool ScaleConfirmed, double WallSqFt, double ColSqFt, double StoreyIn,
    int BoxX0, int BoxY0, int BoxX1, int BoxY1)
{
    public List<Thickening> Thickenings { get; } = new();
}

// A local slab thickening (drop panel / thickened band / built-up transfer zone): its area and the
// concrete depth ADDED above the nominal slab. Priced as a DropPanel plate at the owning slab's count.
sealed record Thickening(double AddedDepthIn, double AreaSqFt, double Confidence);

// Building config for the `estimate` mode: project metadata + the per-plate map (which sheet, which
// crop, how to measure it, the read thickness/height, and how many identical floors it covers).
sealed class EstimateConfig
{
    public string? Project { get; set; }
    public string? Name { get; set; }
    public string? Issue { get; set; }
    public string Profile { get; set; } = "BC-moderate";
    public double Dpi { get; set; } = 110;
    public string Scale { get; set; } = "1/8\"=1'-0\"";
    public string? PngDir { get; set; }
    public List<PlateConfig> Plates { get; set; } = new();
}

sealed class PlateConfig
{
    public string Level { get; set; } = "";
    public string Element { get; set; } = "Slab";
    public string? Variant { get; set; }
    public string Png { get; set; } = "";
    public int[] Crop { get; set; } = new int[4];     // x0, y0, x1, y1
    public string? Scale { get; set; }                // overrides config Scale
    public double? Dpi { get; set; }                  // overrides config Dpi
    public bool Gray { get; set; }                    // measure gray footprint (walls/cols) vs enclosed area
    public double DimensionIn { get; set; }           // slab/mat thickness, or wall/column storey height
    public int Count { get; set; } = 1;               // identical floors this plate stands in for
    public double AreaFraction { get; set; } = 1.0;   // split a gray footprint into wall vs column share
    public string? Grade { get; set; }
    public bool ScaleConfirmed { get; set; } = true;
    public double? RebarLbPerCyOverride { get; set; }
}

public sealed record TakeoffCliCommand(string Name, string Usage, string Description);

public static class TakeoffCliHelp
{
    public static IReadOnlyList<TakeoffCliCommand> Commands { get; } =
    [
        new("pdf-readable", "takeoff pdf-readable <pdf> [first] [last]", "Check whether a PDF has readable vector text."),
        new("pdf-takeoff", "takeoff pdf-takeoff <pdf> <out.dxf> [--page N] [--pages A-B] [--scale 96] [--markup] [--kor-layers] [--rules-db <conn>]", "Take a drawing PDF's structure off to DXF, reading the drawing itself unless --markup."),
        new("pdf-inventory", "takeoff pdf-inventory <pdf> [--pages A-B] [--scale N] [--rules-db <conn>] [--json out.json]", "Ledger every content class on each page: read, discarded, unread, ignored, unaccounted."),
        new("dxf-census", "takeoff dxf-census <beforeDir> <afterDir> [--only LAYER,LAYER]", "Which layers moved between two folders of DXFs written from the same pages; exit 2 when a layer outside --only moved."),
        new("intake-baseline", "takeoff intake-baseline <stickFilesDir> <outDir>", "Write the thirteen plan DXFs of the five stick files to a step folder, for dxf-census."),
        new("pdf-overlay", "takeoff pdf-overlay <pdf> <page> <out.png> --scale N [--dpi 40] [--walls] [--columns] [--rules-db <conn>] [--mark x y]... [--crop x y halfW halfH]", "Draw what the intake extracted over the rasterised page."),
        new("pdf-vs-dxf", "takeoff pdf-vs-dxf <pdf> <dxfFolder> --scale N [--rules-db <conn>]", "Compare the PDF side's reads against a Revit DXF export of the same sheets."),
        new("set-diff", "takeoff set-diff <old.pdf> <new.pdf> --scale N [--sheet S2.02] [--overlay <dir>] [--rules-db <conn>]", "Reissue Impact: what changed between two issues, sheet by sheet, as objects — columns, walls, footings, grid, storeys, schedules; --overlay paints each changed sheet."),
        new("markup-list", "takeoff markup-list <pdf> --scale N [--pages A-B] [--rules-db <conn>]", "A mark-up as a list of instructions: each annotation's words, what it asks, how far, where on the grid, beside which member."),
        new("markup-reconcile", "takeoff markup-reconcile <round.pdf> <backchecked.pdf> --scale N [--engineer <name>]", "The engineer's round against the drafter's back-checked copy: each item done (a tick beside it), replied (words beside it) or open."),
        new("set-check", "takeoff set-check <pdf> --scale N [--reference model.e2k]", "Set Check: the gatekeeper's page — unnumbered or untyped sheets, scale conflicts, marks never placed, undeclared column sizes, footings without labels, grid names twice, a grid axis elsewhere than the set draws it, storeys against the model."),
        new("dxf-render", "takeoff dxf-render <plan.dxf> <out.png> [--size 1800] [--layers SLABEDG,...]", "Render structural DXF layers to a PNG."),
        new("dxf-inspect", "takeoff dxf-inspect <plan.dxf> [--walls] [--plates] [--loops]", "Inspect DXF layers, loops, wall outlines, recovered floor plates; --loops lists loops whose centroid is the vertex mean."),
        new("publish", "takeoff publish <job> [--model-folder <folder>] [--dxf-folder <folder>] [--rules-db <c>] [--per-building] [--land]", "Discover, build, verify, summarize, gate and land a DXF-to-ETABS publish."),
        new("dxf-buildings", "takeoff dxf-buildings <dxfFolder> <reference.e2k>", "Say which storeys belong to which building."),
        new("e2k-compare", "takeoff e2k-compare <reference.e2k> <candidate.e2k> <story> [...]", "Compare generated ETABS geometry against a reference model."),
        new("dxf-import-rules", "takeoff dxf-import-rules <questions.xlsx> --engineer <name> [--rules-db <connection>]", "Import per-job DXF rule answers."),
        new("corpus-read", "takeoff corpus-read <projectsRoot> [out.txt] [--limit N]", "Extract readable project corpus text."),
        new("dxf-to-etabs", "takeoff dxf-to-etabs <dxfFolder> <reference.e2k|-> <out.e2k> [--levels levels.csv [--levels-unit in|ft|mm|m]] [--job 31168] [options]", "Build an ETABS model from DXF plans. --job names the job the banked facts are matched on; absent, the five-digit number in the input names."),
        new("verify-e2k", "takeoff verify-e2k <model.e2k> [--joint-tolerance <in>] [--dropped <a,b,c>] [--reference <ref.e2k>] [--report report.txt] [--questions questions.xlsx]", "Refuse a finished model that breaks a structural invariant."),
        new("e2k-multiset", "takeoff e2k-multiset <before.e2k> <after.e2k>", "Prove a revision only renamed members and moved none."),
        new("ifc-takeoff", "takeoff ifc-takeoff <model.ifc> <out.xlsx>", "Generate a quantity takeoff from an IFC model."),
        new("e2k-takeoff", "takeoff e2k-takeoff <model.e2k> <out.xlsx> [--metric]", "Price the concrete in a generated ETABS model."),
        new("revit-takeoff", "takeoff revit-takeoff <folder|schedule.csv> [more.csv ...] <out.xlsx>", "Price the concrete straight off Revit schedule exports."),
        new("sco-schedule", "takeoff sco-schedule <folder|file.SCO> [more...] <out.xlsx>", "Read a project's column demands out of its S-Concrete files."),
        new("e2k-ask", "takeoff e2k-ask <model.e2k> [storeys|look|openings|sections|concrete] [storey]", "Ask a finished ETABS model about itself."),
        new("vector-takeoff", "takeoff vector-takeoff <pdf> <pngDir> <out.xlsx> [first] [last] [scale] [heightsJson] [--deterministic] [--fresh]", "Run the vector PDF quantity takeoff pipeline."),
        new("vector-plate", "takeoff vector-plate <pdf> <page> <png>", "Ask the vision layer for one slab plate box."),
        new("vector-zones", "takeoff vector-zones <pdf> <png> <page> <modalThk>", "Read thickened slab zones inside a plate."),
        new("vector-synth", "takeoff vector-synth <pdf> <page>", "Synthesize a structured takeoff for one PDF page."),
        new("vector-digest", "takeoff vector-digest <pdf> <out.json> [firstPage] [lastPage]", "Write a drawing digest JSON from a PDF."),
        new("vector-sched", "takeoff vector-sched <pdf> <page>", "Probe slab schedule thickness cells on a page."),
        new("vector-dump", "takeoff vector-dump <pdf> <page>", "Dump vector text and paths from a PDF page."),
        new("sched-border", "takeoff sched-border <pdf> <page>", "Show the border and row cells found under every schedule heading on a page."),
        new("vector-plate-auto", "takeoff vector-plate-auto <png>", "Probe deterministic slab plate detection on a PNG."),
        new("vector-geom", "takeoff vector-geom <pdf> <page>", "Dump vector geometry area candidates."),
        new("vector-signals", "takeoff vector-signals <pdf> <page> [png] [scaleDenom=100] [dpi=110]", "Compare all slab-area signal candidates for a sheet."),
        new("scale-scan", "takeoff scale-scan <pdf> [first] [last]", "Scan pages for machine-readable title-block scales."),
        new("vector-words", "takeoff vector-words <pdf> <page> [needle] | --band y0 y1 [x0 x1]", "Dump PDF words by font size and position; --band lists every word in a band with how many share its spot (a bubble drawn twice)."),
        new("vector-lines", "takeoff vector-lines <pdf> <page> [--region x0 y0 x1 y1] [--min-pt 20] [--scale 96]", "GROUND TRUTH: every long axis-aligned run the page itself draws, grouped by the line it sits on, in points and drawing mm - is the drafter's line there, before a reader is blamed."),
        new("vector-find", "takeoff vector-find <pdf> WORD [WORD ...] [--pages a-b]", "Every distinct text line of a set that mentions a word (regex, case-insensitive), most repeated first, with its pages: what does the drawing CALL this?"),
        new("vision-estimate", "takeoff vision-estimate <pages.json> <out.xlsx>", "Run the vision-assisted estimate pipeline."),
        new("estimate", "takeoff estimate <config.json> <out.xlsx>", "Run a configured raster takeoff estimate."),
        new("measure", "takeoff measure <png> <x0> <y0> <x1> <y1> <dpi> <scaleNote> [gray]", "Measure one raster crop."),
        new("graycomp", "takeoff graycomp <png> <x0> <y0> <x1> <y1> <dpi> <scaleNote> [lo] [hi]", "Inspect gray-fill connected components in a crop."),
        new("hatch", "takeoff hatch <png> <x0> <y0> <x1> <y1> <dpi> <scaleNote> [densityPct] [minSqFt] [winR]", "Detect hatched footing regions in a crop."),
        new("wallsched", "takeoff wallsched <png>", "Read a wall schedule from a raster sheet."),
        new("wallplan", "takeoff wallplan <png>", "Read core wall key-plan mark lengths."),
        new("perim", "takeoff perim <png> [scale] [dpi]", "Measure a plate contour perimeter from a rendered page."),
        new("col-text", "takeoff col-text <pdf> <page>", "Read a column schedule deterministically from PDF text."),
        new("sched-read", "takeoff sched-read <kind> <sheet.png>", "Run one vision schedule reader and print JSON."),
        new("sched-tokens", "takeoff sched-tokens <pdf> <page>", "Dump schedule keyword tokens from a page."),
        new("dedupe-probe", "takeoff dedupe-probe <pdf> <page> <needle>", "Inspect PDF word de-duplication for a token."),
        new("footings", "takeoff footings <pdf> [first] [last]", "Run deterministic footing schedule takeoff."),
        new("render", "takeoff render <pdf> <pngDir> [dpi] [first] [last]", "Rasterize PDF pages to PNG files."),
        new("elev-scan", "takeoff elev-scan <pdf> [first] [last]", "Scan for floor elevations and storey height notes; prints the level ladder's gaps at the sheet's scale."),
        new("e2k-storeys", "takeoff e2k-storeys <model.e2k>", "A model's storeys top to bottom with their heights — what elev-scan's ladder is measured against."),
        new("model-render", "takeoff model-render <model.e2k> <out.png|out.svg> [\"title\"] [--columns N] [--cell px] [--no-png]", "Every storey of a model on one sheet - walls dark red, columns green, each plate its own colour - so a person can LOOK before counting. SVG always, PNG through Edge."),
        new("model-diff", "takeoff model-diff <before.e2k> <after.e2k>", "What a second model lost or gained against a first, storey by storey, with positions: byte-identical, or plates moved / columns and walls lost and gained, frames matched by grid label. Exit 2 when they differ."),
        new("model-yardstick", "takeoff model-yardstick <model.e2k> <yardstick.e2k>", "A model against the engineer's own model of the job, column by column: frames matched by grid name, storeys by name, residuals both ways (ours to theirs, theirs to ours), every storey listed."),
        new("grid-names", "takeoff grid-names <model.e2k> <sheet.dxf> [<sheet.dxf> ...]", "The axis names a sheet carries beside the names the model's GRIDS carry: which the model names, which it does not - the first question when a sheet could not be set on the grid by name."),
        new("model-to-page", "takeoff model-to-page <model.e2k> <sheet.dxf> <x> <y> [<x> <y> ...]", "A model point carried back to the sheet by the shared grid names (median shift, disagreeing axes listed) and, where the DXF banks its page origin, to page millimetres for pdf-overlay --mark."),
        new("corpus-analyze", "takeoff corpus-analyze [<projectsRoot>] [--work <dir>] [--jobs a,b] [--parallel N] [--force] [--rules-db <conn>]", "The whole corpus through the one ingestion point: every job's current stick file mirrored once and built as the verbs build one, one row per set and per sheet into analysis.IntakeSet / IntakeSheet (migration 083) and CSV beside the work; then X of Y build, and why the rest do not."),
        new("corpus-query", "takeoff corpus-query summary|no-model|plan-titles|set <job>...|yardsticks [--ledger <dir>]", "Questions to the corpus ledger, no PDF opened: the population in one table, every set without a model by its reason, how plan sheets name their storeys, one set's sheets, the yardsticks worst first."),
        new("corpus-census", "takeoff corpus-census [<projectsRoot>] [--out census.csv] [--parallel N]", "Every job on the projects share and what it holds for the intake to learn from: dated structural stick files, architects' sets, the engineer's ETABS models; X of Y, one row per job. Read-only, bounded listings."),
        new("pdf-levels", "takeoff pdf-levels <stickfile.pdf> [levels.csv] [--plans <dxfDir>]", "The drawings' storeys as a levels file (level, elevation mm from the lowest stated level), read off the wall elevations — what dxf-to-etabs takes in place of a reference .e2k for a job nobody has modelled."),
        new("pdf-assemblies", "takeoff pdf-assemblies <set.pdf> [assemblies.csv]", "Every wall and floor type card on the set's schedule sheets, read whole: code, name, each layer, F.R.R., S.T.C., references, remarks — and the material and thickness the model takes from them."),
        new("storeys-check", "takeoff storeys-check <stickfile.pdf> <model.e2k>", "The drawings' storey heights (wall elevations) against the model's, pair by pair; the publish reports the same line when --stick-file is given."),
        new("wallconcrete", "takeoff wallconcrete <keyplan.png> <schedule.png> <levels.json>", "Price core wall concrete from key plan and schedule."),
        new("single", "takeoff single <schedule.csv> <out.xlsx> [wbs] [name] [issue] [imperial]", "Generate an absolute takeoff workbook from one schedule CSV."),
        new("overlay", "takeoff overlay <before.pdf> <after.pdf> <out.pdf> [name] [beforeLabel] [afterLabel] [imperial]", "Generate visual rebar markup between two PDFs."),
        new("rebar", "takeoff rebar <before.pdf> <after.pdf> <out.xlsx> [name] [beforeLabel] [afterLabel]", "Generate a rebar change report between two PDFs."),
    ];

    public static bool IsHelpRequest(string[] args) =>
        args.Length == 0
        || (args.Length == 1 && IsHelpToken(args[0]));

    private static bool IsHelpToken(string token) =>
        string.Equals(token, "--help", StringComparison.OrdinalIgnoreCase)
        || string.Equals(token, "-h", StringComparison.OrdinalIgnoreCase);

    public static void WriteTo(TextWriter writer)
    {
        writer.WriteLine("Usage:");
        writer.WriteLine("  takeoff <command> [arguments]");
        writer.WriteLine("  takeoff <before.csv> <after.csv> <out-basepath> [wbs1] [name] [beforeLabel] [afterLabel]");
        writer.WriteLine();
        writer.WriteLine("Commands:");
        foreach (var command in Commands)
        {
            writer.WriteLine($"  {command.Name,-18} {command.Description}");
            writer.WriteLine($"  {"",-18} {command.Usage}");
        }
    }
}
