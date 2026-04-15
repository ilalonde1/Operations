#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Kor.Operations.Services;
using Kor.Operations.Services.AiTools;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// AI bar wiring for the PDF-to-SAFE window. Registers the window as an
    /// <see cref="IAiContextProvider"/>, constructs the tool dispatcher, and
    /// initialises the shared <see cref="Controls.AiQueryPanel"/> in tool-use
    /// mode. Called from the window's Loaded event so the XAML-generated
    /// control field (<c>AiChatPanel</c>) is available.
    /// </summary>
    public partial class PdfToSafeWindow
    {
        private bool _aiBarInitialized;
        private AppAiService? _appAiService;

        private const string AiSystemPrompt =
            "You are an expert structural engineering CAD technician assisting KOR Structural " +
            "(Vancouver, BC). The user has loaded a Bluebeam-marked-up PDF into the KOR NewerForma " +
            "PDF-to-SAFE import tool and will converse with you in plain English to prepare the " +
            "model for export to CSI SAFE, ETABS, or AutoCAD.\n\n" +
            "You have tools that mutate the state of the tool directly: change per-colour or " +
            "per-element types, set slab properties (thickness, grade, SDL, live), toggle " +
            "inclusion, adjust export settings, and trigger the export itself. Prefer tool calls " +
            "over advisory prose — the user wants action. After tool calls, respond with one " +
            "crisp sentence describing what you did.\n\n" +
            "The current extraction state is provided in the CURRENT STATE block on every turn. " +
            "Shape indices in that state match the indices you pass to tools. Never invent " +
            "shapes, indices, or colours that aren't listed. If the user asks for something " +
            "impossible with the current data, explain why in one sentence.\n\n" +
            "Structural conventions — follow these literally when classifying burgundy/\n" +
            "filled Bluebeam shapes:\n" +
            "  • Small roughly-square filled shape (both sides ≤ 1500mm AND aspect ≤ 2.5)\n" +
            "    → 'Column'. Example: 368 x 952mm perimeter column.\n" +
            "  • Elongated or large filled shape (any side > 2000mm OR aspect > 2.5)\n" +
            "    → 'Wall'. The polygon is reduced to a centerline beam element whose\n" +
            "    cross-section comes from the polygon's minor bbox dimension (wall\n" +
            "    thickness) × 1000mm. NEVER use 'Column' for wall-shaped polygons — SAFE\n" +
            "    renders an oversized rectangular column section as an unselectable\n" +
            "    sliver. Examples from past exports: 157 x 5000mm, 1850 x 9737mm,\n" +
            "    952 x 9732mm, 2832 x 9732mm are all walls.\n" +
            "  • 'Beam' is for thin elongated shapes that represent ACTUAL beam members\n" +
            "    (typically with a text annotation like '300x600' nearby). Most \n" +
            "    structural walls should be 'Wall', not 'Beam'.\n" +
            "  • Large closed outlines (bigger than the elements sitting inside them) =\n" +
            "    slab perimeter. Set the colour to 'Slab'; chain assembly joins edge\n" +
            "    segments into closed polygons.\n" +
            "  • Elevator / stair / MEP cores inside a slab = 'Opening' on the floor\n" +
            "    slab.\n" +
            "  • Default concrete is C30 (Vancouver). CSA A23.3-19 with NBC combos is\n" +
            "    the default code. Default slab thickness 250mm unless a text\n" +
            "    annotation says otherwise.\n";

        private void InitializeAiBar()
        {
            if (_aiBarInitialized) return;
            _aiBarInitialized = true;

            try
            {
                _appAiService = AppServices.Get<AppAiService>();
                if (_appAiService is null || !_appAiService.IsConfigured)
                    return;

                var contextBuilder = AppServices.Get<AppAiContextBuilder>();
                contextBuilder.Register(this);

                AiChatPanel.InitializeWithTools(
                    _appAiService,
                    this,
                    PdfToSafeAiTools.All,
                    AiToolDispatchAsync,
                    AiSystemPrompt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialise PdfToSafe AI bar.");
            }
        }

        /// <summary>
        /// Routes a tool call from Claude to the matching handler on the UI thread.
        /// Returns a short human-readable string fed back to Claude as the tool result.
        /// Unknown / unwired tools return a controlled error so Claude can self-correct.
        /// </summary>
        private async Task<string> AiToolDispatchAsync(string toolName, JsonElement input, CancellationToken ct)
        {
            // Export tools do I/O and need to remain async end-to-end. Route them
            // directly; they internally marshal UI updates via the dispatcher.
            try
            {
                switch (toolName)
                {
                    case PdfToSafeAiTools.ExportF2k:
                        return await HandleExportAsync(input, "f2k", DoExportF2kAsync);
                    case PdfToSafeAiTools.ExportE2k:
                        return await HandleExportAsync(input, "e2k", DoExportE2kAsync);
                    case PdfToSafeAiTools.ExportDxf:
                        return await HandleExportAsync(input, "dxf", DoExportDxfAsync);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI export handler threw for {Tool}", toolName);
                return $"Tool '{toolName}' threw: {ex.Message}";
            }

            // All other tools are quick state mutations — run on the UI thread.
            var tcs = new TaskCompletionSource<string>();
            await Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    string result = toolName switch
                    {
                        PdfToSafeAiTools.SetColorType       => HandleSetColorType(input),
                        PdfToSafeAiTools.SetColorProperties => HandleSetColorProperties(input),
                        PdfToSafeAiTools.SetElementType     => HandleSetElementType(input),
                        PdfToSafeAiTools.SetElementExcluded => HandleSetElementExcluded(input),
                        PdfToSafeAiTools.ClearAllOverrides  => HandleClearAllOverrides(input),
                        PdfToSafeAiTools.SetExportSettings  => HandleSetExportSettings(input),
                        _ => $"Tool '{toolName}' is recognised but not yet wired in this build."
                    };
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "AI tool handler threw for {Tool}", toolName);
                    tcs.SetResult($"Tool '{toolName}' threw: {ex.Message}");
                }
            }, DispatcherPriority.Normal);
            return await tcs.Task;
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static readonly string[] ValidElementTypes =
            { "Slab", "Beam", "Wall", "Column", "Ignore", "Opening" };

        private static (byte R, byte G, byte B)? TryParseColorHex(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            var trimmed = hex.Trim().TrimStart('#');
            if (trimmed.Length != 6) return null;
            try
            {
                return ((byte)Convert.ToInt32(trimmed.Substring(0, 2), 16),
                        (byte)Convert.ToInt32(trimmed.Substring(2, 2), 16),
                        (byte)Convert.ToInt32(trimmed.Substring(4, 2), 16));
            }
            catch { return null; }
        }

        private SlabPropsRow? FindColorRow((byte R, byte G, byte B) color) =>
            _slabPropsRows.FirstOrDefault(r => r.Color == color);

        private static string? TryGetString(JsonElement obj, string name) =>
            obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString() : null;

        private static double? TryGetDouble(JsonElement obj, string name) =>
            obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number
                ? el.GetDouble() : null;

        private static int? TryGetInt(JsonElement obj, string name) =>
            obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number
                ? el.GetInt32() : null;

        private static bool? TryGetBool(JsonElement obj, string name)
        {
            if (!obj.TryGetProperty(name, out var el)) return null;
            if (el.ValueKind == JsonValueKind.True) return true;
            if (el.ValueKind == JsonValueKind.False) return false;
            return null;
        }

        // ── Handlers ───────────────────────────────────────────────────────

        private string HandleSetColorType(JsonElement input)
        {
            var hex = TryGetString(input, "colorHex");
            var type = TryGetString(input, "type");

            var rgb = TryParseColorHex(hex);
            if (rgb is null) return $"Invalid colorHex '{hex}'. Use a 6-digit RGB hex like '800000'.";
            if (type is null || !ValidElementTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
                return $"Invalid type '{type}'. Must be one of {string.Join(", ", ValidElementTypes)}.";

            var row = FindColorRow(rgb.Value);
            if (row is null) return $"No shape has colour #{hex}. Check the CURRENT STATE shape list.";

            // Canonicalise casing to match the dropdown enum ("Slab" not "slab").
            string canonical = ValidElementTypes.First(v => v.Equals(type, StringComparison.OrdinalIgnoreCase));
            row.ElementType = canonical;
            row.Included = !IsExcludedType(canonical);
            RebuildExcludedColors();
            DrawOverlay();
            UpdateExportState();
            return $"Set colour #{hex} default type to {canonical}.";
        }

        private string HandleSetColorProperties(JsonElement input)
        {
            var hex = TryGetString(input, "colorHex");
            var rgb = TryParseColorHex(hex);
            if (rgb is null) return $"Invalid colorHex '{hex}'.";

            var row = FindColorRow(rgb.Value);
            if (row is null) return $"No shape has colour #{hex}.";

            var changes = new List<string>();

            var thickness = TryGetDouble(input, "thicknessMm");
            if (thickness is { } t && t > 0)
            {
                row.ThicknessMm = t;
                row.AutoThicknessMm = null; // user/AI override — stop auto-applying hints
                changes.Add($"thickness={t}mm");
            }

            var grade = TryGetString(input, "gradeCode");
            if (!string.IsNullOrWhiteSpace(grade))
            {
                // Accept only grades we ship support for.
                var known = StructuralMaterialDatabase.SupportedGrades;
                string? match = null;
                foreach (var g in known)
                    if (string.Equals(g, grade, StringComparison.OrdinalIgnoreCase))
                    { match = g; break; }
                if (match is not null)
                {
                    row.GradeCode = match;
                    changes.Add($"grade={match}");
                }
                else
                {
                    changes.Add($"grade='{grade}' (unknown — kept previous '{row.GradeCode}')");
                }
            }

            var sdl = TryGetDouble(input, "sdlKPa");
            if (sdl is { } s && s >= 0)
            {
                row.SdlKPa = s;
                changes.Add($"sdl={s}kPa");
            }

            var live = TryGetDouble(input, "liveKPa");
            if (live is { } l && l >= 0)
            {
                row.LiveKPa = l;
                changes.Add($"live={l}kPa");
            }

            if (changes.Count == 0)
                return $"No valid property values provided for colour #{hex}.";

            DrawOverlay();
            return $"Updated colour #{hex}: {string.Join(", ", changes)}.";
        }

        private string HandleSetElementType(JsonElement input)
        {
            var kind = TryGetString(input, "kind")?.ToLowerInvariant();
            var index = TryGetInt(input, "index");
            var type = TryGetString(input, "type");

            if (kind is not ("slab" or "line" or "column"))
                return $"Invalid kind '{kind}'. Use slab, line, or column.";
            if (index is null || index < 0)
                return $"Invalid or missing index '{index}'.";
            if (type is null || !ValidElementTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
                return $"Invalid type '{type}'.";

            int count = kind switch
            {
                "slab"   => _extractedGeometry?.Slabs.Count ?? 0,
                "line"   => _extractedGeometry?.Lines.Count ?? 0,
                "column" => _extractedGeometry?.Columns.Count ?? 0,
                _ => 0
            };
            if (index >= count)
                return $"Index {index} out of range for {kind} (only {count} item(s) available).";

            Dictionary<int, string>? dict = kind switch
            {
                "slab"   => _excl.SlabTypeOverrides,
                "line"   => _excl.LineTypeOverrides,
                "column" => _excl.ColumnTypeOverrides,
                _ => null
            };
            if (dict is null) return "Internal error resolving override dictionary.";

            dict[index.Value] = type!;
            DrawOverlay();
            return $"Set {kind}[{index}] type override to {type}.";
        }

        private string HandleSetElementExcluded(JsonElement input)
        {
            var kind = TryGetString(input, "kind")?.ToLowerInvariant();
            var index = TryGetInt(input, "index");
            var excluded = TryGetBool(input, "excluded");

            if (kind is not ("slab" or "line" or "column"))
                return $"Invalid kind '{kind}'.";
            if (index is null || index < 0) return "Missing or invalid index.";
            if (excluded is null) return "Missing excluded flag.";

            HashSet<int>? set = kind switch
            {
                "slab"   => _excl.Slabs,
                "line"   => _excl.Lines,
                "column" => _excl.Columns,
                _ => null
            };
            if (set is null) return "Internal error resolving exclusion set.";

            if (excluded.Value) set.Add(index.Value);
            else set.Remove(index.Value);

            DrawOverlay();
            UpdateExportState();
            return $"{kind}[{index}] is now {(excluded.Value ? "excluded" : "included")}.";
        }

        private string HandleClearAllOverrides(JsonElement _)
        {
            _excl.Clear();
            foreach (var row in _slabPropsRows)
            {
                row.ElementType = row.DefaultElementType;
                row.Included = !IsExcludedType(row.DefaultElementType);
                row.AutoThicknessMm = null;
            }
            RebuildExcludedColors();
            DrawOverlay();
            UpdateExportState();
            return "Cleared all per-element and per-colour overrides; types reset to auto-detected defaults.";
        }

        private static readonly string[] ValidDesignCodes =
            { "None", "CSA_A23_3_19", "ACI_318_19", "AS_3600_09", "EC2_2004", "NZS_3101_06" };

        private static readonly string[] ValidLoadCombCodes =
            { "", "NBC", "ASCE7", "EC0", "AS/NZS" };

        private string HandleSetExportSettings(JsonElement input)
        {
            var changes = new List<string>();

            var designCode = TryGetString(input, "designCode");
            if (designCode is not null)
            {
                if (!ValidDesignCodes.Contains(designCode, StringComparer.OrdinalIgnoreCase))
                    return $"Invalid designCode '{designCode}'. Allowed: {string.Join(", ", ValidDesignCodes)}.";
                if (Enum.TryParse<DesignCodeOption>(designCode, ignoreCase: true, out var dc))
                {
                    _exportSettings.DesignCode = dc;
                    changes.Add($"designCode={dc}");
                }
            }

            var loadCombCode = TryGetString(input, "loadCombCode");
            if (loadCombCode is not null)
            {
                if (!ValidLoadCombCodes.Contains(loadCombCode, StringComparer.OrdinalIgnoreCase))
                    return $"Invalid loadCombCode '{loadCombCode}'. Allowed: '', NBC, ASCE7, EC0, AS/NZS.";
                _exportSettings.LoadCombCode = loadCombCode;
                changes.Add($"loadCombCode='{loadCombCode}'");
            }

            var meshSize = TryGetDouble(input, "meshSizeMm");
            if (meshSize is { } m && m > 0)
            {
                _exportSettings.MeshSizeMm = m;
                changes.Add($"meshSizeMm={m}");
            }

            var autoStrips = TryGetBool(input, "autoGenerateStrips");
            if (autoStrips is { } a)
            {
                _exportSettings.AutoGenerateStrips = a;
                changes.Add($"autoGenerateStrips={a}");
            }

            var stripSpacing = TryGetDouble(input, "stripSpacingMm");
            if (stripSpacing is { } ss && ss > 0)
            {
                _exportSettings.StripSpacingMm = ss;
                changes.Add($"stripSpacingMm={ss}");
            }

            var includePt = TryGetBool(input, "includePtLoads");
            if (includePt is { } pt)
            {
                _exportSettings.IncludePtLoads = pt;
                changes.Add($"includePtLoads={pt}");
            }

            var memMod = TryGetDouble(input, "slabMembraneModifier");
            if (memMod is { } mm && mm >= 0)
            {
                _exportSettings.SlabMembraneModifier = mm;
                changes.Add($"slabMembraneModifier={mm}");
            }

            var bendMod = TryGetDouble(input, "slabBendingModifier");
            if (bendMod is { } bm && bm >= 0)
            {
                _exportSettings.SlabBendingModifier = bm;
                changes.Add($"slabBendingModifier={bm}");
            }

            var shearMod = TryGetDouble(input, "slabShearModifier");
            if (shearMod is { } sm && sm >= 0)
            {
                _exportSettings.SlabShearModifier = sm;
                changes.Add($"slabShearModifier={sm}");
            }

            if (changes.Count == 0)
                return "No valid export-settings fields provided.";
            return $"Updated export settings: {string.Join(", ", changes)}.";
        }

        // ── Vision auto-classification on PDF load ─────────────────────────

        private const string VisionSeedInstruction =
            "You are looking at a rendered Bluebeam-marked-up structural PDF. " +
            "The user just opened it and the extractor has classified every shape " +
            "into Slabs / Lines / Columns and assigned each colour a default type " +
            "via set_color_type (see CURRENT STATE). Review the rendering and the " +
            "extraction summary, then call set_color_type / set_element_type / " +
            "set_color_properties tools to correct any obvious misclassifications. " +
            "Do NOT call any export tool — leave that to the user. " +
            "End with one short sentence summarising what you adjusted (or 'No changes " +
            "— extraction looks correct.' if nothing needed changing).";

        private bool _visionAutoRunning;

        private async Task TryVisionAutoClassifyAsync()
        {
            // Guardrails — run only once at a time and only when we have
            // something to analyse and a configured AI service.
            if (_visionAutoRunning) return;
            if (!_aiBarInitialized || _appAiService is null || !_appAiService.IsConfigured) return;
            if (_extractedGeometry is null || !_extractedGeometry.IsVectorPdf) return;
            if (_renderedBitmap is null) return;
            if (_slabPropsRows.Count == 0) return;

            _visionAutoRunning = true;
            try
            {
                byte[] pngBytes;
                try
                {
                    pngBytes = EncodeBitmapToPng(_renderedBitmap);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Vision auto-classify: PNG encode failed.");
                    return;
                }

                // Surface an inline spinner the user can actually see, rather
                // than leaving them wondering if anything is happening while
                // Claude Vision takes ~10-60s on a complex PDF.
                await Dispatcher.InvokeAsync(() =>
                {
                    SetStatus("Reviewing the extraction with Claude Vision…",
                        "#E8EAF6", "#3949AB");
                    SetStatusBusy("AI analysis in progress…");
                });

                var conversation = new List<(string Role, string Content)>
                {
                    ("user", VisionSeedInstruction)
                };

                var result = await _appAiService.AskWithToolsAsync(
                    conversation,
                    PdfToSafeAiTools.All,
                    AiToolDispatchAsync,
                    AiSystemPrompt,
                    maxIterations: 8,
                    ct: CancellationToken.None,
                    firstMessageImagePng: pngBytes);

                await Dispatcher.InvokeAsync(() =>
                {
                    if (result.ToolCallsExecuted > 0)
                    {
                        var summary = string.IsNullOrWhiteSpace(result.Text)
                            ? $"AI auto-classified: {result.ToolCallsExecuted} adjustment(s)."
                            : result.Text;
                        SetStatus(summary, "#E8EAF6", "#3949AB");
                    }
                    else if (!string.IsNullOrWhiteSpace(result.Text))
                    {
                        SetStatus(result.Text, "#E8F5E9", "#2E7D32");
                    }
                    else
                    {
                        // Nothing changed and no text — drop the spinner but
                        // keep whatever previous status was up.
                        ClearStatusBusy();
                    }
                });

                _logger.LogInformation(
                    "Vision auto-classify complete: {Calls} tool call(s).",
                    result.ToolCallsExecuted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Vision auto-classify failed.");
                await Dispatcher.InvokeAsync(ClearStatusBusy);
            }
            finally
            {
                _visionAutoRunning = false;
            }
        }

        private static byte[] EncodeBitmapToPng(BitmapSource bitmap)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// Shared export-tool handler. Validates the requested output path,
        /// enforces a sane extension, then delegates to the supplied
        /// <paramref name="doExport"/> (which already marshals UI updates and
        /// runs the file write off the UI thread). Returns a confirmation or
        /// a structured error for Claude.
        /// </summary>
        private async Task<string> HandleExportAsync(
            JsonElement input,
            string expectedExtension,
            Func<string, Task<string>> doExport)
        {
            var path = TryGetString(input, "outputPath");
            if (string.IsNullOrWhiteSpace(path))
                return "Missing outputPath.";

            // Normalise and validate the path before hitting the exporter.
            string fullPath;
            try { fullPath = Path.GetFullPath(path); }
            catch (Exception ex) { return $"Invalid outputPath '{path}': {ex.Message}"; }

            // Be forgiving on extension: warn but still write.
            string actualExt = Path.GetExtension(fullPath).TrimStart('.').ToLowerInvariant();
            string suffix = actualExt == expectedExtension
                ? ""
                : $" (note: extension was '.{actualExt}', expected '.{expectedExtension}')";

            string? dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                return $"Directory does not exist: {dir}";

            // Snapshot readiness on the UI thread before touching the exporter.
            bool ready = false;
            await Dispatcher.InvokeAsync(() =>
            {
                ready = _extractedGeometry is not null && _extractedGeometry.IsVectorPdf;
            });
            if (!ready) return "No PDF is loaded or geometry extraction is empty.";

            var error = await doExport(fullPath);
            return string.IsNullOrEmpty(error)
                ? $"Exported to {fullPath}{suffix}."
                : $"Export failed: {error}";
        }
    }
}
