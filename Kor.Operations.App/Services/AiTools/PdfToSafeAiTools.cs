#nullable enable
using System.Collections.Generic;
using System.Text.Json;

namespace Kor.Operations.Services.AiTools;

/// <summary>
/// Tool definitions exposed to Claude when the user converses with the AI bar
/// inside the PDF-to-SAFE import window. Schemas only — handler dispatch is
/// wired at the window level and passed to <see cref="AppAiService.AskWithToolsAsync"/>
/// via an <see cref="AiToolDispatcher"/>.
///
/// Every tool accepts a minimal payload. Callers normalise strings (trim, upper
/// hex) before mutating state. Unknown enum values must be rejected by the
/// handler with a clear error — Claude will see the error and self-correct.
/// </summary>
internal static class PdfToSafeAiTools
{
    static PdfToSafeAiTools()
    {
        // Schema sanity check — throws at first use if any tool's input schema is
        // malformed JSON. Better to fail fast in the window constructor than to
        // surface an obscure error inside the HTTP call to Claude.
        foreach (var tool in All)
            _ = JsonSerializer.Deserialize<JsonElement>(tool.InputSchemaJson);
    }

    internal const string SetColorType        = "set_color_type";
    internal const string SetColorProperties  = "set_color_properties";
    internal const string SetElementType      = "set_element_type";
    internal const string SetElementExcluded  = "set_element_excluded";
    internal const string ClearAllOverrides   = "clear_all_overrides";
    internal const string SetExportSettings   = "set_export_settings";
    internal const string ExportF2k           = "export_f2k";
    internal const string ExportE2k           = "export_e2k";
    internal const string ExportDxf           = "export_dxf";

    /// <summary>
    /// The authoritative tool list handed to Claude on every turn. Order is
    /// stable (keeps prompt caching effective once Anthropic supports it for
    /// tool-use).
    /// </summary>
    internal static readonly IReadOnlyList<AiTool> All = new[]
    {
        new AiTool(
            SetColorType,
            "Change the default element type for every shape that has a given colour. " +
            "The type applies to shapes that haven't been individually overridden via set_element_type. " +
            "'Ignore' removes the colour from the export entirely.",
            """
            {
              "type": "object",
              "properties": {
                "colorHex": { "type": "string", "description": "6-digit RGB hex without leading '#', e.g. '800000'. Case-insensitive." },
                "type":     { "type": "string", "enum": ["Slab", "Beam", "Column", "Ignore", "Opening"] }
              },
              "required": ["colorHex", "type"]
            }
            """),

        new AiTool(
            SetColorProperties,
            "Set the default slab/structural properties for every shape of a given colour: " +
            "thickness (mm), concrete grade code, superimposed dead load (kPa), live load (kPa). " +
            "Omit any property you don't want to change. All properties are ignored on shapes whose " +
            "effective type isn't 'Slab'.",
            """
            {
              "type": "object",
              "properties": {
                "colorHex":    { "type": "string" },
                "thicknessMm": { "type": "number", "description": "Slab thickness in millimetres (e.g. 200, 250)." },
                "gradeCode":   { "type": "string", "description": "Concrete grade code (e.g. 'C30', 'C35', 'C40')." },
                "sdlKPa":      { "type": "number", "description": "Superimposed dead load in kPa." },
                "liveKPa":     { "type": "number", "description": "Live load in kPa." }
              },
              "required": ["colorHex"]
            }
            """),

        new AiTool(
            SetElementType,
            "Override the element type for a single shape by index (takes precedence over the color-level type). " +
            "Use when shapes of the same colour need different types (e.g. one burgundy shape is a column, " +
            "another is a wall). 'kind' refers to the shape's CURRENT classification bucket.",
            """
            {
              "type": "object",
              "properties": {
                "kind":  { "type": "string", "enum": ["slab", "line", "column"], "description": "Current classification bucket of the shape." },
                "index": { "type": "integer", "description": "Zero-based index into the bucket's list as shown in the context dump." },
                "type":  { "type": "string", "enum": ["Slab", "Beam", "Column", "Ignore", "Opening"] }
              },
              "required": ["kind", "index", "type"]
            }
            """),

        new AiTool(
            SetElementExcluded,
            "Include or exclude a single shape from the export without changing its type. " +
            "Equivalent to the user clicking the shape in the preview to toggle it.",
            """
            {
              "type": "object",
              "properties": {
                "kind":     { "type": "string", "enum": ["slab", "line", "column"] },
                "index":    { "type": "integer" },
                "excluded": { "type": "boolean" }
              },
              "required": ["kind", "index", "excluded"]
            }
            """),

        new AiTool(
            ClearAllOverrides,
            "Reset all per-colour and per-element overrides and exclusions back to auto-detected defaults.",
            """{ "type": "object", "properties": {} }"""),

        new AiTool(
            SetExportSettings,
            "Update one or more model-wide export settings. Omit any field you don't want to change.",
            """
            {
              "type": "object",
              "properties": {
                "designCode":          { "type": "string", "enum": ["None", "CSA_A23_3_19", "ACI_318_19", "AS_3600_09", "EC2_2004", "NZS_3101_06"] },
                "loadCombCode":        { "type": "string", "enum": ["", "NBC", "ASCE7", "EC0", "AS/NZS"], "description": "Factored load combination family. Empty string = none." },
                "meshSizeMm":          { "type": "number", "description": "Target auto-mesh size in millimetres." },
                "autoGenerateStrips":  { "type": "boolean" },
                "stripSpacingMm":      { "type": "number" },
                "includePtLoads":      { "type": "boolean" },
                "slabMembraneModifier":{ "type": "number", "description": "f11/f22/f12 modifier. CSA cracked slab ≈ 0.25." },
                "slabBendingModifier": { "type": "number", "description": "m11/m22/m12 modifier." },
                "slabShearModifier":   { "type": "number", "description": "v13/v23 modifier." }
              }
            }
            """),

        new AiTool(
            ExportF2k,
            "Write the current model to a CSI SAFE .f2k file at the given path. " +
            "The output path should end with '.f2k'. Returns confirmation or an error.",
            """
            {
              "type": "object",
              "properties": {
                "outputPath": { "type": "string", "description": "Full filesystem path including filename and .f2k extension." }
              },
              "required": ["outputPath"]
            }
            """),

        new AiTool(
            ExportE2k,
            "Write the current model to a CSI ETABS .e2k file at the given path.",
            """
            {
              "type": "object",
              "properties": {
                "outputPath": { "type": "string", "description": "Full filesystem path including filename and .e2k extension." }
              },
              "required": ["outputPath"]
            }
            """),

        new AiTool(
            ExportDxf,
            "Write the current geometry to an AutoCAD .dxf file at the given path.",
            """
            {
              "type": "object",
              "properties": {
                "outputPath": { "type": "string", "description": "Full filesystem path including filename and .dxf extension." }
              },
              "required": ["outputPath"]
            }
            """),
    };
}
