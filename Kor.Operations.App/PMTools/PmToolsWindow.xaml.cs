#nullable enable
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClosedXML.Excel;
using Microsoft.Win32;
using Kor.Operations.Financials;

namespace Kor.Operations.PMTools
{
    public partial class PmToolsWindow : Window
    {
        private readonly PmToolsViewModel _vm = new();
        private CancellationTokenSource? _cts;

        public PmToolsWindow()
        {
            InitializeComponent();
            DataContext = _vm;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _ = ApplyHeaderAsync();
            if (_vm.HasData)
                return;

            _cts = new CancellationTokenSource();
            await _vm.RefreshAsync(forceRefresh: false, _cts.Token);
        }

        private async Task ApplyHeaderAsync()
        {
            try
            {
                await global::Kor.Operations.HeaderLoader.ApplyAsync(HeaderBar);
                _vm.CurrentUserName = HeaderBar.UserDisplayName ?? "";
            }
            catch
            {
            }
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            await _vm.RefreshAsync(forceRefresh: true, _cts.Token);
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
        private void PhaseAll_Click(object sender, RoutedEventArgs e) => _vm.SelectedPhase = "All";
        private void PhaseSD_Click(object sender, RoutedEventArgs e) => _vm.SelectedPhase = "SD";
        private void PhaseDD_Click(object sender, RoutedEventArgs e) => _vm.SelectedPhase = "DD";
        private void PhaseCD_Click(object sender, RoutedEventArgs e) => _vm.SelectedPhase = "CD";
        private void PhaseCA_Click(object sender, RoutedEventArgs e) => _vm.SelectedPhase = "CA";
        private void ShowEngineeringCapacityRisk_Click(object sender, RoutedEventArgs e) => _vm.CapacityRiskViewIndex = 0;
        private void ShowDraftingCapacityRisk_Click(object sender, RoutedEventArgs e) => _vm.CapacityRiskViewIndex = 1;

        private void ProjectGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ProjectGrid.SelectedItem is not PmProjectRow row || !IsDataGridRowDoubleClick(e))
                return;

            var counts = BuildPortfolioCounts();
            var win = new Financials.ProjectFinancialDetailWindow(row.Source, counts) { Owner = this };
            win.Show();
        }

        private void UtilizationGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (UtilizationGrid.SelectedItem is not UtilizationRow row || !IsDataGridRowDoubleClick(e))
                return;

            var counts = BuildPortfolioCounts();
            var win = new Financials.ProjectFinancialDetailWindow(row.Project, counts) { Owner = this };
            win.Show();
        }

        private void DraftUtilizationGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DraftUtilizationGrid.SelectedItem is not DraftUtilizationRow row || !IsDataGridRowDoubleClick(e))
                return;

            var counts = BuildPortfolioCounts();
            var win = new Financials.ProjectFinancialDetailWindow(row.Project, counts) { Owner = this };
            win.Show();
        }

        private async void ExportUtilizationBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.CanExportUtilization)
                return;

            var sfd = new SaveFileDialog
            {
                Title = "Export Utilization",
                Filter = "Excel Workbook|*.xlsx",
                FileName = $"PmUtilization_{DateTime.Now:yyyyMMdd}.xlsx",
                AddExtension = true,
                OverwritePrompt = true,
            };

            if (sfd.ShowDialog(this) != true)
                return;

            var exportEng = _vm.IsEngineeringCapacitySelected;
            _vm.SetExporting(true);
            try
            {
                var path = sfd.FileName;
                await Task.Run(() =>
                {
                    using var wb = new XLWorkbook();
                    var ws = wb.Worksheets.Add("Utilization");

                    string[] headers = exportEng
                        ? new[] { "Project", "Project #", "PM", "Phase", "Eng Budget", "Eng Hours", "Remaining Eng Hours", "% Eng Used", "Risk" }
                        : new[] { "Project", "Project #", "PM", "Phase", "Draft Budget", "Draft Hours", "Remaining Draft Hours", "% Draft Used", "Risk" };

                    for (var c = 0; c < headers.Length; c++)
                    {
                        var cell = ws.Cell(1, c + 1);
                        cell.Value = headers[c];
                        cell.Style.Font.Bold = true;
                    }

                    ws.SheetView.FreezeRows(1);

                    if (exportEng)
                    {
                        var rows = _vm.UtilizationView.Cast<UtilizationRow>().ToList();
                        var ri = 2;
                        foreach (var r in rows)
                        {
                            ws.Cell(ri, 1).Value = r.ProjectName;
                            ws.Cell(ri, 2).Value = r.Wbs1;
                            ws.Cell(ri, 3).Value = r.Pm;
                            ws.Cell(ri, 4).Value = r.Phase;
                            ws.Cell(ri, 5).Value = r.EngBudget; ws.Cell(ri, 5).Style.NumberFormat.Format = "0.0";
                            ws.Cell(ri, 6).Value = r.EngHours; ws.Cell(ri, 6).Style.NumberFormat.Format = "0.0";
                            ws.Cell(ri, 7).Value = r.RemainingEngHours; ws.Cell(ri, 7).Style.NumberFormat.Format = "0.0";
                            ws.Cell(ri, 8).Value = r.PercentEngUsed; ws.Cell(ri, 8).Style.NumberFormat.Format = "0.0%";
                            ws.Cell(ri, 9).Value = r.DeliveryConfidence;
                            ri++;
                        }
                    }
                    else
                    {
                        var rows = _vm.DraftUtilizationView.Cast<DraftUtilizationRow>().ToList();
                        var ri = 2;
                        foreach (var r in rows)
                        {
                            ws.Cell(ri, 1).Value = r.ProjectName;
                            ws.Cell(ri, 2).Value = r.Wbs1;
                            ws.Cell(ri, 3).Value = r.Pm;
                            ws.Cell(ri, 4).Value = r.Phase;
                            ws.Cell(ri, 5).Value = r.DraftBudget; ws.Cell(ri, 5).Style.NumberFormat.Format = "0.0";
                            ws.Cell(ri, 6).Value = r.DraftHours; ws.Cell(ri, 6).Style.NumberFormat.Format = "0.0";
                            ws.Cell(ri, 7).Value = r.RemainingDraftHours; ws.Cell(ri, 7).Style.NumberFormat.Format = "0.0";
                            ws.Cell(ri, 8).Value = r.PercentDraftUsed; ws.Cell(ri, 8).Style.NumberFormat.Format = "0.0%";
                            ws.Cell(ri, 9).Value = r.DeliveryConfidence;
                            ri++;
                        }
                    }

                    var used = ws.RangeUsed();
                    if (used != null)
                        used.CreateTable().Theme = XLTableTheme.TableStyleLight9;

                    ws.Columns(1, headers.Length).AdjustToContents();
                    wb.SaveAs(path);
                }).ConfigureAwait(true);

                MessageBox.Show(this, "Export completed.", "Export to Excel", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Export failed:\n{ex.Message}", "Export to Excel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _vm.SetExporting(false);
            }
        }

        private void Window_Closing(object? sender, CancelEventArgs e) => _cts?.Cancel();

        private Kor.Operations.Financials.CfoMetrics.PortfolioHealthCounts BuildPortfolioCounts()
        {
            var watch = _vm.ProjectRows.Count - _vm.PortfolioHighConfidenceCount - _vm.PortfolioCriticalCount;
            return new Kor.Operations.Financials.CfoMetrics.PortfolioHealthCounts(
                Healthy: _vm.PortfolioHighConfidenceCount,
                Watch: watch,
                Critical: _vm.PortfolioCriticalCount);
        }

        private static bool IsDataGridRowDoubleClick(MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject d)
                return false;

            DependencyObject? cur = d;
            while (cur != null && cur is not DataGridRow)
                cur = VisualTreeHelper.GetParent(cur);

            return cur is DataGridRow;
        }
    }
}
