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
        }
    }
}
