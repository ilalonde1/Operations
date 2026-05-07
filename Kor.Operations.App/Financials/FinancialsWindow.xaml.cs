#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using ClosedXML.Excel;
using Kor.Operations.Core;
using Kor.Operations.Data;
using Kor.Operations.App.Options;
namespace Kor.Operations.Financials
{
    public partial class FinancialsWindow : Window
    {
        private readonly FinancialsViewModel _vm;
        private CancellationTokenSource? _cts;

        public FinancialsWindow(FinancialsViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            InitializeComponent();
            DataContext = _vm;

#if DEBUG
            DumpDeltekSchemaMenuItem.Visibility = Visibility.Visible;
#endif

            var contextBuilder = Kor.Operations.Services.AppServices.Get<Kor.Operations.Services.AppAiContextBuilder>();
            contextBuilder.Register(_vm);
            contextBuilder.Register(_vm.ExecutiveSummary);
            var aiService = Kor.Operations.Services.AppServices.Get<Kor.Operations.Services.AppAiService>();
            AiPanel.Initialize(aiService, _vm);
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _ = ApplyHeaderAsync();

            // Respect "prefer cached display"; only load on open if nothing is loaded yet.
            if (_vm.HasData)
                return;

            _cts = new CancellationTokenSource();
            await _vm.RefreshAsync(forceRefresh: false, _cts.Token);
        }

        private async Task ApplyHeaderAsync()
        {
            try { await global::Kor.Operations.HeaderLoader.ApplyAsync(HeaderBar); } catch (Exception ex) { Serilog.Log.Warning(ex, "Header load failed."); }
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            await _vm.RefreshAsync(forceRefresh: true, _cts.Token);
        }

        private async void RecalculateBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_vm._odbcOptions != null)
            {
                _vm._odbcOptions.EngRate = _vm.EngRate;
                _vm._odbcOptions.DraftRate = _vm.DraftRate;
                _vm._odbcOptions.TargetBillingRate = _vm.TargetBilling;
                _vm._odbcOptions.UseTargetRateBudget = _vm.IsTargetRateBudgetMode;
            }
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            await _vm.RefreshAsync(forceRefresh: true, _cts.Token);
        }

        private async void ScopeWatchlist_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.ShowWatchlistOnly) return;
            _vm.ShowWatchlistOnly = true;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            await _vm.RefreshAsync(forceRefresh: true, _cts.Token);
        }

        private async void ScopeAllActive_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.ShowWatchlistOnly) return;
            _vm.ShowWatchlistOnly = false;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            await _vm.RefreshAsync(forceRefresh: true, _cts.Token);
        }

        private void MetricDictionaryBtn_Click(object sender, RoutedEventArgs e)
        {
            var win = new FinancialMetricDictionaryWindow { Owner = this };
            win.Show();
        }

        private void PnLReportBtn_Click(object sender, RoutedEventArgs e)
        {
            _vm.SectionIndex = 2;
        }

        private async void DumpDeltekSchemaBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Run off UI thread; this can take a while and may require VPN/Deltek connectivity.
                var result = await DeltekSchemaDumper.DumpAsync(CancellationToken.None).ConfigureAwait(true);

                MessageBox.Show(
                    this,
                    $"Deltek schema dump completed.\n\nFolder:\n{result.OutputDirectory}\n\nTables: {result.TableCount:N0}\nColumns: {result.ColumnCount:N0}",
                    "Deltek Schema Dump",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"Deltek schema dump failed.\n\n{ex.Message}",
                    "Deltek Schema Dump",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

        private void ShowCommandCenter_Click(object sender, RoutedEventArgs e)
        {
            _vm.SectionIndex = 0;
        }

        private void ShowExecutiveSummary_Click(object sender, RoutedEventArgs e)
        {
            _vm.SectionIndex = 1;
            _vm.ExecutiveSummary.SetScope(_vm.IsWatchlistOnly ? "Watchlist" : "All Active", _vm.Rows.Count);
            _ = _vm.ExecutiveSummary.RefreshAsync(
                forceRefresh: false,
                existingSnapshot: null,
                existingTrend: null,
                existingUtilRows: null,
                ct: CancellationToken.None);
        }

        private void UtilizationGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (UtilizationGrid.SelectedItem is not UtilizationRow row)
                return;

            if (e.OriginalSource is DependencyObject d)
            {
                DependencyObject? cur = d;
                while (cur != null && cur is not DataGridRow)
                    cur = VisualTreeHelper.GetParent(cur);
                if (cur is not DataGridRow)
                    return;
            }

            var counts = new Kor.Operations.Financials.CfoMetrics.PortfolioHealthCounts(
                Healthy: _vm.PortfolioHighConfidenceCount,
                Watch: _vm.PortfolioWatchCount + _vm.PortfolioAtRiskCount,
                Critical: _vm.PortfolioCriticalCount);

            var win = new ProjectFinancialDetailWindow(row.Project, counts) { Owner = this };
            win.Show();
        }

        private void DraftUtilizationGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DraftUtilizationGrid.SelectedItem is not DraftUtilizationRow row)
                return;

            if (e.OriginalSource is DependencyObject d)
            {
                DependencyObject? cur = d;
                while (cur != null && cur is not DataGridRow)
                    cur = VisualTreeHelper.GetParent(cur);
                if (cur is not DataGridRow)
                    return;
            }

            var counts = new Kor.Operations.Financials.CfoMetrics.PortfolioHealthCounts(
                Healthy: _vm.PortfolioHighConfidenceCount,
                Watch: _vm.PortfolioWatchCount + _vm.PortfolioAtRiskCount,
                Critical: _vm.PortfolioCriticalCount);

            var win = new ProjectFinancialDetailWindow(row.Project, counts) { Owner = this };
            win.Show();
        }

        private void ShowBillingManagerReport_Click(object sender, RoutedEventArgs e)
        {
            _vm.SectionIndex = 3;
        }

        private void ShowClients_Click(object sender, RoutedEventArgs e)
        {
            _vm.SectionIndex = 4;
        }

        private void ShowForecast_Click(object sender, RoutedEventArgs e)
        {
            _vm.SectionIndex = 5;
        }

        // Sensitive-data launchers relocated from PM Tools. Each opens a standalone window
        // with Financials as Owner so focus returns cleanly on close.
        private void StaffUtilizationBtn_Click(object sender, RoutedEventArgs e)
            => new Kor.Operations.PMTools.StaffUtilizationWindow(_vm._odbcOptions!, _vm._financialsOptions!) { Owner = this }.Show();

        private void HistoricalAnalyticsBtn_Click(object sender, RoutedEventArgs e)
            => new Kor.Operations.PMTools.HistoricalAnalyticsWindow(_vm._odbcOptions!) { Owner = this }.Show();

        private void ShowEngineeringCapacityRisk_Click(object sender, RoutedEventArgs e)
        {
            _vm.CapacityRiskViewIndex = 0;
        }

        private void ShowDraftingCapacityRisk_Click(object sender, RoutedEventArgs e)
        {
            _vm.CapacityRiskViewIndex = 1;
        }

        private async void ExportUtilizationBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.CanExportUtilization)
                return;

            var sfd = new SaveFileDialog
            {
                Title = "Export Utilization",
                Filter = "Excel Workbook|*.xlsx",
                FileName = $"UtilizationReport_{DateTime.Now:yyyyMMdd}.xlsx",
                AddExtension = true,
                OverwritePrompt = true
            };

            if (sfd.ShowDialog(this) != true)
                return;

            var exportEngineering = _vm.IsEngineeringCapacitySelected;

            _vm.SetExporting(true);
            try
            {
                var path = sfd.FileName;
                await Task.Run(() =>
                {
                    using var wb = new XLWorkbook();
                    var ws = wb.Worksheets.Add("Utilization");

                    var headers = exportEngineering
                        ? new[]
                        {
                            "Project",
                            DisplayTerms.ProjectNumber,
                            "PM",
                            "Phase",
                            "Eng Budget",
                            "Eng Hours",
                            "Remaining Eng Hours",
                            "% Eng Used",
                            "Fee",
                            "% Billed",
                            "Risk",
                            "SeverityScore"
                        }
                        : new[]
                        {
                            "Project",
                            DisplayTerms.ProjectNumber,
                            "PM",
                            "Phase",
                            "Draft Budget",
                            "Draft Hours",
                            "Remaining Draft Hours",
                            "% Draft Used",
                            "Fee",
                            "% Billed",
                            "Risk",
                            "SeverityScore"
                        };

                    for (var c = 0; c < headers.Length; c++)
                    {
                        var cell = ws.Cell(1, c + 1);
                        cell.Value = headers[c];
                        cell.Style.Font.Bold = true;
                    }

                    ws.SheetView.FreezeRows(1);

                    var xlRed = XLColor.FromHtml("#FEE2E2");
                    var xlYellow = XLColor.FromHtml("#FEF3C7");
                    var xlGreen = XLColor.FromHtml("#DCFCE7");

                    if (exportEngineering)
                    {
                        var rows = _vm.UtilizationView.Cast<UtilizationRow>().ToList();
                        var rowIndex = 2;
                        foreach (var r in rows)
                        {
                            ws.Cell(rowIndex, 1).Value = r.ProjectName;
                            ws.Cell(rowIndex, 2).Value = r.Wbs1;
                            ws.Cell(rowIndex, 3).Value = r.Pm;
                            ws.Cell(rowIndex, 4).Value = r.Phase;

                            ws.Cell(rowIndex, 5).Value = r.EngBudget;
                            ws.Cell(rowIndex, 5).Style.NumberFormat.Format = "0.0";

                            ws.Cell(rowIndex, 6).Value = r.EngHours;
                            ws.Cell(rowIndex, 6).Style.NumberFormat.Format = "0.0";

                            ws.Cell(rowIndex, 7).Value = r.RemainingEngHours;
                            ws.Cell(rowIndex, 7).Style.NumberFormat.Format = "0.0";

                            ws.Cell(rowIndex, 8).Value = r.PercentEngUsed;
                            ws.Cell(rowIndex, 8).Style.NumberFormat.Format = "0.0%";

                            ws.Cell(rowIndex, 9).Value = r.Fee;
                            ws.Cell(rowIndex, 9).Style.NumberFormat.Format = "$#,##0";

                            ws.Cell(rowIndex, 10).Value = r.PercentBilled;
                            ws.Cell(rowIndex, 10).Style.NumberFormat.Format = "0.0%";

                            ws.Cell(rowIndex, 11).Value = r.RiskStatus;
                            ws.Cell(rowIndex, 12).Value = (int)r.ConfidenceLevel;

                            rowIndex++;
                        }
                    }
                    else
                    {
                        var rows = _vm.DraftUtilizationView.Cast<DraftUtilizationRow>().ToList();
                        var rowIndex = 2;
                        foreach (var r in rows)
                        {
                            ws.Cell(rowIndex, 1).Value = r.ProjectName;
                            ws.Cell(rowIndex, 2).Value = r.Wbs1;
                            ws.Cell(rowIndex, 3).Value = r.Pm;
                            ws.Cell(rowIndex, 4).Value = r.Phase;

                            ws.Cell(rowIndex, 5).Value = r.DraftBudget;
                            ws.Cell(rowIndex, 5).Style.NumberFormat.Format = "0.0";

                            ws.Cell(rowIndex, 6).Value = r.DraftHours;
                            ws.Cell(rowIndex, 6).Style.NumberFormat.Format = "0.0";

                            ws.Cell(rowIndex, 7).Value = r.RemainingDraftHours;
                            ws.Cell(rowIndex, 7).Style.NumberFormat.Format = "0.0";

                            ws.Cell(rowIndex, 8).Value = r.PercentDraftUsed;
                            ws.Cell(rowIndex, 8).Style.NumberFormat.Format = "0.0%";

                            ws.Cell(rowIndex, 9).Value = r.Fee;
                            ws.Cell(rowIndex, 9).Style.NumberFormat.Format = "$#,##0";

                            ws.Cell(rowIndex, 10).Value = r.PercentBilled;
                            ws.Cell(rowIndex, 10).Style.NumberFormat.Format = "0.0%";

                            ws.Cell(rowIndex, 11).Value = r.RiskStatus;
                            ws.Cell(rowIndex, 12).Value = (int)r.ConfidenceLevel;

                            rowIndex++;
                        }
                    }

                    var used = ws.RangeUsed();
                    if (used != null)
                    {
                        used.CreateTable().Theme = XLTableTheme.TableStyleLight9;
                    }

                    var rowCount = ws.LastRowUsed()?.RowNumber() ?? 1;
                    if (rowCount > 1)
                    {
                        var severityCol = headers.Length; // SeverityScore
                        var dataColsEnd = headers.Length - 1; // exclude SeverityScore from visible range
                        var severityLetter = XLHelper.GetColumnLetterFromNumber(severityCol);
                        var visibleRange = ws.Range(2, 1, rowCount, dataColsEnd);

                        visibleRange.AddConditionalFormat()
                            .WhenIsTrue($"=${severityLetter}2=0")
                            .Fill.SetBackgroundColor(xlRed);

                        visibleRange.AddConditionalFormat()
                            .WhenIsTrue($"=${severityLetter}2=1")
                            .Fill.SetBackgroundColor(xlYellow);

                        visibleRange.AddConditionalFormat()
                            .WhenIsTrue($"=${severityLetter}2=3")
                            .Fill.SetBackgroundColor(xlGreen);

                        ws.Column(severityCol).Hide();
                    }

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

        private static string Csv(string? s)
        {
            s ??= "";
            if (s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private void FinancialsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FinancialsGrid.SelectedItem is not FinancialsProjectRow row)
                return;

            if (e.OriginalSource is DependencyObject d)
            {
                DependencyObject? cur = d;
                while (cur != null && cur is not DataGridRow)
                    cur = VisualTreeHelper.GetParent(cur);
                if (cur is not DataGridRow)
                    return;
            }

            var counts = new Kor.Operations.Financials.CfoMetrics.PortfolioHealthCounts(
                Healthy: _vm.PortfolioHighConfidenceCount,
                Watch: _vm.PortfolioWatchCount + _vm.PortfolioAtRiskCount,
                Critical: _vm.PortfolioCriticalCount);

            var win = new ProjectFinancialDetailWindow(row, counts) { Owner = this };
            win.Show();
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            _cts?.Cancel();
        }

        // Hotlist checkbox click handler — the TwoWay binding has already flipped IsOnHotlist
        // by the time this fires, so we capture the new desired state, fire the sync in the
        // background, and revert on failure. Uses the shared WatchlistSyncClient singleton.
        private async void HotlistCheckbox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.DataContext is not FinancialsProjectRow row)
                return;

            var desiredOn = row.IsOnHotlist;
            var previousState = !desiredOn;
            var requestedBy = Environment.UserName;

            WatchlistSyncClient syncClient;
            try
            {
                syncClient = Kor.Operations.Services.AppServices.Get<WatchlistSyncClient>();
            }
            catch (Exception ex)
            {
                // DI resolution failed — revert the click, notify.
                row.IsOnHotlist = previousState;
                MessageBox.Show(this,
                    $"Watchlist sync service is unavailable:\n{ex.Message}",
                    "Hotlist",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!syncClient.IsConfigured)
            {
                row.IsOnHotlist = previousState;
                MessageBox.Show(this,
                    "Watchlist sync is not configured in App.config (WatchlistSync.ServiceUrl / Username / Password).",
                    "Hotlist",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            row.HotlistSyncState = HotlistSyncState.Pending;
            row.HotlistSyncError = null;

            try
            {
                var result = await syncClient.EnqueueAndWaitAsync(
                    wbs1: row.Wbs1,
                    desiredOn: desiredOn,
                    requestedBy: requestedBy,
                    timeout: TimeSpan.FromSeconds(30),
                    pollInterval: TimeSpan.FromSeconds(2),
                    ct: _cts?.Token ?? CancellationToken.None);

                if (result.Status == "Applied")
                {
                    row.HotlistSyncState = HotlistSyncState.Idle;
                    row.HotlistSyncError = null;
                }
                else if (result.Status == "Error")
                {
                    row.IsOnHotlist = previousState;
                    row.HotlistSyncState = HotlistSyncState.Error;
                    row.HotlistSyncError = result.ErrorMessage ?? "Deltek rejected the change.";
                    MessageBox.Show(this,
                        $"Failed to update Hotlist on {row.Wbs1}:\n\n{row.HotlistSyncError}",
                        "Hotlist sync failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                else
                {
                    // Still Pending after timeout — leave optimistic state, show subtle warning.
                    row.HotlistSyncState = HotlistSyncState.Pending;
                    row.HotlistSyncError = "Still pending — the next Refresh will reflect the real Deltek state.";
                }
            }
            catch (OperationCanceledException) when (_cts?.Token.IsCancellationRequested == true)
            {
                // User cancelled (window closed or refresh triggered) — leave whatever state is there.
                row.HotlistSyncState = HotlistSyncState.Idle;
            }
            catch (Exception ex)
            {
                row.IsOnHotlist = previousState;
                row.HotlistSyncState = HotlistSyncState.Error;
                row.HotlistSyncError = ex.Message;
                MessageBox.Show(this,
                    $"Failed to update Hotlist on {row.Wbs1}:\n\n{ex.Message}",
                    "Hotlist sync failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
