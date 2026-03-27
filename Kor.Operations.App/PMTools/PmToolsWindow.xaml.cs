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
using Kor.Operations.App.PMTools;
using Kor.Operations.Financials;

namespace Kor.Operations.PMTools
{
    public partial class PmToolsWindow : Window
    {
        private readonly PmToolsViewModel _vm = new();
        private readonly WorkloadMeetingPanelViewModel _meetingPanel;
        private CancellationTokenSource? _cts;
        private bool _isSyncingMeetingPriorities;

        public PmToolsWindow(WorkloadMeetingPanelViewModel meetingPanel)
        {
            _meetingPanel = meetingPanel ?? throw new ArgumentNullException(nameof(meetingPanel));
            InitializeComponent();
            DataContext = _vm;
            _meetingPanel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(WorkloadMeetingPanelViewModel.CurrentProjects)
                                    or nameof(WorkloadMeetingPanelViewModel.SelectedMeeting))
                    SyncMeetingPrioritiesToRows();
            };
            _meetingPanel.CurrentProjects.CollectionChanged += (_, _) => SyncMeetingPrioritiesToRows();
        }

        public WorkloadMeetingPanelViewModel MeetingPanel => _meetingPanel;

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _ = ApplyHeaderAsync();
            if (!_vm.HasData || _vm.IsDataStale)
            {
                _cts = new CancellationTokenSource();
                await _vm.RefreshAsync(forceRefresh: false, _cts.Token);
            }

            await _meetingPanel.LoadAsync();
            SyncMeetingPrioritiesToRows();
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
            SyncMeetingPrioritiesToRows();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
        private void KpiDictionaryBtn_Click(object sender, RoutedEventArgs e)
            => new Financials.FinancialMetricDictionaryWindow { Owner = this }.Show();
        private void StaffUtilizationBtn_Click(object sender, RoutedEventArgs e)
            => new StaffUtilizationWindow { Owner = this }.Show();
        private void PhaseAll_Click(object sender, RoutedEventArgs e) => _vm.SelectedPhase = "All";
        private void PhaseSD_Click(object sender, RoutedEventArgs e) => _vm.SelectedPhase = "SD";
        private void PhaseDD_Click(object sender, RoutedEventArgs e) => _vm.SelectedPhase = "DD";
        private void PhaseCD_Click(object sender, RoutedEventArgs e) => _vm.SelectedPhase = "CD";
        private void PhaseCA_Click(object sender, RoutedEventArgs e) => _vm.SelectedPhase = "CA";
        private void SortByFee_Click(object sender, RoutedEventArgs e) => _vm.PmGroupSortMode = 0;
        private void SortByName_Click(object sender, RoutedEventArgs e) => _vm.PmGroupSortMode = 1;
        private void SortByAtRisk_Click(object sender, RoutedEventArgs e) => _vm.PmGroupSortMode = 2;
        private void ShowEngineeringCapacityRisk_Click(object sender, RoutedEventArgs e) => _vm.CapacityRiskViewIndex = 0;
        private void ShowDraftingCapacityRisk_Click(object sender, RoutedEventArgs e) => _vm.CapacityRiskViewIndex = 1;

        private void PmGroupExpand_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: PmGroupViewModel group })
                group.IsExpanded = !group.IsExpanded;
        }

        private void ProjectGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGrid { SelectedItem: PmProjectRow row } || !IsDataGridRowDoubleClick(e))
                return;

            // Priority column (index 0) is interactive  don't treat its double-click as a row open
            if (GetClickedCell(e)?.Column.DisplayIndex == 0)
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

        private async void ExportPmGroupsBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.CanExportPmGroups) return;

            var sfd = new SaveFileDialog
            {
                Title = "Export PM Groups",
                Filter = "Excel Workbook|*.xlsx",
                FileName = $"PmGroups_{DateTime.Now:yyyyMMdd}.xlsx",
                AddExtension = true,
                OverwritePrompt = true,
            };
            if (sfd.ShowDialog(this) != true) return;

            _vm.SetExporting(true);
            try
            {
                var path   = sfd.FileName;
                var groups = _vm.PmGroups.ToList(); // snapshot on UI thread (ObservableCollection is not thread-safe)
                await Task.Run(() =>
                {
                    using var wb = new XLWorkbook();
                    var ws = wb.Worksheets.Add("PM Groups");

                    string[] headers = { "PM", "Project #", "Project Name", "Phase", "Drafting Mgr",
                                          "Fee", "Eng Budget", "Eng Hrs", "Eng %", "Eng Remaining",
                                          "Draft Budget", "Draft Hrs", "Draft %", "Draft Remaining", "Delivery Risk" };
                    for (var c = 0; c < headers.Length; c++)
                    {
                        var cell = ws.Cell(1, c + 1);
                        cell.Value = headers[c];
                        cell.Style.Font.Bold = true;
                    }
                    ws.SheetView.FreezeRows(1);

                    var ri = 2;
                    foreach (var group in groups)
                    {
                        foreach (var p in group.Projects)
                        {
                            ws.Cell(ri, 1).Value = group.PmName;
                            ws.Cell(ri, 2).Value = p.Wbs1;
                            ws.Cell(ri, 3).Value = p.Name;
                            ws.Cell(ri, 4).Value = p.Phase;
                            ws.Cell(ri, 5).Value = p.DraftingManager;
                            ws.Cell(ri, 6).Value = p.Fee;          ws.Cell(ri, 6).Style.NumberFormat.Format = "#,##0";
                            ws.Cell(ri, 7).Value = p.EngBudget;    ws.Cell(ri, 7).Style.NumberFormat.Format = "0.0";
                            ws.Cell(ri, 8).Value = p.EngHrs;       ws.Cell(ri, 8).Style.NumberFormat.Format = "0.0";
                            ws.Cell(ri, 9).Value = p.EngPercent;   ws.Cell(ri, 9).Style.NumberFormat.Format = "0.0%";
                            ws.Cell(ri, 10).Value = p.RemainingEngHours; ws.Cell(ri, 10).Style.NumberFormat.Format = "0.0";
                            ws.Cell(ri, 11).Value = p.DraftBudget; ws.Cell(ri, 11).Style.NumberFormat.Format = "0.0";
                            ws.Cell(ri, 12).Value = p.DraftHrs;    ws.Cell(ri, 12).Style.NumberFormat.Format = "0.0";
                            ws.Cell(ri, 13).Value = p.DraftPercent; ws.Cell(ri, 13).Style.NumberFormat.Format = "0.0%";
                            ws.Cell(ri, 14).Value = p.RemainingDraftHours; ws.Cell(ri, 14).Style.NumberFormat.Format = "0.0";
                            ws.Cell(ri, 15).Value = p.DeliveryRisk;
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

        private async void Window_Closing(object? sender, CancelEventArgs e)
        {
            _cts?.Cancel();
            try
            {
                using var flushCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _meetingPanel.ForceSaveAllAsync(flushCts.Token);
            }
            catch { }
            _meetingPanel.Dispose();
        }

        private void SyncMeetingPrioritiesToRows()
        {
            _isSyncingMeetingPriorities = true;
            try
            {
                var lookup = _meetingPanel.CurrentProjects
                    .ToDictionary(p => p.Wbs1, p => p.Priority, StringComparer.OrdinalIgnoreCase);
                foreach (var row in _vm.ProjectRows)
                    row.MeetingPriority = lookup.TryGetValue(row.Wbs1, out var p) ? p : 0;

                var projectLookup = _vm.ProjectRows
                    .ToDictionary(r => r.Wbs1, r => r, StringComparer.OrdinalIgnoreCase);

                var enrichedRows = _meetingPanel.CurrentProjects
                    .Select(p =>
                    {
                        projectLookup.TryGetValue(p.Wbs1, out var proj);
                        return new Kor.Operations.App.PMTools.WorkloadMeetingProjectRow
                        {
                            MeetingId = p.MeetingId,
                            Wbs1 = p.Wbs1,
                            Priority = p.Priority,
                            Notes = p.Notes ?? string.Empty,
                            ProjectName = proj?.Name ?? p.Wbs1,
                            PmName = proj?.Pm ?? string.Empty,
                        };
                    })
                    .OrderBy(r => r.Priority)
                    .ThenBy(r => r.ProjectName);

                _meetingPanel.SetPriorityProjectRows(enrichedRows);
            }
            finally
            {
                _isSyncingMeetingPriorities = false;
            }
        }

        private async void PriorityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox cb) return;
            if (cb.DataContext is not PmProjectRow row) return;
            if (_isSyncingMeetingPriorities) return;
            if (!_meetingPanel.IsCurrentMeeting || _meetingPanel.SelectedMeeting == null) return;
            var priority = cb.SelectedIndex; // 0=unset,1=P1,...,5=P5
            await _meetingPanel.UpsertPriorityFromUiAsync(row.Wbs1, priority);
        }

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

        private static DataGridCell? GetClickedCell(MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject d) return null;
            DependencyObject? cur = d;
            while (cur != null && cur is not DataGridCell)
                cur = VisualTreeHelper.GetParent(cur);
            return cur as DataGridCell;
        }
    }
}
