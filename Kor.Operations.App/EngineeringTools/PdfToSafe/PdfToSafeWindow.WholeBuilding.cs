#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Kor.Operations.EngineeringTools.Intake;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// BUILD WHOLE BUILDING (ETABS), 2026-10-20. Ian: "wouldn't the ability to do ALL floors SUPERCEDE the ability to do
    /// ONE FLOOR?" The window's own exports are one page, one storey. This button runs the whole-set route - every plan
    /// sheet, the storey ladder, the composer - through <see cref="StickfileRun"/>, the same code `takeoff stickfile`
    /// runs, so the app and the command cannot drift. SAFE for a floor of it comes from ETABS's own storey export.
    /// </summary>
    public partial class PdfToSafeWindow
    {
        /// <summary>The scale the load READ off the sheet; null when it was assumed or typed, so the engine's own
        /// default applies and its report says so - the same as the command run without --scale.</summary>
        private int? _readScale;

        private async void BuildWholeBuilding_Click(object sender, RoutedEventArgs e)
        {
            string? pdf = _loadedFilePath;
            if (string.IsNullOrWhiteSpace(pdf) || !File.Exists(pdf))
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Select the drawing set (PDF)", Filter = "PDF files (*.pdf)|*.pdf" };
                if (dlg.ShowDialog() != true) return;
                pdf = dlg.FileName;
                _readScale = null;
            }

            // A missing rule stops a production run by design: there is no fallback value, and a
            // model built from built-in numbers is not a model built to the office's standard.
            string? rules = Environment.GetEnvironmentVariable("KOR_ENGINEERINGTOOLS_STANDARDSDB");
            if (string.IsNullOrWhiteSpace(rules))
            {
                SetStatus("KOR_ENGINEERINGTOOLS_STANDARDSDB is not set on this machine, so the rules in KorStandards " +
                          "cannot be read. Building from built-in values would produce a model to nobody's standard, " +
                          "so this run is refused.", "#FEE2E2", "#991B1B");
                return;
            }

            string folder = NextModelFolder(pdf);
            int? scale = _readScale;
            BuildWholeBuildingButton.IsEnabled = false;
            LoadPdfButton.IsEnabled = false;
            try
            {
                ShowLoading("Building the whole building — every plan sheet. A full set takes a few minutes…");
                SetStatusBusy("Building the whole building…");
                var r = await Task.Run(() => StickfileRun.Run(pdf, folder, scale, job: null, rulesConnection: rules)).ConfigureAwait(true);
                SetStatus(WholeBuildingStatus(r, folder), r.Built.Model is null ? "#FEE2E2" : "#E8F5E9", r.Built.Model is null ? "#991B1B" : "#2E7D32");
                ClearStatusBusy();
                Open(folder);
                if (r.Png is not null) Open(r.Png);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Whole-building build failed for {Pdf}", pdf);
                SetStatus($"The whole-building build failed: {ex.Message}", "#FEE2E2", "#991B1B");
            }
            finally
            {
                HideLoading();
                BuildWholeBuildingButton.IsEnabled = true;
                LoadPdfButton.IsEnabled = true;
            }
        }

        /// <summary>What the engineer reads first: the model or why there is none, the counts, the questions, the flags.</summary>
        internal static string WholeBuildingStatus(StickfileRun.Result r, string folder)
        {
            var sb = new System.Text.StringBuilder();
            var built = r.Built;
            if (built.Model is null)
            {
                sb.AppendLine("NO MODEL WAS BUILT.").AppendLine(built.ModelError)
                  .Append("What was read is in ").Append(Path.GetFileName(folder)).Append(" (sheets.csv, dxf).");
                return sb.ToString();
            }
            var m = built.Model;
            sb.Append("Whole building → ").Append(Path.GetFileName(folder)).AppendLine("\\out.e2k")
              .Append(m.SavedModel.Storeys.Count).Append(" storeys (").Append(m.PlatesByStorey.Count(p => p.Value > 0)).Append(" with a floor) · ")
              .Append(m.SavedModel.Walls).Append(" walls · ").Append(m.SavedModel.Columns).Append(" columns · ")
              .Append(m.SavedModel.Floors).AppendLine(" floors")
              .Append("Rules: ").AppendLine(r.RulesSource);
            // DrawingIntake: a sheet's stated scale wins; this is only the fallback for a sheet that states none.
            sb.AppendLine(r.ScaleAssumed
                ? "Scale: each sheet's stated scale is used; a sheet that states none falls back to 1/8\" = 1'-0\" (96), ASSUMED."
                : $"Scale: each sheet's stated scale is used; a sheet that states none falls back to 1:{r.Scale}, as read on the loaded page.");
            sb.AppendLine(r.Questions is not null
                ? $"Questions for the engineer: {r.OpenQuestions} open in questions.xlsx"
                : $"Questions workbook not written: {r.QuestionsError}");
            if (r.Png is null) sb.AppendLine($"Picture not written: {r.RenderError}");
            if (r.ReadFirst.Count > 0)
            {
                sb.AppendLine("Read these first:");
                foreach (string w in r.ReadFirst.Take(6))
                    sb.Append("  ⚠ ").AppendLine(w.Length > 300 ? w[..300] + " …" : w);
                if (r.ReadFirst.Count > 6) sb.Append("  … and ").Append(r.ReadFirst.Count - 6).AppendLine(" more in report.txt");
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// A new folder beside the PDF, "&lt;pdf name&gt; - ETABS model - &lt;date&gt; vN", N one past the highest already there
        /// for that PDF. Never an existing folder: the build deletes the folder it is given before it writes.
        /// </summary>
        internal static string NextModelFolder(string pdf)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(pdf))!;
            string stem = Path.GetFileNameWithoutExtension(pdf);
            var version = new Regex("^" + Regex.Escape(stem) + @" - ETABS model - \d{4}-\d{2}-\d{2} v(\d+)\b", RegexOptions.IgnoreCase);
            int highest = Directory.EnumerateDirectories(dir, stem + " - ETABS model - *")
                .Select(d => version.Match(Path.GetFileName(d)))
                .Where(m => m.Success)
                .Select(m => int.Parse(m.Groups[1].Value))
                .DefaultIfEmpty(0)
                .Max();
            return Path.Combine(dir, $"{stem} - ETABS model - {DateTime.Now:yyyy-MM-dd} v{highest + 1}");
        }

        private void Open(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not open {Path}", path); }
        }
    }
}
