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
using Microsoft.Win32;
using Kor.Operations.App.Options;
using Kor.Operations.App.PMTools;
using Kor.Operations.Financials;

namespace Kor.Operations.PMTools
{
    /// <summary>
    /// Round 48 — PM Capacity & Risk window. Per-discipline engineering /
    /// drafting capacity-risk view only. The workload meeting board and the
    /// PM-by-PM project listings live in <see cref="WorkloadMeetingWindow"/>;
    /// this window deliberately has neither — no <c>WorkloadMeetingPanelViewModel</c>
    /// dependency, no priority sync, no hotlist toggle. Just the two risk
    /// DataGrids (Engineering tab + Drafting tab) backed by the shared
    /// singleton <see cref="PmToolsViewModel"/>.
    /// </summary>
    internal partial class PmCapacityWindow : Window
    {
        private readonly PmToolsViewModel _vm;
        private DeltekOdbcOptions? _odbcOptions;
        private CancellationTokenSource? _cts;

        internal PmCapacityWindow(PmToolsViewModel vm, DeltekOdbcOptions odbcOptions)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            _odbcOptions = odbcOptions;
            InitializeComponent();
            DataContext = _vm;

            var contextBuilder = Kor.Operations.Services.AppServices.Get<Kor.Operations.Services.AppAiContextBuilder>();
            contextBuilder.Register(_vm);
            AiPanel.Initialize(Kor.Operations.Services.AppServices.Get<Kor.Operations.Services.AppAiService>(), _vm);
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _ = ApplyHeaderAsync();
            if (!_vm.HasData || _vm.IsDataStale)
            {
                _cts = new CancellationTokenSource();
                await _vm.RefreshAsync(forceRefresh: false, _cts.Token);
            }
        }

        private async Task ApplyHeaderAsync()
        {
            try
            {
                await global::Kor.Operations.HeaderLoader.ApplyAsync(HeaderBar);
                _vm.CurrentUserName = HeaderBar.UserDisplayName ?? "";
            }
            catch (Exception ex)
            {
                _vm.CurrentUserName = "";
                Serilog.Log.Warning(ex, "PM Capacity & Risk: header load failed; CurrentUserName unavailable.");
            }
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            await _vm.RefreshAsync(forceRefresh: true, _cts.Token);
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

        private void KpiDictionaryBtn_Click(object sender, RoutedEventArgs e)
            => new Financials.FinancialMetricDictionaryWindow { Owner = this }.Show();

        private void ShowEngineeringCapacityRisk_Click(object sender, RoutedEventArgs e)
            => _vm.CapacityRiskViewIndex = 0;

        private void ShowDraftingCapacityRisk_Click(object sender, RoutedEventArgs e)
            => _vm.CapacityRiskViewIndex = 1;

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
            if (!_vm.CanExportUtilization) return;

            var exportEng = _vm.IsEngineeringCapacitySelected;
            var label = exportEng ? "Engineering" : "Drafting";

            var sfd = new SaveFileDialog
            {
                Title = "Export Utilization",
                Filter = "Excel Workbook|*.xlsx",
                FileName = $"PmUtilization_{DateTime.Now:yyyyMMdd}.xlsx",
                AddExtension = true,
                OverwritePrompt = true,
            };
            if (sfd.ShowDialog(this) != true) return;

            _vm.SetExporting(true);
            try
            {
                var path = sfd.FileName;
                var engRows = exportEng ? _vm.UtilizationView.Cast<UtilizationRow>().ToList() : null;
                var draftingRows = !exportEng ? _vm.DraftUtilizationView.Cast<DraftUtilizationRow>().ToList() : null;
                await Task.Run(() => PmToolsExportService.ExportUtilization(path, label, exportEng, engRows, draftingRows)).ConfigureAwait(true);
                MessageBox.Show(this, "Export completed.", "PM Capacity & Risk — Export to Excel", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Export failed:\n{ex.Message}", "PM Capacity & Risk — Export to Excel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _vm.SetExporting(false);
            }
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            Kor.Operations.Services.AppServices.Get<Kor.Operations.Services.AppAiContextBuilder>().Unregister(_vm);
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        private Kor.Operations.Financials.CfoMetrics.PortfolioHealthCounts BuildPortfolioCounts()
        {
            // Match FinancialsWindow's definition explicitly: Watch = Watch + AtRisk.
            return new Kor.Operations.Financials.CfoMetrics.PortfolioHealthCounts(
                Healthy: _vm.PortfolioHighConfidenceCount,
                Watch: _vm.PortfolioWatchCount + _vm.PortfolioAtRiskCount,
                Critical: _vm.PortfolioCriticalCount);
        }

        private static bool IsDataGridRowDoubleClick(MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject d) return false;
            DependencyObject? cur = d;
            while (cur != null && cur is not DataGridRow)
                cur = VisualTreeHelper.GetParent(cur);
            return cur is DataGridRow;
        }
    }
}
