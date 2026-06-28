#nullable enable

using System.Net.Http;
using System.Text;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

// Vision Layer 2: sends a rasterized plan sheet to Claude and gets back a structured reading
// (sheet kind, scale, and the concrete-outline plates with normalized boxes + thickness). Reuses
// the firm's existing Anthropic path: KOR_ANTHROPIC_KEY, /v1/messages, claude-sonnet-4-6, a forced
// tool call for guaranteed JSON. The JSON is parsed by Core's PlanVisionParser; the deterministic
// geometry then measures the located plates, so vision never has to be pixel-accurate.
static class PlanVisionClient
{
    private const string Endpoint = "https://api.anthropic.com/v1/messages";
    private const string Model = "claude-sonnet-4-6";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    public static string? ApiKey =>
        Environment.GetEnvironmentVariable("KOR_ANTHROPIC_KEY")
        ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

    /// <summary>Sends the page PNG, forces the report_sheet tool, returns its raw input JSON.</summary>
    public static Task<string> ReadSheetJsonAsync(byte[] pngBytes) =>
        PostForcedToolAsync(ReportSheetTool(), "report_sheet", Prompt, pngBytes, 2048);

    /// <summary>Reads a SHEAR WALL SCHEDULE sheet — wall thickness per mark per level range.</summary>
    public static Task<string> ReadWallScheduleJsonAsync(byte[] pngBytes) =>
        PostForcedToolAsync(WallScheduleTool(), "report_wall_schedule", WallSchedulePrompt, pngBytes, 8000);

    /// <summary>Reads a CORE WALL KEY PLAN — each wall mark's centreline length.</summary>
    public static Task<string> ReadWallKeyPlanJsonAsync(byte[] pngBytes) =>
        PostForcedToolAsync(WallKeyPlanTool(), "report_wall_key_plan", WallKeyPlanPrompt, pngBytes, 3072);

    /// <summary>Reads a COLUMN SCHEDULE — each column mark's W×D size per level band.</summary>
    public static Task<string> ReadColumnScheduleJsonAsync(byte[] pngBytes) =>
        PostForcedToolAsync(ColumnScheduleTool(), "report_column_schedule", ColumnSchedulePrompt, pngBytes, 8000);

    /// <summary>Posts the PNG with a forced tool call and returns that tool's raw input JSON.</summary>
    private static async Task<string> PostForcedToolAsync(object tool, string toolName, string prompt, byte[] pngBytes, int maxTokens)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("KOR_ANTHROPIC_KEY is not set — vision layer needs an Anthropic key.");

        string b64 = Convert.ToBase64String(pngBytes);
        var request = new
        {
            model = Model,
            max_tokens = maxTokens,
            temperature = 0,   // pin output: the same sheet must read the same way run-to-run
            tools = new object[] { tool },
            tool_choice = new { type = "tool", name = toolName },
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "image", source = new { type = "base64", media_type = "image/png", data = b64 } },
                        new { type = "text", text = prompt }
                    }
                }
            }
        };
        return await SendWithRetryAsync(JsonSerializer.Serialize(request));
    }

    // Text-only forced-tool POST (no image) — Layer-2 synthesis reasons over the EXACT extracted digest.
    private static async Task<string> PostForcedToolTextAsync(object tool, string toolName, string prompt, int maxTokens)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("KOR_ANTHROPIC_KEY is not set — synthesis needs an Anthropic key.");

        var request = new
        {
            model = Model,
            max_tokens = maxTokens,
            temperature = 0,
            tools = new object[] { tool },
            tool_choice = new { type = "tool", name = toolName },
            messages = new object[]
            {
                new { role = "user", content = new object[] { new { type = "text", text = prompt } } }
            }
        };
        return await SendWithRetryAsync(JsonSerializer.Serialize(request));
    }

    // Shared POST + retry (429/5xx + transport drops) for both the image and text forced-tool calls.
    private static async Task<string> SendWithRetryAsync(string body)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint);
                req.Headers.Add("x-api-key", ApiKey);
                req.Headers.Add("anthropic-version", "2023-06-01");
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");

                using var resp = await Http.SendAsync(req);
                string respText = await resp.Content.ReadAsStringAsync();
                int code = (int)resp.StatusCode;
                if ((code == 429 || code >= 500) && attempt < 4)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)));
                    continue;
                }
                if (!resp.IsSuccessStatusCode)
                    throw new HttpRequestException($"Anthropic {code}: {Truncate(respText, 500)}");
                return ExtractToolInput(respText);
            }
            // A transport-level drop (connection reset, TLS write failure, timeout) is thrown before any
            // response and so isn't caught by the status-code check above — retry it like a 5xx.
            catch (Exception ex) when (attempt < 4 && ex is HttpRequestException or IOException or System.Net.Sockets.SocketException or TaskCanceledException)
            {
                await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)));
            }
        }
    }

    // ── Layer 2: estimator-synthesis over the exact digest (general, firm-agnostic) ──────────────
    public static Task<string> SynthesizePageAsync(string digestJson) =>
        PostForcedToolTextAsync(PageTakeoffTool(), "report_page_takeoff",
            PageTakeoffPrompt + "\n\n=== SHEET DIGEST (exact vector facts) ===\n" + digestJson, 1500);

    // Image-fused synthesis: the rendered page lets Claude judge the floor-plate plan AREA (which is
    // NOT recoverable from the closed-region geometry); the digest supplies every exact text/number.
    public static Task<string> SynthesizePageWithImageAsync(string digestJson, byte[] png) =>
        PostForcedToolAsync(PageTakeoffTool(), "report_page_takeoff",
            PageTakeoffPrompt +
            "\n\nYou ALSO have the rendered page image. Use the IMAGE only to judge the floor-plate plan " +
            "AREA in square feet (slabAreaSqFt) from its visible extents and the drawing scale — read the " +
            "overall building dimensions / grid extents. Use the exact DIGEST for ALL text and numbers " +
            "(slab thickness, schedules, level, strengths). If it is not a floor/foundation plan, leave " +
            "slabAreaSqFt null.\n\n=== SHEET DIGEST (exact vector facts) ===\n" + digestJson, 1800);

    private const string PageTakeoffPrompt =
@"You are a senior structural quantity-takeoff estimator reading ONE sheet of a structural drawing set.
The content below was extracted EXACTLY from the vector PDF — text lines (no OCR) plus geometry — it is
not an image. Dense sheets often have jumbled/interleaved text lines; read through that with judgment.
Classify the sheet and extract its takeoff-relevant facts. Report ONLY what the sheet actually states;
never invent a number — if a value is absent, leave it null and add a flag. Generalize across drawing
conventions from any firm; do not assume one labelling style.";

    private static object PageTakeoffTool() => new
    {
        name = "report_page_takeoff",
        description = "Classify one structural sheet and extract its takeoff-relevant facts.",
        input_schema = new
        {
            type = "object",
            properties = new
            {
                sheetKind = new { type = "string", @enum = new[] { "floor_plan", "foundation_plan", "schedule", "section", "detail", "notes", "cover", "other" } },
                level = new { type = new[] { "string", "null" }, description = "the building level/floor this sheet represents, as labelled (e.g. LEVEL P4, L12); null if not a single-level plan" },
                slabThicknessIn = new { type = new[] { "number", "null" }, description = "nominal suspended slab thickness in inches if stated" },
                slabAreaSqFt = new { type = new[] { "number", "null" }, description = "approximate suspended-slab plan AREA for this level in square feet, reasoned from the geometry regions (areas given in PDF points^2) and the drawing scale; null if not a floor/foundation plan. Note 1 PDF point = 1/72 inch on the page; at 1/8\"=1'-0\" one page-inch = 8 feet." },
                columnCount = new { type = new[] { "integer", "null" }, description = "number of columns visible on this plan, if countable; else null" },
                hasColumnSchedule = new { type = "boolean" },
                hasWallSchedule = new { type = "boolean" },
                hasFootingSchedule = new { type = "boolean" },
                concreteStrengthMpa = new { type = new[] { "number", "null" } },
                confidence = new { type = "number", description = "0..1" },
                flags = new { type = "array", items = new { type = "string" }, description = "uncertainties, missing values, transfer/podium slabs, anything needing a human" }
            },
            required = new[] { "sheetKind", "hasColumnSchedule", "hasWallSchedule", "hasFootingSchedule", "confidence", "flags" }
        }
    };

    private static string ExtractToolInput(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var rootEl = doc.RootElement;

        // A max_tokens stop means the tool-input JSON is cut off mid-object; surface that explicitly
        // rather than letting a partial, unparseable blob fall through to the JSON parser downstream.
        if (rootEl.TryGetProperty("stop_reason", out var sr) && sr.GetString() == "max_tokens")
            throw new InvalidOperationException(
                "Vision response truncated (stop_reason=max_tokens) — sheet has more plates than the token budget; raise max_tokens.");

        if (rootEl.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            foreach (var block in content.EnumerateArray())
                if (block.TryGetProperty("type", out var t) && t.GetString() == "tool_use"
                    && block.TryGetProperty("input", out var input))
                    return input.GetRawText();
        throw new InvalidOperationException($"No tool_use block in vision response: {Truncate(responseJson, 400)}");
    }

    private static object ReportSheetTool() => new
    {
        name = "report_sheet",
        description = "Report the structural-plan classification and every concrete-outline plate drawn on this sheet.",
        input_schema = new
        {
            type = "object",
            properties = new
            {
                kind = new { type = "string", @enum = new[] { "Framing", "Foundation", "Schedule", "Detail", "Other" } },
                scaleNote = new { type = new[] { "string", "null" }, description = "title-block scale exactly as written, e.g. 1/8\"=1'-0\"" },
                scheduleType = new { type = new[] { "string", "null" }, @enum = new[] { "WallSchedule", "ColumnSchedule", "FootingSchedule", "OtherSchedule", null }, description = "if this sheet contains a schedule table, which kind; else null" },
                hasWallKeyPlan = new { type = "boolean", description = "true if the sheet shows a CORE WALL KEY PLAN (a small core plan with wall segments labelled W1, Z3, …)" },
                plates = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            level = new { type = "string", description = "level label, e.g. L17-28" },
                            count = new { type = "integer", description = "physical floors this plate represents" },
                            element = new { type = "string", @enum = new[] { "Slab", "Wall", "Column", "Foundation", "Beam", "DropPanel" } },
                            variant = new { type = new[] { "string", "null" } },
                            thicknessIn = new { type = new[] { "number", "null" }, description = "slab/mat thickness in inches" },
                            box = new { type = "array", items = new { type = "number" }, minItems = 4, maxItems = 4, description = "normalized [x0,y0,x1,y1], 0..1, top-left origin" },
                            confidence = new { type = "number" }
                        },
                        required = new[] { "level", "count", "element", "box", "confidence" }
                    }
                }
            },
            required = new[] { "kind", "plates" }
        }
    };

    private static object WallScheduleTool() => new
    {
        name = "report_wall_schedule",
        description = "Report every wall-thickness cell of a shear/core wall schedule: the wall mark, the level, and the thickness in inches.",
        input_schema = new
        {
            type = "object",
            properties = new
            {
                entries = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            mark = new { type = "string", description = "wall mark from the column header, e.g. W1, W4, Z3, Z8A" },
                            levelTop = new { type = "string", description = "top level of the range this thickness covers, e.g. L18" },
                            levelBottom = new { type = "string", description = "bottom level of the range, e.g. L15 (same as levelTop for a single level)" },
                            thicknessIn = new { type = "number", description = "wall thickness in inches read from the cell's 'N\" WALL'" },
                            confidence = new { type = "number" }
                        },
                        required = new[] { "mark", "levelTop", "levelBottom", "thicknessIn" }
                    }
                }
            },
            required = new[] { "entries" }
        }
    };

    private static object WallKeyPlanTool() => new
    {
        name = "report_wall_key_plan",
        description = "Report each labelled shear-wall segment on a core wall key plan: its mark and centreline length in feet.",
        input_schema = new
        {
            type = "object",
            properties = new
            {
                marks = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            mark = new { type = "string", description = "wall mark labelling the segment, e.g. W1, Z3, Z8A" },
                            lengthFt = new { type = "number", description = "centreline length of that wall segment in feet" },
                            box = new { type = "array", items = new { type = "number" }, minItems = 4, maxItems = 4, description = "normalized [x0,y0,x1,y1] of the segment" },
                            confidence = new { type = "number" }
                        },
                        required = new[] { "mark", "lengthFt" }
                    }
                }
            },
            required = new[] { "marks" }
        }
    };

    private static object ColumnScheduleTool() => new
    {
        name = "report_column_schedule",
        description = "Report every column-size cell of a column schedule: the mark, the level range, and the width × depth in inches.",
        input_schema = new
        {
            type = "object",
            properties = new
            {
                entries = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            mark = new { type = "string", description = "column mark from the column header, e.g. C1, C12, PC1" },
                            levelTop = new { type = "string", description = "top level of the range this size covers" },
                            levelBottom = new { type = "string", description = "bottom level of the range (same as top for a single level)" },
                            widthIn = new { type = "number", description = "column width in inches (the first dimension)" },
                            depthIn = new { type = "number", description = "column depth in inches (the second dimension; equal to width if square)" },
                            confidence = new { type = "number" }
                        },
                        required = new[] { "mark", "levelTop", "levelBottom", "widthIn", "depthIn" }
                    }
                }
            },
            required = new[] { "entries" }
        }
    };

    private const string ColumnSchedulePrompt = @"You are a senior structural estimator reading a COLUMN SCHEDULE — a table whose ROWS are building levels and whose COLUMNS are column marks (C1, C2, … or PC1, PC2, …). Each non-empty cell gives that column's plan size at that level, e.g. '24""x24""', '600 x 600', '24""x16""', often with reinforcing below it.

A given column mark keeps ONE size over a RANGE of levels until it changes (merged cells). Report ONE entry per (mark, size) band with report_column_schedule:
   - mark: the column header (C1, PC3 …).
   - levelTop / levelBottom: the top and bottom row labels of the band this size covers (both the same for a single level).
   - widthIn / depthIn: the column's two plan dimensions in INCHES. Convert millimetres to inches if the schedule is metric (600 mm ≈ 24""). If the cell shows a single number it is square — set width = depth.
Do NOT emit a separate entry per level — collapse each merged size cell into one top→bottom band. IGNORE reinforcing-only text, ties, the title block, and any blank cell.";

    private const string WallSchedulePrompt = @"You are a senior structural estimator reading a SHEAR / CORE WALL SCHEDULE — a table whose ROWS are building levels (e.g. LEVEL 20 … LEVEL 01, P1 MEZZ, P1 … P7) and whose COLUMNS are wall marks (W1, W2, W3, W4, W5, Z1, Z2, Z3, Z3A, Z4, Z5, Z6, Z7, Z8, Z8A, and similar). Each non-empty WALL cell states a thickness like '30"" WALL', '12"" WALL', '6"" WALL', '36"" WALL', '42"" WALL', usually followed by reinforcing (e.g. '20M @ 6"" VERT. EACH FACE').

A given wall mark keeps ONE thickness over a RANGE of levels until it changes (the cells are merged vertically). Report ONE entry per such (mark, thickness) BAND with report_wall_schedule:
   - mark: the column header (W1, Z3, Z8A …).
   - levelTop / levelBottom: the top and bottom row labels of the band this thickness covers (e.g. levelTop 'L14', levelBottom 'L08'). For a band that is a single level, set both to that level.
   - thicknessIn: the wall thickness in inches from the band's leading 'N"" WALL' (30, 12, 6, 36, 42 …).
Do NOT emit a separate entry per level — collapse each merged thickness cell into one top→bottom band. This keeps the output compact.

IGNORE: the H1–H5 'HEADERS' columns (coupling/deep beams, not walls) UNLESS a cell there explicitly gives a wall thickness; cells that are only reinforcing notes or arrows with no 'N"" WALL'; the title block; any 'SEE DRAWING …' overlay text. If a cell is blank, omit it.";

    private const string WallKeyPlanPrompt = @"You are a senior structural estimator reading a CORE WALL KEY PLAN — a small plan of the building core where each shear-wall segment is drawn as a solid (poché) bar and labelled with a mark (W1, W2, W3, W4, W5, Z1, Z2, Z3, Z3A, Z4, Z5, Z6, Z7, Z8, Z8A) via a leader line.

For EACH labelled wall mark, report with report_wall_key_plan:
   - mark: the label.
   - lengthFt: the centreline LENGTH of that wall segment in FEET. Estimate it from the gridlines (the bubbles around the plan) and the plan scale — measure the long dimension of the poché bar, not its thickness.
   - box: a normalized [x0,y0,x1,y1] around that segment.
If the SAME mark labels two segments (e.g. a wall on each side of the core), report each occurrence separately. Report only the wall segments that carry a mark; ignore columns, dimensions, notes and the title block.";

    private const string Prompt = @"You are a senior structural estimator reading ONE sheet from a concrete building's structural drawing set (a 'stickfile'). Report what you see with the report_sheet tool.

- kind: 'Framing' for a suspended-floor / 'CONCRETE OUTLINE' plan; 'Foundation' for a mat / footing / parkade slab-on-grade plan; 'Schedule' for column/wall/footing schedule tables; 'Detail' for sections/details; otherwise 'Other'.
- scaleNote: the title-block drawing scale, exactly as written (e.g. 1/8""=1'-0"").
- scheduleType: if the sheet contains a SCHEDULE TABLE, classify it: 'WallSchedule' (a SHEAR/CORE WALL SCHEDULE — rows are levels, columns are wall marks W/Z giving 'N"" WALL'), 'ColumnSchedule' (rows levels, columns column marks C/PC giving sizes like 24""x24""), 'FootingSchedule' (footing marks F/SF with plan sizes and depths), else 'OtherSchedule'. If there is no schedule table, null.
- hasWallKeyPlan: true if the sheet shows a CORE WALL KEY PLAN — a small plan of the building core with each wall segment labelled by a mark (W1, W2, Z3, Z8A, …) on a leader. These often share a sheet with the wall schedule.
- For EACH concrete-outline plate on the sheet (framing sheets often show two side by side), add one plate:
   - level: the label from the plate's OWN PLAN TITLE, e.g. 'L17-28' from 'LEVEL 17 - 28 PLAN - CONCRETE OUTLINE'. Use the plan title, NOT a level range printed on a schedule header (a 'LEVEL P7 - L1 SHEAR WALL SCHEDULE' is a schedule that happens to serve many levels — it is NOT this sheet's level).
   - count: how many physical floors the PLAN TITLE itself stands for. A 'typical' plan whose title gives a level RANGE ('LEVEL 17 - 28 PLAN' = 12, 'LEVEL 4-12' = 9) repeats for that many floors. A single-level plan = 1. A FOUNDATIONS/FOOTINGS plan is the foundation built ONCE — count = 1 (never take a count from a schedule's level range like 'P7-L1').
   - element: 'Slab' for a suspended floor plate (incl. a parkade/podium suspended slab); 'Foundation' for a FOUNDATIONS/FOOTINGS plan (footing schedule F1/SF, core footings, pile caps, mat, or slab-on-grade).
   - thicknessIn: the dominant slab thickness in inches from the plan's 'N"" SLAB', 'N"" P/T SLAB', 'N"" S.O.G.' or 'N"" SLAB ON GRADE' callout. On a FOOTINGS/FOUNDATIONS plan there is usually NO single slab thickness (the concrete is in footings/core-footings of varying depth) — return null, do NOT report 0 or a footing depth as the slab thickness.
   - box: a normalized [x0,y0,x1,y1] bounding box (0..1, origin top-left) around JUST that plate's drawn concrete outline. Make it generous enough to contain the whole plate outline, but exclude the title block and the OTHER plate.
   - confidence: 0..1.

THICKENED ZONES (drop panels / thickened bands / built-up transfer zones) on a Framing / CONCRETE OUTLINE plate — local areas of the floor that are DEEPER than the nominal slab. For EACH such zone that carries an EXPLICIT total-thickness callout (e.g. 'DROP PANEL', '16"" THICK', '24"" DEEP DROP', a hatched thickened band noted '18"" SLAB' next to a '10"" SLAB' field), add a plate:
   - element 'DropPanel', count = the SAME count as the floor plate it sits on (a drop panel on a 'LEVEL 17-28' typical plan repeats for all 12 floors).
   - box tightly around just that thickened region; level = this plate's floor; thicknessIn = the zone's TOTAL thickness in inches (its full depth, NOT the depth above the slab); confidence reflecting how clearly the callout reads.
   - CRITICAL — do NOT report an elevation STEP as a thickening: a callout like '47""± STEP', '19""± STEP', a step in the slab top/bottom elevation, the dimension of a zone's width/length, or 'BUILT-UP PER ARCH' with no concrete thickness is NOT a slab thickness — ignore it. Only report a zone whose CONCRETE is explicitly deeper than the nominal slab, with a number of inches you can read.

FOUNDATIONS/FOOTINGS plan — the concrete is in footings of varying depth, so report SEVERAL plates, each element 'Foundation', count 1:
   - the SLAB-ON-GRADE as one plate: box around the whole floor outline, thicknessIn = the SOG thickness from the 'N"" SLAB ON GRADE' / 'N"" S.O.G.' note (null if none), level e.g. 'P1 SOG'.
   - EACH large deep/core footing or mat (the hatched regions labelled like '96"" DP. CORE FOOTING', '144"" DP. FOOTING', '36"" THICK FTG'): one plate, box tightly around that hatched footing, thicknessIn = its labelled DEPTH in inches, level e.g. 'P1 core ftg 96in'. Report the major footings (up to ~10 largest); do NOT report the many small spread footings from the F/SF schedule (handled elsewhere).

Only report concrete-outline floor/foundation plates. Ignore schedules, details, general notes, and the title block. A slab thicker than ~24"" is a transfer slab or mat — still report it with its real thickness.";

    private static string Truncate(string s, int n) => s.Length <= n ? s : s.Substring(0, n) + "…";
}
