#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// Builds the human-readable summary sidecar that lives next to the F2K
    /// output. Engineer reads this before launching SAFE — gets model counts,
    /// section inventory, opening locations, warnings, and a pre-flight
    /// checklist in 5 seconds without paying the 30-second SAFE startup.
    /// </summary>
    internal static class ExportSummaryReport
    {
        public record SummaryCounts(int SlabCount, int WallCount, int ColumnCount, int OpeningCount);

        /// <summary>
        /// Pure function — composes the summary text from the inputs. No I/O,
        /// no time-of-day dependency (caller supplies the timestamp). Easy to
        /// snapshot-test.
        /// </summary>
        public static string Build(
            string? sourcePdfName,
            string outputF2kName,
            DateTime generatedAt,
            ExtractedGeometry reclassified,
            SummaryCounts counts,
            ValidationResult validation,
            IReadOnlyList<(int ParentSlabIndex, List<(double X, double Y)> Polygon)> openings)
        {
            var ic = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(capacity: 2048);

            sb.AppendLine("KOR Operations - Structural PDF Import");
            sb.AppendLine("F2K Export Summary");
            sb.AppendLine("======================================");
            sb.AppendLine($"  Source PDF : {sourcePdfName ?? "(unknown)"}");
            sb.AppendLine($"  Output F2K : {outputF2kName}");
            sb.AppendLine($"  Generated  : {generatedAt:yyyy-MM-dd HH:mm}");
            sb.AppendLine();

            sb.AppendLine("MODEL COUNTS");
            sb.AppendLine("------------");
            sb.AppendLine($"  Slabs    : {counts.SlabCount}");
            sb.AppendLine($"  Walls    : {counts.WallCount}");
            sb.AppendLine($"  Columns  : {counts.ColumnCount}");
            sb.AppendLine($"  Openings : {counts.OpeningCount}");
            sb.AppendLine();

            // Column section inventory.
            if (reclassified.Columns.Count > 0)
            {
                var colGroups = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < reclassified.Columns.Count; i++)
                {
                    var (w, d) = i < reclassified.ColumnSizes.Count ? reclassified.ColumnSizes[i] : (0d, 0d);
                    if (w <= 0 || d <= 0) continue;
                    var (secName, _, _) = F2kModelPrep.SnapColumnSection(w, d);
                    colGroups[secName] = colGroups.GetValueOrDefault(secName) + 1;
                }
                if (colGroups.Count > 0)
                {
                    sb.AppendLine("COLUMN SECTIONS USED");
                    sb.AppendLine("--------------------");
                    foreach (var (name, count) in colGroups.OrderByDescending(g => g.Value).ThenBy(g => g.Key))
                        sb.AppendLine($"  {name,-14}  x{count}");
                    sb.AppendLine();
                }
            }

            // Wall section inventory.
            int wallTotal = 0;
            var wallGroups = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reclassified.LineSectionHints.Count; i++)
            {
                var hint = reclassified.LineSectionHints[i];
                if (!hint.HasValue) continue;
                var (secName, _, _) = F2kModelPrep.SnapWallSection(hint.Value.WidthMm, hint.Value.DepthMm);
                wallGroups[secName] = wallGroups.GetValueOrDefault(secName) + 1;
                wallTotal++;
            }
            if (wallTotal > 0)
            {
                sb.AppendLine("WALL SECTIONS USED");
                sb.AppendLine("------------------");
                foreach (var (name, count) in wallGroups.OrderByDescending(g => g.Value).ThenBy(g => g.Key))
                    sb.AppendLine($"  {name,-14}  x{count}");
                sb.AppendLine();
            }

            // Openings.
            if (openings.Count > 0)
            {
                sb.AppendLine($"AUTO-CUT OPENINGS ({openings.Count})");
                sb.AppendLine("------------------");
                for (int i = 0; i < openings.Count; i++)
                {
                    var poly = openings[i].Polygon;
                    double minX = poly.Min(p => p.X), maxX = poly.Max(p => p.X);
                    double minY = poly.Min(p => p.Y), maxY = poly.Max(p => p.Y);
                    sb.AppendLine($"  [{i}] {(maxX - minX).ToString("0", ic)}x{(maxY - minY).ToString("0", ic)}mm " +
                                  $"@ ({((minX + maxX) / 2).ToString("0", ic)},{((minY + maxY) / 2).ToString("0", ic)})mm " +
                                  $"in slab[{openings[i].ParentSlabIndex}]");
                }
                sb.AppendLine();
            }

            // Warnings.
            if (validation.WarningCount > 0)
            {
                sb.AppendLine($"PRE-EXPORT WARNINGS ({validation.WarningCount})");
                sb.AppendLine("-----------------------");
                foreach (var issue in validation.Issues.Where(x => x.Severity == ValidationSeverity.Warning))
                    sb.AppendLine($"  ! {issue.Category}: {issue.Message}");
                sb.AppendLine();
            }

            sb.AppendLine("CHECKLIST BEFORE RUNNING SAFE");
            sb.AppendLine("-----------------------------");
            if (openings.Count > 0)
                sb.AppendLine("  [ ] Verify each auto-cut opening covers the intended shaft void.");
            if (validation.WarningCount > 0)
                sb.AppendLine("  [ ] Review the warnings above; override element types in KOR if needed.");
            sb.AppendLine("  [ ] Confirm slab thicknesses match drawing schedules.");
            sb.AppendLine("  [ ] Confirm column base restraint matches the modelled level (pinned for typical floors).");
            sb.AppendLine("  [ ] Verify design strips orientation matches your slab span direction.");

            return sb.ToString();
        }

        /// <summary>
        /// Writes the summary to <paramref name="f2kPath"/> + ".summary.txt".
        /// Best-effort: failure to write is trace-logged but never thrown.
        /// </summary>
        public static void WriteSidecar(
            string f2kPath,
            string? sourcePdfPath,
            ExtractedGeometry reclassified,
            SummaryCounts counts,
            ValidationResult validation,
            IReadOnlyList<(int ParentSlabIndex, List<(double X, double Y)> Polygon)> openings)
        {
            try
            {
                string content = Build(
                    sourcePdfName: sourcePdfPath is null ? null : Path.GetFileName(sourcePdfPath),
                    outputF2kName: Path.GetFileName(f2kPath),
                    generatedAt: DateTime.Now,
                    reclassified, counts, validation, openings);
                File.WriteAllText(f2kPath + ".summary.txt", content);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning(
                    $"ExportSummaryReport.WriteSidecar: failed to write summary for '{f2kPath}'. {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
