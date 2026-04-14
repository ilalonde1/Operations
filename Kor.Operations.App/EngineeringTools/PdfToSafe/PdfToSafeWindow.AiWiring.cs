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
            "Structural conventions:\n" +
            "  • Small roughly-square filled annotations = columns.\n" +
            "  • Elongated filled annotations = walls / beam line elements.\n" +
            "  • Large closed outlines = slab perimeters (set to Slab; chain assembly joins edges).\n" +
            "  • Core / elevator / stair enclosures = typically Opening on the floor slab.\n" +
            "  • Default concrete is C30 (Vancouver). CSA A23.3-19 with NBC combos is the default code.\n";

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
            { "Slab", "Beam", "Column", "Ignore", "Opening" };

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

            // SelectionChanged handler wired on the ComboBox in BuildSlabPropsRows
            // automatically mirrors IncludeCheckBox and redraws — we just need
            // to rebuild the excluded-colour set and redraw afterwards.
            row.TypeComboBox.SelectedItem = type;
            RebuildExcludedColors();
            DrawOverlay();
            UpdateExportState();
            return $"Set colour #{hex} default type to {type}.";
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
                row.ThicknessTextBox.Text = t.ToString("0.###", CultureInfo.InvariantCulture);
                changes.Add($"thickness={t}mm");
            }

            var grade = TryGetString(input, "gradeCode");
            if (!string.IsNullOrWhiteSpace(grade))
            {
                // Only accept a grade the combo actually offers; otherwise the
                // SelectedItem setter silently no-ops and the user is confused.
                bool match = false;
                foreach (var item in row.GradeComboBox.Items)
                    if (string.Equals(item?.ToString(), grade, StringComparison.OrdinalIgnoreCase))
                    { row.GradeComboBox.SelectedItem = item; match = true; break; }
                if (match) changes.Add($"grade={grade}");
                else changes.Add($"grade='{grade}' (unknown — kept previous value)");
            }

            var sdl = TryGetDouble(input, "sdlKPa");
            if (sdl is { } s && s >= 0)
            {
                row.SdlTextBox.Text = s.ToString("0.###", CultureInfo.InvariantCulture);
                changes.Add($"sdl={s}kPa");
            }

            var live = TryGetDouble(input, "liveKPa");
            if (live is { } l && l >= 0)
            {
                row.LiveTextBox.Text = l.ToString("0.###", CultureInfo.InvariantCulture);
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
                row.TypeComboBox.SelectedItem = row.DefaultElementType;
                row.IncludeCheckBox.IsChecked = true;
            }
            RebuildExcludedColors();
            DrawOverlay();
            UpdateExportState();
            return "Cleared all per-element and per-colour overrides; types reset to auto-detected defaults.";
        }
    }
}
