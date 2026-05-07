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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Kor.Operations.App.Options;
using Kor.Operations.App.PMTools;
using Kor.Operations.Financials;

namespace Kor.Operations.PMTools
{
    public partial class PmToolsWindow : Window
    {
        private readonly PmToolsViewModel _vm;
        private readonly WorkloadMeetingPanelViewModel _meetingPanel;
        private DeltekOdbcOptions? _odbcOptions;
        private CancellationTokenSource? _cts;
        private bool _isSyncingMeetingPriorities;

        public PmToolsWindow(FinancialsService financialsService, WorkloadMeetingPanelViewModel meetingPanel, DeltekOdbcOptions odbcOptions)
        {
            _vm = new PmToolsViewModel(financialsService);
            _meetingPanel = meetingPanel ?? throw new ArgumentNullException(nameof(meetingPanel));
            _odbcOptions = odbcOptions;
            _vm.EngRate = odbcOptions?.EngRate ?? 474;
            _vm.DraftRate = odbcOptions?.DraftRate ?? 655;
            _vm.TargetBilling = odbcOptions?.TargetBillingRate ?? 185;
            InitializeComponent();
            DataContext = _vm;

            var contextBuilder = Kor.Operations.Services.AppServices.Get<Kor.Operations.Services.AppAiContextBuilder>();
            contextBuilder.Register(_vm);
            AiPanel.Initialize(Kor.Operations.Services.AppServices.Get<Kor.Operations.Services.AppAiService>(), _vm);

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
            catch (Exception ex)
            {
                // Header failure breaks "My Projects" filter silently — log so users can be helped.
                _vm.CurrentUserName = "";
                Serilog.Log.Warning(ex, "PM Tools: header load failed; CurrentUserName unavailable — 'My Projects' filter will be empty.");
            }
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            await _vm.RefreshAsync(forceRefresh: true, _cts.Token);
            SyncMeetingPrioritiesToRows();
        }

        private async void DeleteMeetingBtn_Click(object sender, RoutedEventArgs e)
        {
            var meeting = _meetingPanel.SelectedMeeting;
            if (meeting == null) return;

            var label = meeting.MeetingDate.ToString("MMM d, yyyy");
            var result = MessageBox.Show(
                this,
                $"Delete the meeting from {label} and all its priority data?\n\nThis cannot be undone.",
                "PM Tools — Delete Meeting",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            await _meetingPanel.DeleteMeetingAsync();
            SyncMeetingPrioritiesToRows();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
        private void KpiDictionaryBtn_Click(object sender, RoutedEventArgs e)
            => new Financials.FinancialMetricDictionaryWindow { Owner = this }.Show();
        // Staff Utilization and Historical Analytics launchers were relocated to the
        // Financials window (sensitive-data access centralized there).

        private async void ScopeWatchlist_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.ShowWatchlistOnly) return;
            _vm.ShowWatchlistOnly = true;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            await _vm.RefreshAsync(forceRefresh: true, _cts.Token);
            SyncMeetingPrioritiesToRows();
        }

        private async void ScopeAllActive_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.ShowWatchlistOnly) return;
            _vm.ShowWatchlistOnly = false;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            await _vm.RefreshAsync(forceRefresh: true, _cts.Token);
            SyncMeetingPrioritiesToRows();
        }

        private void PhaseAll_Click(object sender, RoutedEventArgs e) => _vm.SelectedPhase = "All";
        private void PhaseSD_Click(object sender, RoutedEventArgs e) => _vm.SelectedPhase = "SD";
        private void PhaseDD_Click(object sender, RoutedEventArgs e) => _vm.SelectedPhase = "DD";
        private void PhaseCD_Click(object sender, RoutedEventArgs e) => _vm.SelectedPhase = "CD";
        private void PhaseCA_Click(object sender, RoutedEventArgs e) => _vm.SelectedPhase = "CA";
        private void SortByFee_Click(object sender, RoutedEventArgs e) => _vm.PmGroupSortMode = 0;
        private void SortByUnbilled_Click(object sender, RoutedEventArgs e) => _vm.PmGroupSortMode = 3;
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
                MessageBox.Show(this, "Export completed.", "PM Tools — Export To Excel", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Export failed:\n{ex.Message}", "PM Tools — Export To Excel", MessageBoxButton.OK, MessageBoxImage.Error);
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
                var path = sfd.FileName;
                var groups = _vm.PmGroups.ToList();
                await Task.Run(() => PmToolsExportService.ExportPmGroups(path, groups)).ConfigureAwait(true);
                MessageBox.Show(this, "Export completed.", "PM Tools — Export To Excel", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Export failed:\n{ex.Message}", "PM Tools — Export To Excel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _vm.SetExporting(false);
            }
        }

        private async void ExportMeetingBtn_Click(object sender, RoutedEventArgs e)
        {
            var meeting = _meetingPanel.SelectedMeeting;
            if (meeting == null)
                return;

            // Warn the user if any filter is active — the export only includes visible projects.
            var activeFilters = new System.Collections.Generic.List<string>();
            if (_vm.ShowWatchlistOnly)
                activeFilters.Add("Scope: Watchlist only (default)");
            if (!string.IsNullOrWhiteSpace(_vm.SelectedPhase) && !string.Equals(_vm.SelectedPhase, "All", StringComparison.OrdinalIgnoreCase))
                activeFilters.Add($"Phase: {_vm.SelectedPhase}");
            if (!string.IsNullOrWhiteSpace(_vm.SelectedConstructionType) && !string.Equals(_vm.SelectedConstructionType, "All", StringComparison.OrdinalIgnoreCase))
                activeFilters.Add($"Construction Type: {_vm.SelectedConstructionType}");
            if (_vm.ShowMyProjectsOnly)
                activeFilters.Add("My Projects Only");
            if (!string.IsNullOrWhiteSpace(_vm.ProjectSearchText))
                activeFilters.Add($"Search: \"{_vm.ProjectSearchText}\"");

            if (activeFilters.Count > 0)
            {
                var msg = "The export will only include projects matching your current filters:\n\n  "
                        + string.Join("\n  ", activeFilters)
                        + "\n\nContinue?";
                var result = MessageBox.Show(this, msg, "PM Tools — Export Filters Active",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                    return;
            }

            var sfd = new SaveFileDialog
            {
                Title = "Export Workload Meeting",
                Filter = "Excel Workbook|*.xlsx",
                FileName = $"WorkloadMeeting_{meeting.MeetingDate:yyyyMMdd}.xlsx",
                AddExtension = true,
                OverwritePrompt = true,
            };

            if (sfd.ShowDialog(this) != true)
                return;

            var dateLabel = meeting.MeetingDate.ToString("MMM d, yyyy");
            var priorityByWbs1 = new System.Collections.Generic.Dictionary<string, WorkloadMeetingProjectRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var pr in _meetingPanel.PriorityProjects.ToList())
                priorityByWbs1.TryAdd(pr.Wbs1, pr);

            var groups = _vm.PmGroups.ToList();
            var notes = _meetingPanel.MeetingNotes ?? string.Empty;
            var path = sfd.FileName;

            try
            {
                await Task.Run(() => PmToolsExportService.ExportMeeting(path, dateLabel, groups, priorityByWbs1, notes)).ConfigureAwait(true);
                MessageBox.Show(this, "Export completed.", "PM Tools — Export To Excel", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Export failed:\n{ex.Message}", "PM Tools — Export To Excel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Window_Closing(object? sender, CancelEventArgs e)
        {
            _cts?.Cancel();
            try
            {
                // Small yield so any in-flight priority ComboBox SelectionChanged can reach UpsertPriorityFromUiAsync
                // before we start the flush. Without this, a change made <600ms before close can be lost.
                await Task.Delay(100).ConfigureAwait(true);
                using var flushCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _meetingPanel.ForceSaveAllAsync(flushCts.Token);
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Failed to save meeting data on window close."); }
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

        private async void HotlistCheckbox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.DataContext is not PmProjectRow pmRow)
                return;

            var row = pmRow.Source;
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
                row.IsOnHotlist = previousState;
                MessageBox.Show(this,
                    $"Watchlist sync service is unavailable:\n{ex.Message}",
                    "PM Tools — Watchlist Sync Unavailable",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!syncClient.IsConfigured)
            {
                row.IsOnHotlist = previousState;
                MessageBox.Show(this,
                    "Watchlist sync is not configured in App.config (WatchlistSync.ServiceUrl / Username / Password).",
                    "PM Tools — Watchlist Sync Not Configured",
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
                        "PM Tools — Hotlist Sync Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                else
                {
                    row.HotlistSyncState = HotlistSyncState.Pending;
                    row.HotlistSyncError = "Still pending — the next Refresh will reflect the real Deltek state.";
                }
            }
            catch (OperationCanceledException)
            {
                row.HotlistSyncState = HotlistSyncState.Idle;
            }
            catch (Exception ex)
            {
                row.IsOnHotlist = previousState;
                row.HotlistSyncState = HotlistSyncState.Error;
                row.HotlistSyncError = ex.Message;
                MessageBox.Show(this,
                    $"Failed to update Hotlist on {row.Wbs1}:\n\n{ex.Message}",
                    "PM Tools — Hotlist Sync Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void PriorityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox cb) return;
            if (cb.DataContext is not PmProjectRow row) return;
            if (_isSyncingMeetingPriorities) return;
            if (!_meetingPanel.IsCurrentMeeting || _meetingPanel.SelectedMeeting == null) return;
            var priority = cb.SelectedIndex; // 0=unset,1=P1,...,5=P5
            if (priority < 0 || priority > 5)
            {
                Serilog.Log.Warning("PM Tools: invalid priority value {Priority} for {Wbs1}; ignoring.", priority, row.Wbs1);
                return;
            }
            await _meetingPanel.UpsertPriorityFromUiAsync(row.Wbs1, priority);
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
