#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Kor.Operations.EngineeringTools.RebarChange;

namespace Kor.Operations.EngineeringTools.StructuralTakeoff
{
    /// <summary>
    /// Consolidated Structural Quantity Takeoff window. One tool, two modes that share the project
    /// header and the metric/imperial toggle:
    ///   • Single-Issue Takeoff — absolute per-floor concrete + reinforcing + formwork estimate.
    ///   • Compare Two Issues   — reinforcing change-list + on-drawing visual markup (Rory's compare).
    /// All measurement logic lives in EngineeringTools.Core; this window is intake + dispatch + output.
    /// </summary>
    public partial class StructuralQuantityTakeoffWindow : Window
    {
        private IReadOnlyList<StructuralTakeoffInput>? _takeoffInputs;
        private string? _beforePdf;
        private string? _afterPdf;

        public StructuralQuantityTakeoffWindow()
        {
            InitializeComponent();
        }

        private UnitSystem Unit => ImperialRadio.IsChecked == true ? UnitSystem.Imperial : UnitSystem.Metric;

        private string ProjectLabel
        {
            get
            {
                string wbs = Wbs1Box.Text.Trim();
                string name = ProjectNameBox.Text.Trim();
                return string.IsNullOrEmpty(wbs) ? name : $"{name} ({wbs})";
            }
        }

        // ===================== Single-Issue Absolute Takeoff =====================

        private void ImportTakeoffCsv_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Select a concrete schedule (CSV) — Level, Element, Variant, ConcreteVolume…",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            };
            if (ofd.ShowDialog(this) != true) return;
            try
            {
                _takeoffInputs = StructuralTakeoffCsvImporter.Import(File.ReadAllText(ofd.FileName));
                TakeoffCsvStatus.Text = $"{Path.GetFileName(ofd.FileName)} — {_takeoffInputs.Count} rows";
                TakeoffGrid.ItemsSource = null;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not read CSV: {ex.Message}", "Structural Quantity Takeoff",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GenerateTakeoff_Click(object sender, RoutedEventArgs e)
        {
            if (_takeoffInputs is null || _takeoffInputs.Count == 0)
            {
                MessageBox.Show("Import a concrete schedule (CSV) first.", "Structural Quantity Takeoff");
                return;
            }

            var densities = Unit == UnitSystem.Imperial
                ? StructuralDensityTable.KorImperialDefault
                : StructuralDensityTable.KorMetricDefault;

            var result = StructuralTakeoffService.Compute(_takeoffInputs, densities);
            TakeoffGrid.ItemsSource = result.Lines;

            var sfd = new SaveFileDialog
            {
                Title = "Save structural quantity takeoff",
                Filter = "Excel workbook (*.xlsx)|*.xlsx",
                FileName = $"{Wbs1Box.Text.Trim()} - Structural Quantity Takeoff.xlsx",
            };
            if (sfd.ShowDialog(this) != true) return;

            try
            {
                var model = new StructuralTakeoffReportModel(
                    Wbs1Box.Text.Trim(), ProjectNameBox.Text.Trim(),
                    TakeoffIssueBox.Text.Trim(), DateTime.UtcNow, result);
                File.WriteAllBytes(sfd.FileName, StructuralTakeoffReportGenerator.BuildXlsx(model));

                string vU = Unit == UnitSystem.Imperial ? "cu.yd" : "m³";
                string wU = Unit == UnitSystem.Imperial ? "lb" : "kg";
                TakeoffResultStatus.Text =
                    $"{result.TotalConcreteVolume:N0} {vU} concrete · {result.TotalRebarWeight:N0} {wU} reinforcing — saved.";
                Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not write takeoff: {ex.Message}", "Structural Quantity Takeoff",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ===================== Compare Two Issues =====================

        private void PickBefore_Click(object sender, RoutedEventArgs e)
        {
            if (PickPdf(out var p)) { _beforePdf = p; BeforeStatus.Text = Path.GetFileName(p); }
        }

        private void PickAfter_Click(object sender, RoutedEventArgs e)
        {
            if (PickPdf(out var p)) { _afterPdf = p; AfterStatus.Text = Path.GetFileName(p); }
        }

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

        private void FillBeforeFromCsv_Click(object sender, RoutedEventArgs e) => FillFromCsv(before: true);
        private void FillAfterFromCsv_Click(object sender, RoutedEventArgs e) => FillFromCsv(before: false);

        private void FillFromCsv(bool before)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Select a concrete schedule (CSV) — Level, Element, ConcreteVolume",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            };
            if (ofd.ShowDialog(this) != true) return;
            try
            {
                var inputs = StructuralTakeoffCsvImporter.Import(File.ReadAllText(ofd.FileName));
                var vols = new Dictionary<string, double>();
                foreach (var i in inputs)
                    vols[Bucket(i.Element)] = vols.GetValueOrDefault(Bucket(i.Element)) + i.ConcreteVolume;

                (before ? SlabBefore : SlabAfter).Text = vols.GetValueOrDefault("Slab").ToString("0.#");
                (before ? WallBefore : WallAfter).Text = vols.GetValueOrDefault("Wall").ToString("0.#");
                (before ? ColBefore : ColAfter).Text = vols.GetValueOrDefault("Column").ToString("0.#");
                (before ? FdnBefore : FdnAfter).Text = vols.GetValueOrDefault("Foundation").ToString("0.#");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not read CSV: {ex.Message}", "Structural Quantity Takeoff");
            }
        }

        private static string Bucket(TakeoffElementType t) => t switch
        {
            TakeoffElementType.Wall => "Wall",
            TakeoffElementType.Column => "Column",
            TakeoffElementType.Foundation => "Foundation",
            _ => "Slab",
        };

        private static double Num(TextBox box) =>
            double.TryParse(box.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;

        private bool TryReadVolumes(out Dictionary<string, double> vb, out Dictionary<string, double> va)
        {
            vb = new Dictionary<string, double>
            { ["Slab"] = Num(SlabBefore), ["Wall"] = Num(WallBefore), ["Column"] = Num(ColBefore), ["Foundation"] = Num(FdnBefore) };
            va = new Dictionary<string, double>
            { ["Slab"] = Num(SlabAfter), ["Wall"] = Num(WallAfter), ["Column"] = Num(ColAfter), ["Foundation"] = Num(FdnAfter) };
            return vb.Values.Sum() > 0 && va.Values.Sum() > 0;
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows10.0.10240.0")]
        private async void GenerateChangeReport_Click(object sender, RoutedEventArgs e)
        {
            if (_beforePdf is null || _afterPdf is null)
            {
                MessageBox.Show("Pick a BEFORE and an AFTER issue PDF first.", "Structural Quantity Takeoff");
                return;
            }

            // Weight delta uses the metric reinforcing estimator; offered only in metric mode so we
            // never print imperial volumes against metric densities. Imperial tonnage = Single-Issue tab.
            bool volumesEntered = TryReadVolumes(out var vb, out var va);
            bool withWeight = volumesEntered && Unit == UnitSystem.Metric;

            var sfd = new SaveFileDialog
            {
                Title = "Save change report",
                Filter = "Excel workbook (*.xlsx)|*.xlsx",
                FileName = $"{Wbs1Box.Text.Trim()} - Rebar " +
                           (withWeight ? "Takeoff & Change" : "Change Detection") + ".xlsx",
            };
            if (sfd.ShowDialog(this) != true) return;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                CompareResultStatus.Text = "Reading PDFs…";

                var beforeRead = await PdfTextWithOcr.ReadAsync(_beforePdf);
                var afterRead = await PdfTextWithOcr.ReadAsync(_afterPdf);
                var beforePages = beforeRead.Pages;
                var afterPages = afterRead.Pages;

                var change = RebarChangeService.Compare(
                    beforePages, afterPages,
                    BeforeLabelBox.Text.Trim(), AfterLabelBox.Text.Trim(), Unit);

                ComparePreviewGrid.ItemsSource = change.Sheets
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
                    bytes = RebarChangeReportGenerator.BuildFull(change, weight, ProjectLabel, priced);
                    CompareResultStatus.Text =
                        $"{Math.Round(weight.TotalAfter)} t (Δ {weight.TotalDelta:+0.0;-0.0;0}) · " +
                        $"{change.SheetsChanged}/{change.SheetsCompared} sheets changed{OcrNote(beforeRead, afterRead)} — saved.";
                }
                else
                {
                    bytes = RebarChangeReportGenerator.BuildXlsx(change, ProjectLabel);
                    string hint = volumesEntered
                        ? " (imperial weight: use the Single-Issue Takeoff tab.)"
                        : " (enter concrete volumes for a weight delta.)";
                    CompareResultStatus.Text =
                        $"{change.SheetsChanged} of {change.SheetsCompared} sheets changed{OcrNote(beforeRead, afterRead)} — saved.{hint}";
                }

                File.WriteAllBytes(sfd.FileName, bytes);
                Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not generate report: {ex.Message}", "Structural Quantity Takeoff",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void GenerateOverlay_Click(object sender, RoutedEventArgs e)
        {
            if (_beforePdf is null || _afterPdf is null)
            {
                MessageBox.Show("Pick a BEFORE and an AFTER issue PDF first.", "Structural Quantity Takeoff");
                return;
            }

            var sfd = new SaveFileDialog
            {
                Title = "Save visual markup",
                Filter = "PDF (*.pdf)|*.pdf",
                FileName = $"{Wbs1Box.Text.Trim()} - Rebar Changes (highlighted).pdf",
            };
            if (sfd.ShowDialog(this) != true) return;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                CompareResultStatus.Text = "Rendering markup…";
                var bytes = RebarOverlayGenerator.Build(
                    _beforePdf, _afterPdf, ProjectLabel,
                    BeforeLabelBox.Text.Trim(), AfterLabelBox.Text.Trim(), Unit);
                File.WriteAllBytes(sfd.FileName, bytes);
                CompareResultStatus.Text = "Visual markup saved.";
                Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not generate markup: {ex.Message}", "Structural Quantity Takeoff",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private static string OcrNote(PdfReadResult before, PdfReadResult after)
        {
            int n = before.OcrPageNumbers.Count + after.OcrPageNumbers.Count;
            return n > 0 ? $" · {n} image-only page(s) OCR-recovered (verify)" : string.Empty;
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
