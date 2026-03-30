#nullable enable
namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    public enum DesignCodeOption
    {
        None,
        CSA_A23_3_19,
        ACI_318_19,
        AS_3600_09,
        EC2_2004,
        NZS_3101_06
    }

    /// <summary>
    /// Model-level export configuration. Drives design code, rebar table,
    /// load cases, mesh, and design strip generation written into the F2K file.
    /// </summary>
    internal sealed class ExportSettings
    {
        /// <summary>Concrete design code for design preferences table and rebar selection.</summary>
        public DesignCodeOption DesignCode { get; set; } = DesignCodeOption.None;

        /// <summary>
        /// Load combination code key for factored strength/serviceability combos.
        /// Values: "", "AS/NZS", "ASCE7", "EC0", "NBC".
        /// </summary>
        public string LoadCombCode { get; set; } = "";

        /// <summary>
        /// When true, adds a LBALC (balanced PT) load pattern and includes PT
        /// terms in load combinations.
        /// </summary>
        public bool IncludePtLoads { get; set; } = false;

        /// <summary>Target maximum mesh size written to FLOOR AUTO MESH OPTIONS (mm).</summary>
        public double MeshSizeMm { get; set; } = PdfToSafeConstants.DefaultMeshSizeMm;

        /// <summary>When true, writes auto-generated design strips to the F2K file.</summary>
        public bool AutoGenerateStrips { get; set; } = false;

        /// <summary>Centre-to-centre spacing of auto-generated design strips (mm).</summary>
        public double StripSpacingMm { get; set; } = PdfToSafeConstants.DefaultStripSpacingMm;

        /// <summary>
        /// When true, Strip A runs along the X-axis (Strip B along Y).
        /// When false, Strip A runs along Y-axis.
        /// </summary>
        public bool StripAAlongX { get; set; } = true;

        /// <summary>
        /// Multiplier applied to the parent slab thickness to derive the drop panel
        /// thickness.  Default 1.5 (50 % increase over slab thickness).
        /// Must be > 1.0.
        /// </summary>
        public double DropPanelThicknessMultiplier { get; set; } = 1.5;

        /// <summary>
        /// In-plane (membrane) stiffness modifier for slab floor objects (f11, f22, f12).
        /// CSA A23.3-19 Cl. 6.6.3.4 recommends 0.25 for cracked slabs.
        /// Default 1.0 (uncracked / no reduction).
        /// </summary>
        public double SlabMembraneModifier { get; set; } = 1.0;

        /// <summary>
        /// Bending stiffness modifier for slab floor objects (m11, m22, m12).
        /// Default 1.0.
        /// </summary>
        public double SlabBendingModifier { get; set; } = 1.0;

        /// <summary>
        /// Shear stiffness modifier for slab floor objects (v13, v23).
        /// Default 1.0.
        /// </summary>
        public double SlabShearModifier { get; set; } = 1.0;
    }
}
