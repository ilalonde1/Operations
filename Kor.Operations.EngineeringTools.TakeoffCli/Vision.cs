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
    public static async Task<string> ReadSheetJsonAsync(byte[] pngBytes)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("KOR_ANTHROPIC_KEY is not set — vision layer needs an Anthropic key.");

        string b64 = Convert.ToBase64String(pngBytes);
        var request = new
        {
            model = Model,
            max_tokens = 2048,
            temperature = 0,   // pin output: the same sheet must read the same way run-to-run
            tools = new object[] { ReportSheetTool() },
            tool_choice = new { type = "tool", name = "report_sheet" },
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "image", source = new { type = "base64", media_type = "image/png", data = b64 } },
                        new { type = "text", text = Prompt }
                    }
                }
            }
        };
        string body = JsonSerializer.Serialize(request);

        for (int attempt = 0; ; attempt++)
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
    }

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

    private const string Prompt = @"You are a senior structural estimator reading ONE sheet from a concrete building's structural drawing set (a 'stickfile'). Report what you see with the report_sheet tool.

- kind: 'Framing' for a suspended-floor / 'CONCRETE OUTLINE' plan; 'Foundation' for a mat / footing / parkade slab-on-grade plan; 'Schedule' for column/wall schedules; 'Detail' for sections/details; otherwise 'Other'.
- scaleNote: the title-block drawing scale, exactly as written (e.g. 1/8""=1'-0"").
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
