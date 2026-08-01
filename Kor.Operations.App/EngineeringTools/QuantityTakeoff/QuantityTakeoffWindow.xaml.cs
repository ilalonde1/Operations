#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    public partial class QuantityTakeoffWindow : Window
    {
        private IReadOnlyList<TakeoffLineResult>? _before;
        private IReadOnlyList<TakeoffLineResult>? _after;

        public QuantityTakeoffWindow()
        {
            InitializeComponent();
        }

        private void ImportBefore_Click(object sender, RoutedEventArgs e)
        {
            if (TryImport(out var lines, out var name))
            {
                _before = lines;
                BeforeStatus.Text = $"{name} — {lines.Count} rows";
            }
        }

        private void ImportAfter_Click(object sender, RoutedEventArgs e)
        {
            if (TryImport(out var lines, out var name))
            {
                _after = lines;
                AfterStatus.Text = $"{name} — {lines.Count} rows";
            }
        }

        private bool TryImport(out IReadOnlyList<TakeoffLineResult> lines, out string fileName)
        {
            lines = Array.Empty<TakeoffLineResult>();
            fileName = string.Empty;
            var ofd = new OpenFileDialog
            {
                Title = "Select a concrete schedule (CSV)",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            };
            if (ofd.ShowDialog(this) != true) return false;
            try
            {
                lines = TakeoffCsvImporter.Import(File.ReadAllText(ofd.FileName), RebarDensityTable.Default);
                fileName = Path.GetFileName(ofd.FileName);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not read CSV: {ex.Message}", "Quantity Takeoff", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void Generate_Click(object sender, RoutedEventArgs e)
        {
            if (_before is null || _after is null)
            {
                MessageBox.Show("Import a BEFORE and an AFTER CSV first.", "Quantity Takeoff");
                return;
            }

            var diff = TakeoffDiffService.Compare(_before, _after);
            PreviewGrid.ItemsSource = diff.Lines;

            var model = new TakeoffReportModel(
                Wbs1Box.Text.Trim(),
                ProjectNameBox.Text.Trim(),
                BeforeLabelBox.Text.Trim(),
                AfterLabelBox.Text.Trim(),
                DateTime.UtcNow,
                diff);

            var sfd = new SaveFileDialog
            {
                Title = "Save delta report",
                Filter = "Excel workbook (*.xlsx)|*.xlsx",
                FileName = $"{Wbs1Box.Text.Trim()} - Quantity Delta.xlsx",
            };
            if (sfd.ShowDialog(this) != true) return;

            try
            {
                string xlsxPath = sfd.FileName;
                string docxPath = Path.ChangeExtension(xlsxPath, ".docx");
                File.WriteAllBytes(xlsxPath, TakeoffReportGenerator.BuildXlsx(model));
                File.WriteAllBytes(docxPath, TakeoffReportGenerator.BuildDocx(model));

                ResultStatus.Text =
                    $"Δ concrete {diff.TotalConcreteDeltaM3:N1} m³ — saved. (Rebar: use the Rebar Takeoff tool.)";

                Process.Start(new ProcessStartInfo(xlsxPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not write report: {ex.Message}", "Quantity Takeoff", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
