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
            catch
            {
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
            catch
            {
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
