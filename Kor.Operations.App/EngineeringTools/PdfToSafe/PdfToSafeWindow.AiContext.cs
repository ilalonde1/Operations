#nullable enable
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Kor.Operations.Services;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// <see cref="IAiContextProvider"/> implementation for the PDF-to-SAFE import
    /// window. Builds a structured, machine-readable snapshot of the current
    /// extraction state for Claude to reason about. Pure read-only — no UI
    /// mutations. Every field mirrors state already shown in the UI so there is
    /// no risk of the AI seeing something the user cannot verify.
    /// </summary>
    public partial class PdfToSafeWindow : IAiContextProvider
    {
        /// <summary>
        /// Most recently clicked preview shape, if any. Populated by
        /// Shape_MouseDown (left or right click) and surfaced to Claude via
        /// BuildLocalContext so "what about this one?" questions resolve
        /// without the user having to name the shape.
        /// </summary>
        private (string Kind, int Index)? _lastFocusedElement;

        string IAiContextProvider.ProviderName => "PdfToSafe (Structural PDF Import)";

        bool IAiContextProvider.HasData => _extractedGeometry is not null && _extractedGeometry.IsVectorPdf;

        string IAiContextProvider.BuildContext()
        {
            var geo = _extractedGeometry;
            if (geo is null) return "No PDF loaded.";

            var ic = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(capacity: 4096);

            // ── Document summary ────────────────────────────────────────
            sb.AppendLine("--- DOCUMENT ---");
            sb.AppendLine($"  File: {_loadedFilePath ?? "(unsaved)"}");
            sb.AppendLine($"  Page count: {geo.PageCount}");
            sb.AppendLine($"  Page size: {geo.PageWidthPts.ToString("0.#", ic)} x {geo.PageHeightPts.ToString("0.#", ic)} pts");
            sb.AppendLine($"  Scale denominator (1:{geo.ScaleDenominator}).");
            sb.AppendLine($"  Vector PDF: {geo.IsVectorPdf}");
            sb.AppendLine();

            // ── Shape counts ────────────────────────────────────────────
            sb.AppendLine("--- DETECTED SHAPES (summary) ---");
            sb.AppendLine($"  Slabs:   {geo.Slabs.Count}");
            sb.AppendLine($"  Columns: {geo.Columns.Count}");
            sb.AppendLine($"  Lines:   {geo.Lines.Count}");
            sb.AppendLine();

            // ── Slabs with coordinates ──────────────────────────────────
            if (geo.Slabs.Count > 0)
            {
                sb.AppendLine("--- SLAB SHAPES ---");
                sb.AppendLine("  Format: [index] color=#HEX  vertices=N  bbox=WxH_mm  centroid=(x,y)_mm  area=A_mm2");
                for (int i = 0; i < geo.Slabs.Count; i++)
                {
                    var pts = geo.Slabs[i];
                    (byte R, byte G, byte B) color = i < geo.SlabColors.Count ? geo.SlabColors[i] : ((byte)0, (byte)0, (byte)0);
                    double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
                    double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
                    var centroid = PolygonProcessor.Centroid(pts);
                    double area = PolygonProcessor.PolygonAreaMm2(pts);
                    string extras = BuildElementExtras("slab", i, color);
                    sb.AppendLine($"  [{i}] color=#{color.R:X2}{color.G:X2}{color.B:X2}  vertices={pts.Count}  " +
                                  $"bbox={(maxX - minX).ToString("0", ic)}x{(maxY - minY).ToString("0", ic)}mm  " +
                                  $"centroid=({centroid.X.ToString("0", ic)},{centroid.Y.ToString("0", ic)})  " +
                                  $"area={area.ToString("0", ic)}mm2{extras}");
                }
                sb.AppendLine();
            }

            // ── Columns ─────────────────────────────────────────────────
            if (geo.Columns.Count > 0)
            {
                sb.AppendLine("--- COLUMN SHAPES ---");
                sb.AppendLine("  Format: [index] color=#HEX  centroid=(x,y)_mm  section=WxD_mm");
                for (int i = 0; i < geo.Columns.Count; i++)
                {
                    var (x, y) = geo.Columns[i];
                    (byte R, byte G, byte B) color = i < geo.ColumnColors.Count ? geo.ColumnColors[i] : ((byte)0, (byte)0, (byte)0);
                    var (w, d) = i < geo.ColumnSizes.Count ? geo.ColumnSizes[i] : (0d, 0d);
                    string extras = BuildElementExtras("column", i, color);
                    sb.AppendLine($"  [{i}] color=#{color.R:X2}{color.G:X2}{color.B:X2}  " +
                                  $"centroid=({x.ToString("0", ic)},{y.ToString("0", ic)})mm  " +
                                  $"section={w.ToString("0", ic)}x{d.ToString("0", ic)}mm{extras}");
                }
                sb.AppendLine();
            }

            // ── Lines ───────────────────────────────────────────────────
            if (geo.Lines.Count > 0)
            {
                sb.AppendLine("--- LINE SHAPES ---");
                sb.AppendLine("  Format: [index] color=#HEX  pts=N  length=L_mm  closed=YES/NO");
                for (int i = 0; i < geo.Lines.Count; i++)
                {
                    var pts = geo.Lines[i];
                    (byte R, byte G, byte B) color = i < geo.LineColors.Count ? geo.LineColors[i] : ((byte)0, (byte)0, (byte)0);
                    double len = PolygonProcessor.PathLength(pts);
                    bool closed = pts.Count >= 3 &&
                                  PolygonProcessor.Distance(pts[0], pts[^1]) < 10.0;
                    string extras = BuildElementExtras("line", i, color);
                    sb.AppendLine($"  [{i}] color=#{color.R:X2}{color.G:X2}{color.B:X2}  " +
                                  $"pts={pts.Count}  length={len.ToString("0", ic)}mm  " +
                                  $"closed={(closed ? "YES" : "NO")}{extras}");
                }
                sb.AppendLine();
            }

            // ── Per-colour settings (data model) ─────────────────────────
            if (_slabPropsRows.Count > 0)
            {
                sb.AppendLine("--- COLOR SETTINGS (defaults applied to all shapes of each colour) ---");
                sb.AppendLine("  Format: #HEX  type=X  thickness=Tmm  grade=G  sdl=SkPa  live=LkPa  included=Y/N");
                foreach (var row in _slabPropsRows)
                {
                    sb.AppendLine($"  #{row.Color.R:X2}{row.Color.G:X2}{row.Color.B:X2}  " +
                                  $"type={row.ElementType}  " +
                                  $"thickness={row.ThicknessMm.ToString("0.###", ic)}mm  " +
                                  $"grade={row.GradeCode}  " +
                                  $"sdl={row.SdlKPa.ToString("0.###", ic)}kPa  " +
                                  $"live={row.LiveKPa.ToString("0.###", ic)}kPa  " +
                                  $"included={(row.Included ? "Y" : "N")}");
                }
                sb.AppendLine();
            }

            // ── Excluded colors ─────────────────────────────────────────
            if (_excl.Colors.Count > 0)
            {
                sb.AppendLine("--- EXCLUDED COLORS ---");
                foreach (var c in _excl.Colors)
                    sb.AppendLine($"  #{c.R:X2}{c.G:X2}{c.B:X2}");
                sb.AppendLine();
            }

            // ── Text annotations ────────────────────────────────────────
            if (geo.TextAnnotations is not null && geo.TextAnnotations.Count > 0)
            {
                int limit = Math.Min(geo.TextAnnotations.Count, 40);
                sb.AppendLine($"--- TEXT ANNOTATIONS ({geo.TextAnnotations.Count}, showing first {limit}) ---");
                for (int i = 0; i < limit; i++)
                {
                    var (t, x, y) = geo.TextAnnotations[i];
                    sb.AppendLine($"  \"{t}\" @ ({x.ToString("0", ic)},{y.ToString("0", ic)})mm");
                }
                sb.AppendLine();
            }

            // ── Text-annotation resolution ───────────────────────────────
            // Pre-computed nearest-text matches for every shape. When a shape
            // has a matched annotation, the export pipeline will use THIS
            // section/thickness instead of bbox-derived defaults. Giving the
            // AI this mapping up-front stops it from re-deriving sizes the
            // engineer already wrote on the PDF.
            try
            {
                var resolution = AnnotationResolver.Resolve(geo);
                int slabHits = 0, colHits = 0, lineHits = 0;
                for (int i = 0; i < resolution.SlabThicknessMm.Length; i++)
                    if (resolution.SlabThicknessMm[i].HasValue) slabHits++;
                for (int i = 0; i < resolution.ColumnSectionMm.Length; i++)
                    if (resolution.ColumnSectionMm[i].HasValue) colHits++;
                for (int i = 0; i < resolution.LineSectionMm.Length; i++)
                    if (resolution.LineSectionMm[i].HasValue) lineHits++;

                if (slabHits + colHits + lineHits > 0)
                {
                    sb.AppendLine($"--- TEXT-ANNOTATION MATCHES ({slabHits + colHits + lineHits} resolved) ---");
                    sb.AppendLine("  These sections/thicknesses come from the PDF itself — treat as authoritative.");
                    for (int i = 0; i < resolution.SlabThicknessMm.Length; i++)
                    {
                        if (!resolution.SlabThicknessMm[i].HasValue) continue;
                        sb.AppendLine($"  slab[{i}] → thickness {resolution.SlabThicknessMm[i]!.Value.ToString("0", ic)}mm");
                    }
                    for (int i = 0; i < resolution.ColumnSectionMm.Length; i++)
                    {
                        if (!resolution.ColumnSectionMm[i].HasValue) continue;
                        var (w, d) = resolution.ColumnSectionMm[i]!.Value;
                        sb.AppendLine($"  column[{i}] → section {w.ToString("0", ic)}x{d.ToString("0", ic)}mm");
                    }
                    for (int i = 0; i < resolution.LineSectionMm.Length; i++)
                    {
                        if (!resolution.LineSectionMm[i].HasValue) continue;
                        var (w, d) = resolution.LineSectionMm[i]!.Value;
                        sb.AppendLine($"  line[{i}] → beam section {w.ToString("0", ic)}x{d.ToString("0", ic)}mm");
                    }
                    sb.AppendLine();
                }
            }
            catch { /* context building must never throw */ }

            return sb.ToString();
        }

        string IAiContextProvider.BuildLocalContext()
        {
            if (_lastFocusedElement is null || _extractedGeometry is null)
                return string.Empty;

            var (kind, idx) = _lastFocusedElement.Value;
            var ic = CultureInfo.InvariantCulture;

            try
            {
                switch (kind)
                {
                    case "slab":
                        if (idx < 0 || idx >= _extractedGeometry.Slabs.Count) return string.Empty;
                        {
                            var pts = _extractedGeometry.Slabs[idx];
                            (byte R, byte G, byte B) color = idx < _extractedGeometry.SlabColors.Count
                                ? _extractedGeometry.SlabColors[idx] : ((byte)0, (byte)0, (byte)0);
                            double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
                            double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
                            var centroid = PolygonProcessor.Centroid(pts);
                            return $"User last focused slab[{idx}] — colour #{color.R:X2}{color.G:X2}{color.B:X2}, " +
                                   $"bbox {(maxX - minX).ToString("0", ic)}x{(maxY - minY).ToString("0", ic)}mm, " +
                                   $"centroid ({centroid.X.ToString("0", ic)},{centroid.Y.ToString("0", ic)})mm.";
                        }
                    case "line":
                        if (idx < 0 || idx >= _extractedGeometry.Lines.Count) return string.Empty;
                        {
                            var pts = _extractedGeometry.Lines[idx];
                            (byte R, byte G, byte B) color = idx < _extractedGeometry.LineColors.Count
                                ? _extractedGeometry.LineColors[idx] : ((byte)0, (byte)0, (byte)0);
                            double len = PolygonProcessor.PathLength(pts);
                            return $"User last focused line[{idx}] — colour #{color.R:X2}{color.G:X2}{color.B:X2}, " +
                                   $"{pts.Count} pts, length {len.ToString("0", ic)}mm.";
                        }
                    case "column":
                        if (idx < 0 || idx >= _extractedGeometry.Columns.Count) return string.Empty;
                        {
                            var (x, y) = _extractedGeometry.Columns[idx];
                            (byte R, byte G, byte B) color = idx < _extractedGeometry.ColumnColors.Count
                                ? _extractedGeometry.ColumnColors[idx] : ((byte)0, (byte)0, (byte)0);
                            var (w, d) = idx < _extractedGeometry.ColumnSizes.Count
                                ? _extractedGeometry.ColumnSizes[idx] : (0d, 0d);
                            return $"User last focused column[{idx}] — colour #{color.R:X2}{color.G:X2}{color.B:X2}, " +
                                   $"centroid ({x.ToString("0", ic)},{y.ToString("0", ic)})mm, " +
                                   $"section {w.ToString("0", ic)}x{d.ToString("0", ic)}mm.";
                        }
                }
            }
            catch { /* best-effort — never throw from context building */ }

            return string.Empty;
        }

        /// <summary>
        /// Extras emitted inline for each shape: per-element type overrides,
        /// exclusion state by index, and same by color. Kept on one line so the
        /// context remains compact.
        /// </summary>
        private string BuildElementExtras(string kind, int index, (byte R, byte G, byte B) color)
        {
            var parts = new System.Collections.Generic.List<string>();

            System.Collections.Generic.Dictionary<int, string>? overrides = kind switch
            {
                "slab"   => _excl.SlabTypeOverrides,
                "line"   => _excl.LineTypeOverrides,
                "column" => _excl.ColumnTypeOverrides,
                _        => null
            };
            if (overrides is not null && overrides.TryGetValue(index, out var ov))
                parts.Add($"override={ov}");

            bool indexExcluded = kind switch
            {
                "slab"   => _excl.Slabs.Contains(index),
                "line"   => _excl.Lines.Contains(index),
                "column" => _excl.Columns.Contains(index),
                _        => false
            };
            if (indexExcluded) parts.Add("excluded=true");

            if (_excl.Colors.Contains(color)) parts.Add("colorExcluded=true");

            return parts.Count == 0 ? string.Empty : "  " + string.Join(" ", parts);
        }
    }
}
