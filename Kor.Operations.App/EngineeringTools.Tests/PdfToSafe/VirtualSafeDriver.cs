#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kor.Operations.EngineeringTools.PdfToSafe;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe
{
    /// <summary>
    /// In-memory simulation of CSI SAFE's OAPI behaviour. Unlike
    /// <c>FakeSafeOapiDriver</c> (which just records calls), this driver
    /// VALIDATES inputs the way a real SAFE COM server would — rejecting
    /// degenerate polygons, duplicate names, missing section references, etc.
    /// It also builds a textual snapshot of the "model" it constructs, which
    /// tests diff against committed golden files.
    ///
    /// Rules are derived from:
    ///   - The SAFE 22.7 F2K import error log (session 2026-04-14)
    ///   - CSI OAPI documentation (public API reference)
    ///   - Observed SAFE behaviour during the Regent floor export runs
    ///
    /// When SAFE changes a rule, updating this driver is how we keep the
    /// virtual pipeline in sync with reality — cheaper than a publish.
    /// </summary>
    internal sealed class VirtualSafeDriver : ISafeOapiDriver
    {
        // ── Model state ───────────────────────────────────────────────────

        public sealed record MaterialDef(string Name, string Notes, double E, double U, double A);
        public sealed record SlabPropDef(string Name, string MatName, double ThicknessMm);
        public sealed record FramePropDef(string Name, string MatName, double DepthMm, double WidthMm);
        public sealed record PointDef(string Name, double X, double Y, double Z);
        public sealed record AreaDef(string Name, string PropName, IReadOnlyList<string> PointNames);
        public sealed record FrameDef(string Name, string SectionName, string StartPt, string EndPt);
        public sealed record AreaLoadDef(string AreaName, string PatternName, double ValueNperMm2);
        public sealed record RestraintDef(string PointName, bool[] Dof6);
        public sealed record LoadPatternDef(string Name, SafeLoadPatternType Type);

        public List<MaterialDef> Materials { get; } = new();
        public List<SlabPropDef> SlabProps { get; } = new();
        public List<FramePropDef> FrameProps { get; } = new();
        public List<PointDef> Points { get; } = new();
        public List<AreaDef> Areas { get; } = new();
        public List<FrameDef> Frames { get; } = new();
        public List<AreaLoadDef> AreaLoads { get; } = new();
        public List<RestraintDef> Restraints { get; } = new();
        public List<LoadPatternDef> LoadPatterns { get; } = new();

        /// <summary>Names of areas flagged as openings via SetAreaOpening(true).</summary>
        public HashSet<string> Openings { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Pre-populated pattern names that NewBlank creates (SAFE default).</summary>
        public HashSet<string> PreExistingPatterns { get; } = new(StringComparer.OrdinalIgnoreCase) { "DEAD", "LIVE" };

        public bool Started { get; private set; }
        public bool ModelInitialized { get; private set; }
        public bool BlankCreated { get; private set; }
        public double MergeTolMm { get; private set; } = 25.4; // SAFE imperial default
        public string? SavedPath { get; private set; }
        public bool Disposed { get; private set; }

        private int _nextPointId = 1;
        private int _nextAreaId = 1;
        private int _nextFrameId = 1;

        // ── Lifecycle ─────────────────────────────────────────────────────

        public int Start() { Started = true; return 0; }
        public int Unhide() => 0;
        public bool IsImperial { get; private set; }
        public int InitializeNewModel(bool imperial) { ModelInitialized = true; IsImperial = imperial; return 0; }

        public int NewBlank()
        {
            BlankCreated = true;
            foreach (var p in PreExistingPatterns)
                LoadPatterns.Add(new LoadPatternDef(p, p == "DEAD" ? SafeLoadPatternType.SuperDead : SafeLoadPatternType.Live));
            return 0;
        }

        public int SetMergeTol(double mm) { MergeTolMm = mm; return 0; }

        public int SaveModel(string destFdbPath)
        {
            SavedPath = destFdbPath;
            return 0;
        }

        // ── Definitions ───────────────────────────────────────────────────

        public int SetMaterial(string name, string notes)
        {
            if (string.IsNullOrWhiteSpace(name)) return 1;
            Materials.RemoveAll(m => m.Name == name);
            Materials.Add(new MaterialDef(name, notes, 0, 0, 0));
            return 0;
        }

        public int SetMPIsotropic(string name, double e, double u, double a)
        {
            var idx = Materials.FindIndex(m => m.Name == name);
            if (idx < 0) return 1;
            Materials[idx] = new MaterialDef(name, Materials[idx].Notes, e, u, a);
            return 0;
        }

        public int SetSlabProp(string name, string matName, double thicknessMm)
        {
            if (thicknessMm <= 0) return 1;
            if (!Materials.Any(m => m.Name == matName)) return 1;
            SlabProps.RemoveAll(p => p.Name == name);
            SlabProps.Add(new SlabPropDef(name, matName, thicknessMm));
            return 0;
        }

        public int SetFrameRectangleProp(string name, string matName, double depthMm, double widthMm)
        {
            if (depthMm <= 0 || widthMm <= 0) return 1;
            if (!Materials.Any(m => m.Name == matName)) return 1;
            FrameProps.RemoveAll(p => p.Name == name);
            FrameProps.Add(new FramePropDef(name, matName, depthMm, widthMm));
            return 0;
        }

        // ── Load patterns ─────────────────────────────────────────────────

        public IReadOnlyList<string> ListLoadPatternNames()
            => LoadPatterns.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        public int AddLoadPattern(string name, SafeLoadPatternType type)
        {
            if (LoadPatterns.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                return 1; // already exists
            LoadPatterns.Add(new LoadPatternDef(name, type));
            return 0;
        }

        // ── Geometry ──────────────────────────────────────────────────────

        public int AddPoint(double x, double y, double z, out string pointName)
        {
            // Merge: if a point exists within MergeTolMm, return the existing name.
            foreach (var existing in Points)
            {
                double dx = existing.X - x, dy = existing.Y - y, dz = existing.Z - z;
                if (Math.Sqrt(dx * dx + dy * dy + dz * dz) <= MergeTolMm)
                {
                    pointName = existing.Name;
                    return 0;
                }
            }
            pointName = $"P{_nextPointId++}";
            Points.Add(new PointDef(pointName, x, y, z));
            return 0;
        }

        public int AddArea(IReadOnlyList<string> pointNames, string propName, out string areaName)
        {
            areaName = "";
            if (pointNames.Count < 3) return 1; // SAFE rejects < 3 vertices
            if (!SlabProps.Any(p => p.Name == propName)) return 1; // unknown section
            // Check all point names exist
            foreach (var pn in pointNames)
                if (!Points.Any(p => p.Name == pn)) return 1;
            // Zero-area check (collinear points)
            var coords = pointNames.Select(n => Points.First(p => p.Name == n)).ToList();
            double area2 = 0;
            for (int i = 0; i < coords.Count; i++)
            {
                var a = coords[i];
                var b = coords[(i + 1) % coords.Count];
                area2 += a.X * b.Y - b.X * a.Y;
            }
            if (Math.Abs(area2 / 2.0) < 1.0) return 1; // SAFE rejects area < 1 mm²

            areaName = $"A{_nextAreaId++}";
            Areas.Add(new AreaDef(areaName, propName, pointNames.ToList()));
            return 0;
        }

        public int AddFrame(string startPointName, string endPointName, string sectionName, out string frameName)
        {
            frameName = "";
            if (!Points.Any(p => p.Name == startPointName)) return 1;
            if (!Points.Any(p => p.Name == endPointName)) return 1;
            if (startPointName == endPointName) return 1; // zero-length frame
            if (!FrameProps.Any(p => p.Name == sectionName)) return 1;
            frameName = $"F{_nextFrameId++}";
            Frames.Add(new FrameDef(frameName, sectionName, startPointName, endPointName));
            return 0;
        }

        // ── Assignments ───────────────────────────────────────────────────

        public int SetAreaLoadUniform(string areaName, string loadPatternName, double loadNperMm2)
        {
            if (!Areas.Any(a => a.Name == areaName)) return 1;
            if (!LoadPatterns.Any(p => string.Equals(p.Name, loadPatternName, StringComparison.OrdinalIgnoreCase))) return 1;
            AreaLoads.Add(new AreaLoadDef(areaName, loadPatternName, loadNperMm2));
            return 0;
        }

        public int SetPointRestraint(string pointName, bool[] dof6)
        {
            if (dof6.Length != 6) return 1;
            if (!Points.Any(p => p.Name == pointName)) return 1;
            Restraints.Add(new RestraintDef(pointName, (bool[])dof6.Clone()));
            return 0;
        }

        public int SetSlabModifiers(string propName, double membrane, double bending, double shear)
        {
            // Store for verification; doesn't affect virtual model behavior.
            return 0;
        }

        public int SetFrameInsertionPoint(string frameName, int cardinalPoint)
        {
            if (!Frames.Any(f => f.Name == frameName)) return 1;
            return 0;
        }

        public int SetAreaEdgeConstraint(string areaName, bool enabled)
        {
            if (!Areas.Any(a => a.Name == areaName)) return 1;
            return 0;
        }

        public int SetAreaOpening(string areaName, bool opening)
        {
            if (!Areas.Any(a => a.Name == areaName)) return 1;
            if (opening) Openings.Add(areaName);
            else Openings.Remove(areaName);
            return 0;
        }

        public List<(string Label, bool IsAlongX, double Ordinate)> GridLines { get; } = new();

        public int AddGridLines(IReadOnlyList<(string Label, bool IsAlongX, double Ordinate)> gridLines)
        {
            GridLines.AddRange(gridLines);
            return 0;
        }

        public void Dispose() { Disposed = true; }

        // ── Snapshot ──────────────────────────────────────────────────────

        /// <summary>
        /// Builds a deterministic textual summary of the model state. Tests
        /// compare this against committed golden files to catch regressions
        /// without a live SAFE install.
        /// </summary>
        public string BuildSnapshot()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== VIRTUAL SAFE MODEL SNAPSHOT ===");
            sb.AppendLine();

            sb.AppendLine($"MergeTol: {MergeTolMm} mm");
            sb.AppendLine($"Points: {Points.Count}");
            sb.AppendLine($"Areas: {Areas.Count} (openings: {Openings.Count})");
            sb.AppendLine($"Frames: {Frames.Count}");
            sb.AppendLine($"Materials: {Materials.Count}");
            sb.AppendLine($"SlabProps: {SlabProps.Count}");
            sb.AppendLine($"FrameProps: {FrameProps.Count}");
            sb.AppendLine($"LoadPatterns: {LoadPatterns.Count}");
            sb.AppendLine($"AreaLoads: {AreaLoads.Count}");
            sb.AppendLine($"Restraints: {Restraints.Count}");
            sb.AppendLine($"SavedPath: {SavedPath ?? "(not saved)"}");
            sb.AppendLine();

            sb.AppendLine("--- Materials ---");
            foreach (var m in Materials.OrderBy(m => m.Name))
                sb.AppendLine($"  {m.Name}: E={m.E:F2} U={m.U:F2} A={m.A:E2} ({m.Notes})");

            sb.AppendLine("--- Slab Properties ---");
            foreach (var p in SlabProps.OrderBy(p => p.Name))
                sb.AppendLine($"  {p.Name}: mat={p.MatName} thickness={p.ThicknessMm:F1}mm");

            sb.AppendLine("--- Frame Properties ---");
            foreach (var p in FrameProps.OrderBy(p => p.Name))
                sb.AppendLine($"  {p.Name}: mat={p.MatName} depth={p.DepthMm:F1} width={p.WidthMm:F1}");

            sb.AppendLine("--- Load Patterns ---");
            foreach (var lp in LoadPatterns.OrderBy(lp => lp.Name))
                sb.AppendLine($"  {lp.Name}: {lp.Type}");

            sb.AppendLine("--- Areas ---");
            foreach (var a in Areas.OrderBy(a => a.Name))
            {
                var pts = a.PointNames.Select(n =>
                {
                    var p = Points.FirstOrDefault(pt => pt.Name == n);
                    return p is not null ? $"({p.X:F1},{p.Y:F1})" : n;
                });
                string kind = Openings.Contains(a.Name) ? " [OPENING]" : "";
                sb.AppendLine($"  {a.Name}: prop={a.PropName} vertices={a.PointNames.Count} [{string.Join(" ", pts)}]{kind}");
            }

            sb.AppendLine("--- Area Loads ---");
            foreach (var l in AreaLoads.OrderBy(l => l.AreaName).ThenBy(l => l.PatternName))
                sb.AppendLine($"  {l.AreaName}: pattern={l.PatternName} value={l.ValueNperMm2:E4} N/mm²");

            sb.AppendLine("--- Frames ---");
            foreach (var f in Frames.OrderBy(f => f.Name))
            {
                var s = Points.FirstOrDefault(p => p.Name == f.StartPt);
                var e = Points.FirstOrDefault(p => p.Name == f.EndPt);
                string start = s is not null ? $"({s.X:F1},{s.Y:F1},{s.Z:F1})" : f.StartPt;
                string end   = e is not null ? $"({e.X:F1},{e.Y:F1},{e.Z:F1})" : f.EndPt;
                sb.AppendLine($"  {f.Name}: sec={f.SectionName} {start}→{end}");
            }

            sb.AppendLine("--- Restraints ---");
            foreach (var r in Restraints.OrderBy(r => r.PointName))
            {
                string dofs = string.Join("", r.Dof6.Select(d => d ? "1" : "0"));
                var p = Points.FirstOrDefault(pt => pt.Name == r.PointName);
                string loc = p is not null ? $"({p.X:F1},{p.Y:F1},{p.Z:F1})" : r.PointName;
                sb.AppendLine($"  {r.PointName} {loc}: [{dofs}]");
            }

            return sb.ToString();
        }
    }
}
