#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    internal enum ValidationSeverity { Error, Warning, Info }

    internal sealed record ValidationIssue(
        ValidationSeverity Severity,
        string Category,
        string Message,
        string? ElementKind = null,
        int? ElementIndex = null);

    internal sealed record ValidationResult(IReadOnlyList<ValidationIssue> Issues)
    {
        public bool HasErrors    => Issues.Any(i => i.Severity == ValidationSeverity.Error);
        public bool HasWarnings  => Issues.Any(i => i.Severity == ValidationSeverity.Warning);
        public int  ErrorCount   => Issues.Count(i => i.Severity == ValidationSeverity.Error);
        public int  WarningCount => Issues.Count(i => i.Severity == ValidationSeverity.Warning);
    }

    /// <summary>
    /// Pure-logic pre-export validator. Consumes the reclassified geometry
    /// (exactly what the F2K writer will see) plus colour/export settings
    /// and returns a structured list of errors and warnings.
    ///
    /// Rules encode conditions that either make SAFE import fail or produce
    /// structurally meaningless / misleading models. Each rule is independent;
    /// new rules can be added in isolation without touching the others.
    /// </summary>
    internal static class ExportValidator
    {
        /// <summary>
        /// Validates the model a caller is about to export.
        /// </summary>
        /// <param name="reclassified">
        /// Output of <see cref="PdfGeometryExtractor.ReclassifyByColor"/>. Must
        /// reflect the final state the F2K writer will see.
        /// </param>
        /// <param name="colorSettings">
        /// Map of colour → <see cref="SlabColorSettings"/>. Caller's current
        /// per-colour UI state, honoured during property validation.
        /// </param>
        /// <param name="settings">Model-wide export settings.</param>
        public static ValidationResult Validate(
            ExtractedGeometry reclassified,
            IReadOnlyDictionary<(byte R, byte G, byte B), SlabColorSettings>? colorSettings,
            ExportSettings settings)
        {
            if (reclassified is null) throw new ArgumentNullException(nameof(reclassified));
            if (settings is null) throw new ArgumentNullException(nameof(settings));

            var issues = new List<ValidationIssue>();

            // ── Rule 1: Something to export ──────────────────────────────
            if (reclassified.Slabs.Count == 0 && reclassified.Columns.Count == 0 && reclassified.Lines.Count == 0)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "model-empty",
                    "Nothing to export — model has no slabs, columns, or walls/beams."));
            }

            // ── Rule 2: Slab polygon integrity ───────────────────────────
            for (int i = 0; i < reclassified.Slabs.Count; i++)
            {
                var pts = reclassified.Slabs[i];
                if (pts is null || pts.Count < 3)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        "slab-degenerate",
                        $"Slab [{i}] has fewer than 3 vertices — SAFE cannot mesh this polygon.",
                        "slab", i));
                    continue;
                }
                double area = Math.Abs(PolygonProcessor.PolygonAreaMm2(pts));
                if (area < 1.0) // <1 mm² is noise
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        "slab-zero-area",
                        $"Slab [{i}] has zero or near-zero area ({area:F2} mm²).",
                        "slab", i));
                }
            }

            // ── Rule 3: Slab thickness is a positive number ──────────────
            if (colorSettings is not null)
            {
                for (int i = 0; i < reclassified.Slabs.Count; i++)
                {
                    (byte R, byte G, byte B) color = i < reclassified.SlabColors.Count
                        ? reclassified.SlabColors[i] : ((byte)0, (byte)0, (byte)0);
                    if (!colorSettings.TryGetValue(color, out var cs)) continue;
                    if (cs.ThicknessMm <= 0)
                    {
                        issues.Add(new ValidationIssue(
                            ValidationSeverity.Error,
                            "slab-thickness",
                            $"Slab [{i}] (#{color.R:X2}{color.G:X2}{color.B:X2}) has thickness = {cs.ThicknessMm} mm. Must be > 0.",
                            "slab", i));
                    }
                }
            }

            // ── Rule 4: Column supported above by a slab ─────────────────
            // If no slabs exist this rule is vacuous — Rule 1 already fires.
            if (reclassified.Slabs.Count > 0)
            {
                for (int i = 0; i < reclassified.Columns.Count; i++)
                {
                    var pt = reclassified.Columns[i];
                    bool supported = false;
                    for (int s = 0; s < reclassified.Slabs.Count; s++)
                    {
                        if (PolygonProcessor.PointInPolygon(pt, reclassified.Slabs[s]))
                        { supported = true; break; }
                    }
                    if (!supported)
                    {
                        issues.Add(new ValidationIssue(
                            ValidationSeverity.Warning,
                            "column-unsupported",
                            $"Column [{i}] at ({pt.X:F0}, {pt.Y:F0}) mm lies outside every slab polygon — nothing for it to support.",
                            "column", i));
                    }
                }
            }

            // ── Rule 5: Wall centerlines have length ─────────────────────
            // A wall hint with zero-length centerline is a geometry bug, not a
            // user mistake — but it still renders as garbage in SAFE.
            for (int i = 0; i < reclassified.Lines.Count; i++)
            {
                if (i >= reclassified.LineSectionHints.Count) break;
                if (!reclassified.LineSectionHints[i].HasValue) continue;
                var pts = reclassified.Lines[i];
                if (pts.Count < 2) continue;
                double len = PolygonProcessor.PathLength(pts);
                if (len < 10.0) // <10mm = degenerate
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning,
                        "wall-zero-length",
                        $"Wall [{i}] has length {len:F1} mm — likely degenerate.",
                        "line", i));
                }
            }

            // ── Rule 6: LIVE load mentioned in combo but not assigned ────
            if (!string.IsNullOrWhiteSpace(settings.LoadCombCode))
            {
                bool anyLive = colorSettings is not null
                    && colorSettings.Values.Any(s => s.LiveKPa > 0);
                if (!anyLive)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning,
                        "live-load-missing",
                        $"Load combo family '{settings.LoadCombCode}' is selected but no colour has LIVE load > 0. " +
                        $"Combos will still reference 'L' but only DEAD will contribute."));
                }
            }

            // ── Rule 7: Duplicate columns (≤10 mm apart) ────────────────
            const double dupTolMm = 10.0;
            for (int i = 0; i < reclassified.Columns.Count; i++)
            {
                for (int j = i + 1; j < reclassified.Columns.Count; j++)
                {
                    if (PolygonProcessor.Distance(reclassified.Columns[i], reclassified.Columns[j]) < dupTolMm)
                    {
                        issues.Add(new ValidationIssue(
                            ValidationSeverity.Warning,
                            "column-duplicate",
                            $"Columns [{i}] and [{j}] are within {dupTolMm:F0} mm of each other — likely duplicates.",
                            "column", i));
                        // Don't double-report: break the inner loop so we flag each
                        // column at most once as a duplicate source.
                        break;
                    }
                }
            }

            // ── Rule 8: Suspicious wall thickness ───────────────────────
            // Real concrete walls run 100–500 mm thick; even chunky shear
            // walls rarely exceed 1 m. A "wall" section > 800 mm wide is
            // almost always a shaft outline drawn as a single polygon that
            // the shaft-decomposition heuristic missed (e.g. aspect just
            // under the threshold) — emits as an absurdly thick frame
            // section in SAFE. Flag for engineer review.
            const double wallThicknessWarnMm = 800.0;
            for (int i = 0; i < reclassified.Lines.Count; i++)
            {
                if (i >= reclassified.LineSectionHints.Count) break;
                var hint = reclassified.LineSectionHints[i];
                if (!hint.HasValue) continue;
                if (hint.Value.WidthMm > wallThicknessWarnMm)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning,
                        "wall-suspicious-thickness",
                        $"Wall [{i}] section {hint.Value.WidthMm:F0} mm thick — unusual for a real wall; may be a shaft outline mis-detected as a single wall.",
                        "line", i));
                }
            }

            // ── Rule 10: Three-vertex slab is a degenerate triangle ─────
            // Slabs chain-assembled from open polylines (balcony stubs,
            // slab-edge fragments) often emerge with only 3 vertices. They
            // might be legitimate balcony bump-outs OR pen-thickness /
            // extraction artifacts that scraped past the 2 m² noise filter.
            // Surface so the engineer can verify or exclude in the UI.
            for (int i = 0; i < reclassified.Slabs.Count; i++)
            {
                var pts = reclassified.Slabs[i];
                if (pts is null || pts.Count != 3) continue;
                var centroid = PolygonProcessor.Centroid(pts);
                double area = PolygonProcessor.PolygonAreaMm2(pts);
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    "slab-triangle",
                    $"Slab [{i}] at ({centroid.X:F0},{centroid.Y:F0}) has only 3 vertices ({area / 1_000_000.0:F1} m²) — likely a balcony stub or extraction fragment. Verify or exclude before export.",
                    "slab", i));
            }

            // ── Rule 9: Suspicious column dimension ─────────────────────
            // Reclassify's column-guardrail already routes anything > 2 m
            // to wall reduction, but a column right at the boundary (e.g.
            // 1.5–2 m) is still likely a wall stub. Surface as a warning so
            // the engineer can override the type if it really should be a
            // wall.
            const double columnDimWarnMm = 1500.0;
            for (int i = 0; i < reclassified.Columns.Count; i++)
            {
                if (i >= reclassified.ColumnSizes.Count) break;
                var (w, d) = reclassified.ColumnSizes[i];
                double maxDim = Math.Max(w, d);
                if (maxDim > columnDimWarnMm)
                {
                    var pt = reclassified.Columns[i];
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning,
                        "column-suspicious-size",
                        $"Column [{i}] at ({pt.X:F0}, {pt.Y:F0}) is {w:F0}×{d:F0} mm — unusually large; may be a wall stub.",
                        "column", i));
                }
            }

            return new ValidationResult(issues);
        }
    }
}
