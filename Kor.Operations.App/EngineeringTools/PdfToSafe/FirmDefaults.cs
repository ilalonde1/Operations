#nullable enable
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// Per-user firm defaults that persist across sessions and PDFs. Stored
    /// at <see cref="DefaultPath"/> as a single JSON file. Every field has a
    /// sensible KOR Structural default so first-run writes a usable file.
    /// </summary>
    internal sealed class FirmDefaults
    {
        // ── Concrete / design ────────────────────────────────────────────
        public string DefaultGradeCode   { get; set; } = "C30";
        public double DefaultSlabThicknessMm { get; set; } = 250;
        public double DefaultWallDepthMm { get; set; } = 1000;
        public string DefaultDesignCode  { get; set; } = "CSA_A23_3_19";
        public string DefaultLoadCombCode { get; set; } = "NBC";

        // ── Loading ──────────────────────────────────────────────────────
        public double DefaultSdlKPa      { get; set; } = 1.5;
        public double DefaultLiveKPa     { get; set; } = 1.9;

        // ── Mesh ─────────────────────────────────────────────────────────
        public double DefaultMeshSizeMm  { get; set; } = 500;

        // ── Design automation ────────────────────────────────────────────
        /// <summary>
        /// When true, the .f2k writer emits an auto-generated DESIGN STRIPS
        /// table covering the slab in two orthogonal directions at
        /// <see cref="DefaultStripSpacingMm"/> intervals. SAFE needs design
        /// strips to produce reinforcement results; auto-generation gets the
        /// engineer to a usable starting point without manual strip placement.
        /// They can still adjust strip widths and orientation post-import.
        /// </summary>
        public bool DefaultAutoGenerateStrips { get; set; } = true;
        public double DefaultStripSpacingMm  { get; set; } = 1800;
        /// <summary>True → Strip A along X (Strip B along Y). KOR convention.</summary>
        public bool DefaultStripAAlongX { get; set; } = true;

        /// <summary>
        /// When true, after the auto-vision pass finishes the WPF will fire
        /// the F2K export to a file next to the source PDF. Saves the
        /// engineer the click but writes to disk — leave OFF for users who
        /// want to review before export. The export still surfaces a status
        /// banner with success/skipped counts.
        /// </summary>
        public bool AutoExportAfterClassify { get; set; } = false;

        /// <summary>
        /// Occupancy preset that drives the default LIVE load applied to each
        /// new slab colour row. "Residential" → 1.92 kPa (NBC residential
        /// suite); "Office" → 2.4 kPa; "Retail" → 4.8 kPa; "Custom" →
        /// fall back to <see cref="DefaultLiveKPa"/>. When unset, the AI
        /// auto-vision pass can infer occupancy from the drawing and set
        /// this via set_color_properties — but no inference fires if the
        /// engineer has explicitly set it.
        /// </summary>
        public string DefaultOccupancy { get; set; } = "Residential";

        // ── Stiffness modifiers (CSA A23.3-19 Cl. 6.6.3.4 cracked = 0.25) ─
        public double DefaultSlabMembraneModifier { get; set; } = 1.0;
        public double DefaultSlabBendingModifier  { get; set; } = 1.0;
        public double DefaultSlabShearModifier    { get; set; } = 1.0;

        // ── Unit system ──────────────────────────────────────────────────
        /// <summary>
        /// "Metric" (N, mm, °C) or "Imperial" (kip, in, °F). Default Imperial
        /// per firm guidance (90% of KOR projects are imperial). Controls:
        ///   - SAFE model database units (eUnits on InitializeNewModel)
        ///   - Coordinate/thickness/load conversion at export time
        ///   - Display units in the Firm Defaults dialog
        /// Internal storage (ThicknessMm, SdlKPa, etc.) always stays metric;
        /// the dialog and orchestrator convert at the boundary.
        /// </summary>
        public string UnitSystem { get; set; } = "Imperial";

        // ── SAFE OAPI ────────────────────────────────────────────────────
        /// <summary>
        /// Optional full path to the licensed SAFE.exe. When set, the SAFE API
        /// exporter launches this exact install instead of whichever version
        /// the COM ProgID happens to resolve to — required on machines with
        /// multiple SAFE editions where the default registration is wrong.
        /// </summary>
        public string SafeExePath { get; set; } = string.Empty;
        public string EtabsExePath { get; set; } = string.Empty;
        public string Sap2000ExePath { get; set; } = string.Empty;

        // ── File I/O ─────────────────────────────────────────────────────
        public static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KorOperations",
            "pdftosafe_defaults.json");

        /// <summary>
        /// Loads defaults from <see cref="DefaultPath"/>. Any file-IO or parse
        /// failure returns a fresh instance with shipped defaults — never
        /// throws, never blocks app startup.
        /// </summary>
        public static FirmDefaults Load()
        {
            try
            {
                if (!File.Exists(DefaultPath)) return new FirmDefaults();
                var json = File.ReadAllText(DefaultPath);
                var defaults = JsonSerializer.Deserialize<FirmDefaults>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return defaults ?? new FirmDefaults();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning(
                    $"FirmDefaults.Load: failed to read '{DefaultPath}', using shipped defaults. {ex.GetType().Name}: {ex.Message}");
                return new FirmDefaults();
            }
        }

        /// <summary>
        /// Saves the current instance to <see cref="DefaultPath"/>, creating
        /// the parent directory if needed. Returns true on success.
        /// </summary>
        public bool Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(DefaultPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(this,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(DefaultPath, json);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning(
                    $"FirmDefaults.Save: failed to write '{DefaultPath}'. {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Applies the stored code/combo/mesh/stiffness defaults to the
        /// supplied <see cref="ExportSettings"/> in place. Does NOT touch
        /// slab-level properties (those belong to SlabColorSettings).
        /// </summary>
        public void ApplyTo(ExportSettings target)
        {
            if (target is null) return;
            if (Enum.TryParse<DesignCodeOption>(DefaultDesignCode, ignoreCase: true, out var dc))
                target.DesignCode = dc;
            if (!string.IsNullOrWhiteSpace(DefaultLoadCombCode))
                target.LoadCombCode = DefaultLoadCombCode;
            if (DefaultMeshSizeMm > 0) target.MeshSizeMm = DefaultMeshSizeMm;
            if (DefaultSlabMembraneModifier > 0) target.SlabMembraneModifier = DefaultSlabMembraneModifier;
            if (DefaultSlabBendingModifier  > 0) target.SlabBendingModifier  = DefaultSlabBendingModifier;
            if (DefaultSlabShearModifier    > 0) target.SlabShearModifier    = DefaultSlabShearModifier;

            // Design-strip automation: writer reads these from ExportSettings.
            target.AutoGenerateStrips = DefaultAutoGenerateStrips;
            if (DefaultStripSpacingMm > 0) target.StripSpacingMm = DefaultStripSpacingMm;
            target.StripAAlongX = DefaultStripAAlongX;
        }

        /// <summary>
        /// Resolves the live load (kPa) corresponding to <see cref="DefaultOccupancy"/>
        /// per NBC's typical-occupancy table. Used as the seed for new slab
        /// colour rows when no per-row value is set.
        /// </summary>
        public double GetOccupancyLiveKPa() =>
            DefaultOccupancy switch
            {
                "Residential" => 1.92,
                "Office"      => 2.40,
                "Retail"      => 4.80,
                "Assembly"    => 4.80,
                _             => DefaultLiveKPa > 0 ? DefaultLiveKPa : 1.92,
            };
    }
}
