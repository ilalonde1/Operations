#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Kor.Operations.EngineeringTools.QuantityTakeoff;

namespace Kor.Operations.EngineeringTools.RebarChange
{
    public partial class RebarChangeWindow : Window
    {
        private string? _beforePdf;
        private string? _afterPdf;

        public RebarChangeWindow()
        {
            InitializeComponent();
        }

        private void PickBefore_Click(object sender, RoutedEventArgs e)
        {
            if (PickPdf(out var p)) { _beforePdf = p; BeforeStatus.Text = Path.GetFileName(p); }
        }

        private void PickAfter_Click(object sender, RoutedEventArgs e)
        {
            if (PickPdf(out var p)) { _afterPdf = p; AfterStatus.Text = Path.GetFileName(p); }
        }

        private void FillBeforeFromCsv_Click(object sender, RoutedEventArgs e) => FillFromCsv(before: true);
        private void FillAfterFromCsv_Click(object sender, RoutedEventArgs e) => FillFromCsv(before: false);

        private bool PickPdf(out string path)
        {
            path = string.Empty;
            var ofd = new OpenFileDialog
            {
                Title = "Select a structural drawing PDF",
                Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
            };
            if (ofd.ShowDialog(this) != true) return false;
            path = ofd.FileName;
            return true;
        }

        // Reads a concrete schedule CSV and drops the per-element totals into the volume boxes.
        private void FillFromCsv(bool before)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Select a concrete schedule (CSV) — Level, Element, ConcreteM3",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            };
            if (ofd.ShowDialog(this) != true) return;
            try
            {
                var lines = TakeoffCsvImporter.Import(File.ReadAllText(ofd.FileName), RebarDensityTable.Default);
                var vols = new Dictionary<string, double>();
                foreach (var l in lines)
                    vols[ElementKey(l.ElementType)] = vols.GetValueOrDefault(ElementKey(l.ElementType)) + l.ConcreteM3;

                (before ? SlabBefore : SlabAfter).Text = vols.GetValueOrDefault("Slab").ToString("0.#");
                (before ? WallBefore : WallAfter).Text = vols.GetValueOrDefault("Wall").ToString("0.#");
                (before ? ColBefore : ColAfter).Text = vols.GetValueOrDefault("Column").ToString("0.#");
                (before ? FdnBefore : FdnAfter).Text = vols.GetValueOrDefault("Foundation").ToString("0.#");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not read CSV: {ex.Message}", "Rebar Takeoff");
            }
        }

        private static string ElementKey(TakeoffElementType t) => t switch
        {
            TakeoffElementType.Wall => "Wall",
            TakeoffElementType.Column => "Column",
            TakeoffElementType.Foundation => "Foundation",
            _ => "Slab", // Slab, Beam, DropPanel folded into Slab for the high-level takeoff
        };

        private static double Num(TextBox box) =>
            double.TryParse(box.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;

        // Returns true if the boxes carry enough to compute weight (both issues have volume).
        private bool TryReadVolumes(out Dictionary<string, double> vb, out Dictionary<string, double> va)
        {
            vb = new Dictionary<string, double>
            { ["Slab"] = Num(SlabBefore), ["Wall"] = Num(WallBefore), ["Column"] = Num(ColBefore), ["Foundation"] = Num(FdnBefore) };
            va = new Dictionary<string, double>
            { ["Slab"] = Num(SlabAfter), ["Wall"] = Num(WallAfter), ["Column"] = Num(ColAfter), ["Foundation"] = Num(FdnAfter) };
            return vb.Values.Sum() > 0 && va.Values.Sum() > 0;
        }

        private void Generate_Click(object sender, RoutedEventArgs e)
        {
            if (_beforePdf is null || _afterPdf is null)
            {
                MessageBox.Show("Pick a BEFORE and an AFTER issue PDF first.", "Rebar Takeoff");
                return;
            }

            bool withWeight = TryReadVolumes(out var vb, out var va);
            var sfd = new SaveFileDialog
            {
                Title = "Save rebar report",
                Filter = "Excel workbook (*.xlsx)|*.xlsx",
                FileName = $"{ProjectNameBox.Text.Trim()} - Rebar " +
                           (withWeight ? "Takeoff & Change" : "Change Detection") + ".xlsx",
            };
            if (sfd.ShowDialog(this) != true) return;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                ResultStatus.Text = "Reading PDFs…";

                var beforePages = PdfPageTextReader.ReadPages(_beforePdf);
                var afterPages = PdfPageTextReader.ReadPages(_afterPdf);

                var change = RebarChangeService.Compare(
                    beforePages, afterPages,
                    BeforeLabelBox.Text.Trim(), AfterLabelBox.Text.Trim());

                PreviewGrid.ItemsSource = change.Sheets
                    .Where(s => s.Status != RebarChangeStatus.Unchanged)
                    .OrderBy(s => s.Status == RebarChangeStatus.Changed ? 0 : 1)
                    .Select(s => new SheetRow(
                        s.Sheet, s.Title, StatusText(s.Status),
                        s.BeforeCount, s.AfterCount, s.NetDelta,
                        string.Join("; ", s.Added.Concat(s.Removed))))
                    .ToList();

                byte[] bytes;
                if (withWeight)
                {
                    var corr = RebarWeightEstimator.Corroborate(afterPages);
                    var intB = RebarWeightEstimator.CalloutIntensity(beforePages);
                    var intA = RebarWeightEstimator.CalloutIntensity(afterPages);
                    var weight = RebarWeightEstimator.Estimate(
                        vb, va, RebarWeightEstimator.DefaultDensities, corr,
                        BeforeLabelBox.Text.Trim(), AfterLabelBox.Text.Trim(), intB, intA);
                    var priced = RebarGridPricer.Compare(
                        beforePages, afterPages, null, null,
                        BeforeLabelBox.Text.Trim(), AfterLabelBox.Text.Trim());
                    bytes = RebarChangeReportGenerator.BuildFull(change, weight, ProjectNameBox.Text.Trim(), priced);
                    ResultStatus.Text =
                        $"{Math.Round(weight.TotalAfter)} t (Δ {weight.TotalDelta:+0.0;-0.0;0}) · " +
                        $"{change.SheetsChanged}/{change.SheetsCompared} sheets changed · " +
                        $"{priced.Changes.Count} field-grid change(s) — saved.";
                }
                else
                {
                    bytes = RebarChangeReportGenerator.BuildXlsx(change, ProjectNameBox.Text.Trim());
                    ResultStatus.Text =
                        $"{change.SheetsChanged} of {change.SheetsCompared} sheets changed — saved. " +
                        "(Enter concrete volumes for weight.)";
                }

                File.WriteAllBytes(sfd.FileName, bytes);
                Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not generate report: {ex.Message}", "Rebar Takeoff",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        // Renders the on-drawing "spot the difference" markup: each changed call-out boxed in
        // place (removed=red on the BEFORE sheet, added=green on the AFTER sheet), paired.
        private void GenerateOverlay_Click(object sender, RoutedEventArgs e)
        {
            if (_beforePdf is null || _afterPdf is null)
            {
                MessageBox.Show("Pick a BEFORE and an AFTER issue PDF first.", "Rebar Takeoff");
                return;
            }

            var sfd = new SaveFileDialog
            {
                Title = "Save visual markup",
                Filter = "PDF (*.pdf)|*.pdf",
                FileName = $"{ProjectNameBox.Text.Trim()} - Rebar Changes (highlighted).pdf",
            };
            if (sfd.ShowDialog(this) != true) return;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                ResultStatus.Text = "Rendering markup…";
                var bytes = RebarOverlayGenerator.Build(
                    _beforePdf, _afterPdf,
                    ProjectNameBox.Text.Trim(),
                    BeforeLabelBox.Text.Trim(), AfterLabelBox.Text.Trim());
                File.WriteAllBytes(sfd.FileName, bytes);
                ResultStatus.Text = "Visual markup saved.";
                Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not generate markup: {ex.Message}", "Rebar Takeoff",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private static string StatusText(RebarChangeStatus s) => s switch
        {
            RebarChangeStatus.Changed => "CHANGED",
            RebarChangeStatus.NewSheet => "NEW (verify)",
            RebarChangeStatus.RemovedSheet => "REMOVED (verify)",
            _ => "unchanged"
        };

        private sealed record SheetRow(
            string Sheet, string Title, string Status,
            int BeforeCount, int AfterCount, int NetDelta, string ChangeText);
    }
}
