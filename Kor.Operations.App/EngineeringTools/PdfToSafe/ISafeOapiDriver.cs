#nullable enable
using System;
using System.Collections.Generic;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// CSI load-pattern types we actually use. Maps to SAFEv1.eLoadPatternType
    /// values; resolved by name in the real driver so the orchestrator stays
    /// free of reflection / SAFEv1.dll dependencies.
    /// </summary>
    internal enum SafeLoadPatternType
    {
        SuperDead,
        Live,
    }

    /// <summary>
    /// Abstraction over the SAFE OAPI surface actually used by our exporter.
    /// Hides reflection, COM, and the fixed-value OAPI parameters (slab type,
    /// shell type, coordinate system, gravity direction code, item type)
    /// inside implementations so the <see cref="ExportOrchestrator"/> can be
    /// unit-tested against a fake without touching SAFE.
    ///
    /// Return-value convention: all methods return an OAPI-style int where 0
    /// means success. Infrastructure-level failures (missing COM server,
    /// GUID mismatch, missing interop types) surface as <see cref="SafeDriverException"/>;
    /// the orchestrator wraps the entire flow in a catch to convert those
    /// into a structured <see cref="SafeApiExporter.ExportResult"/>.
    /// </summary>
    internal interface ISafeOapiDriver : IDisposable
    {
        // ── Lifecycle ─────────────────────────────────────────────────────
        int Start();
        int Unhide();
        int InitializeNewModel(bool imperial);
        int NewBlank();
        int SetMergeTol(double mm);
        int SaveModel(string destFdbPath);

        // ── Definitions ───────────────────────────────────────────────────
        int SetMaterial(string name, string notes);
        int SetMPIsotropic(string name, double e, double u, double a);
        int SetSlabProp(string name, string matName, double thicknessMm);
        int SetFrameRectangleProp(string name, string matName, double depthMm, double widthMm);

        // ── Load patterns ─────────────────────────────────────────────────
        IReadOnlyList<string> ListLoadPatternNames();
        int AddLoadPattern(string name, SafeLoadPatternType type);

        // ── Geometry ──────────────────────────────────────────────────────
        int AddPoint(double x, double y, double z, out string pointName);
        int AddArea(IReadOnlyList<string> pointNames, string propName, out string areaName);
        int AddFrame(string startPointName, string endPointName, string sectionName, out string frameName);

        // ── Assignments ───────────────────────────────────────────────────
        /// <summary>Applies a uniform gravity load to an area. Value in SAFE database units (N/mm²).</summary>
        int SetAreaLoadUniform(string areaName, string loadPatternName, double loadNperMm2);
        /// <summary>Sets 6-DOF restraint at a point. <paramref name="dof6"/> must be length 6: UX, UY, UZ, RX, RY, RZ.</summary>
        int SetPointRestraint(string pointName, bool[] dof6);

        /// <summary>Sets the frame insertion point (cardinal point 2 = bottom center for columns below slab).</summary>
        int SetFrameInsertionPoint(string frameName, int cardinalPoint);

        /// <summary>Enables auto edge constraints on an area so SAFE auto-connects frame nodes to the slab mesh during analysis.</summary>
        int SetAreaEdgeConstraint(string areaName, bool enabled);

        /// <summary>
        /// Flags an existing area as an opening (a hole cut out of the parent
        /// slab). The area must already be defined via <see cref="AddArea"/>;
        /// SAFE/ETABS/SAP2000 then exclude its footprint from the parent slab
        /// during meshing instead of treating it as a discrete floor area.
        /// </summary>
        int SetAreaOpening(string areaName, bool opening);

        /// <summary>Sets the 8 stiffness modifiers on a slab property: f11, f22, f12, m11, m22, m12, v13, v23.</summary>
        int SetSlabModifiers(string propName, double membrane, double bending, double shear);

        /// <summary>
        /// Adds structural grid lines to the model. Each grid line has a label
        /// (1/2/3 for vertical, A/B/C for horizontal), a direction flag, and
        /// an ordinate in database units (mm or inches depending on model).
        /// </summary>
        int AddGridLines(IReadOnlyList<(string Label, bool IsAlongX, double Ordinate)> gridLines);
    }

    /// <summary>
    /// Infrastructure-level failure talking to the SAFE OAPI — e.g. missing
    /// COM registration, interface-GUID mismatch between the loaded SAFEv1.dll
    /// and the running SAFE COM server, or interop-type loading failure.
    /// The orchestrator maps this to a friendly SAFE export failure.
    /// </summary>
    internal sealed class SafeDriverException : Exception
    {
        public SafeDriverException(string message) : base(message) { }
        public SafeDriverException(string message, Exception inner) : base(message, inner) { }
    }
}
