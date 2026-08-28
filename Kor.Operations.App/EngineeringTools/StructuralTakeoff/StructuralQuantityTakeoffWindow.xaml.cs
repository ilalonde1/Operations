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
using Kor.Operations.EngineeringTools.Dxf;
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
        /// <summary>Where a set of rows came from and what it rests on, carried from whichever
        /// importer produced them onto the workbook and the window.</summary>
        private sealed record TakeoffBasis(string ConcreteBasis, List<string> Assumptions, string? FoundationNote);

        private IReadOnlyList<StructuralTakeoffInput>? _takeoffInputs;
        private TakeoffBasis? _takeoffBasis;
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
                _takeoffBasis = null;
                ShowBasis(null);
                TakeoffCsvStatus.Text = $"{Path.GetFileName(ofd.FileName)} — {_takeoffInputs.Count} rows";
                TakeoffGrid.ItemsSource = null;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not read CSV: {ex.Message}", "Structural Quantity Takeoff",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// The takeoff that costs nothing to measure. A model we generated from the drawings already
        /// states every slab outline, wall thickness and column section, placed on its storey and
        /// checked against the shipped-model invariants — this restates it in the units an estimator
        /// prices, so the takeoff and the engineer's model cannot disagree about the building.
        /// </summary>
        private void ImportTakeoffFromE2k_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Select a finished ETABS model (.e2k)",
                Filter = "ETABS model (*.e2k)|*.e2k|All files (*.*)|*.*",
            };
            if (ofd.ShowDialog(this) != true) return;

            try
            {
                // The storey vocabulary is the firm's, from the same rules the model was built with.
                IReadOnlyCollection<string>? roofWords = null, parkadeWords = null;
                string rulesNote;
                try
                {
                    var settings = RuleSettings.Load();
                    roofWords = settings.ListOr("dxf.roof-words", Array.Empty<string>());
                    parkadeWords = settings.ListOr("dxf.parkade-words", Array.Empty<string>());
                    if (roofWords.Count == 0) roofWords = null;
                    if (parkadeWords.Count == 0) parkadeWords = null;
                    rulesNote = "Roof and parkade slabs were identified using the firm's own storey words from KorStandards.";
                }
                catch (Exception dbEx)
                {
                    rulesNote = "The rules database was unreachable (" + dbEx.Message.Split('\n')[0].Trim()
                        + "), so the built-in roof and parkade words were used instead of the firm's.";
                }

                var takeoff = E2kQuantityTakeoff.Read(E2kDocument.Load(ofd.FileName), Unit, roofWords, parkadeWords);

                if (takeoff.Inputs.Count == 0)
                {
                    MessageBox.Show(
                        "There is no priceable concrete in that model.\n\n"
                        + string.Join("\n", takeoff.Residual.Take(6).Select(r => $"• {r.Storey} {r.Object}: {r.Note}")),
                        "Structural Quantity Takeoff", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _takeoffInputs = takeoff.Inputs;
                _takeoffBasis = new TakeoffBasis(
                    "Concrete volume is the ETABS model's own geometry — the slab outlines, wall thicknesses and "
                    + "column sections read from the drawings, placed on their storeys and checked against the "
                    + "shipped-model invariants. The takeoff and the engineer's model cannot disagree about the building.",
                    takeoff.Flags.Select(f => f.Note)
                        .Append(rulesNote)
                        .Concat(takeoff.Residual.Where(r => r.Kind != "foundation")
                            .GroupBy(r => r.Kind)
                            .Select(g => $"NOT PRICED — {g.Count()} {g.Key}(s). First: {g.First().Storey} {g.First().Object} — {g.First().Note}"))
                        .ToList(),
                    takeoff.Residual.FirstOrDefault(r => r.Kind == "foundation")?.Note);

                ShowBasis(_takeoffBasis);
                TakeoffGrid.ItemsSource = null;

                string aU = Unit == UnitSystem.Imperial ? "sq ft" : "m²";
                TakeoffCsvStatus.Text =
                    $"{Path.GetFileName(ofd.FileName)} — {takeoff.ObjectsRead} objects, {_takeoffInputs.Count} rows"
                    + (takeoff.OpeningAreaDeducted > 0 ? $", {takeoff.OpeningAreaDeducted:N0} {aU} of openings deducted" : "");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not read the model: {ex.Message}", "Structural Quantity Takeoff",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Revit's schedules, exactly as Revit exports them — title row, header on the second row,
        /// the level named differently in every category, the unit inside each cell. Select all of
        /// them at once; the element type comes from each schedule's own title.
        /// </summary>
        private void ImportTakeoffFromRevit_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Select the Revit schedule exports — floors, walls, columns, foundations",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                Multiselect = true,
            };
            if (ofd.ShowDialog(this) != true || ofd.FileNames.Length == 0) return;

            try
            {
                var revit = RevitScheduleImporter.Import(
                    ofd.FileNames.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                       .Select(f => (Path.GetFileName(f), File.ReadAllText(f))));

                if (revit.Inputs.Count == 0)
                {
                    MessageBox.Show(
                        $"Nothing priceable in those {ofd.FileNames.Length} file(s).\n\n"
                        + string.Join("\n", revit.Residual.Take(6).Select(r => $"• {r.Source}: {r.Note}")),
                        "Structural Quantity Takeoff", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // THE SCHEDULE'S UNIT DECIDES, NOT THE RADIO BUTTON.
                //
                // Revit writes the unit in every cell. Pricing a cubic-metre schedule against the
                // imperial density table would be wrong by the volume ratio and nothing on the page
                // would say so, so the import moves the toggle and says that it did.
                bool switched = Unit != revit.Unit;
                if (revit.Unit == UnitSystem.Imperial) ImperialRadio.IsChecked = true;
                else MetricRadio.IsChecked = true;

                _takeoffInputs = revit.Inputs;
                _takeoffBasis = new TakeoffBasis(
                    "Concrete volume is Revit's own, read straight from the exported schedules — modelled solid "
                    + "geometry, so every thickening, drop and transfer the model carries is already in it.",
                    revit.Notes
                        .Concat(switched
                            ? new[] { $"The unit was read from the schedules and the takeoff was switched to {revit.Unit}." }
                            : Array.Empty<string>())
                        .Concat(revit.Residual.GroupBy(r => r.Source)
                            .Select(g => $"NOT PRICED — {g.Count()} row(s) from {g.Key}: {g.First().Note}"))
                        .ToList(),
                    FoundationNote: null);

                ShowBasis(_takeoffBasis);
                TakeoffGrid.ItemsSource = null;
                TakeoffCsvStatus.Text =
                    $"{ofd.FileNames.Length} schedule(s) — {revit.RowsRead} rows read, {_takeoffInputs.Count} priced";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not read the schedules: {ex.Message}", "Structural Quantity Takeoff",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowBasis(TakeoffBasis? basis)
        {
            var lines = basis?.Assumptions ?? new List<string>();
            TakeoffBasisList.ItemsSource = lines;
            TakeoffBasisPanel.Visibility = lines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void GenerateTakeoff_Click(object sender, RoutedEventArgs e)
        {
            if (_takeoffInputs is null || _takeoffInputs.Count == 0)
            {
                MessageBox.Show("Load an ETABS model, Revit's schedules, or a prepared CSV first.",
                    "Structural Quantity Takeoff");
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
                // Whatever the source said about itself travels onto the workbook: an estimator
                // reads the xlsx, not this window.
                var model = new StructuralTakeoffReportModel(
                    Wbs1Box.Text.Trim(), ProjectNameBox.Text.Trim(),
                    TakeoffIssueBox.Text.Trim(), DateTime.UtcNow, result,
                    ConcreteBasis: _takeoffBasis?.ConcreteBasis,
                    FoundationNote: _takeoffBasis?.FoundationNote,
                    Assumptions: _takeoffBasis?.Assumptions);
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
