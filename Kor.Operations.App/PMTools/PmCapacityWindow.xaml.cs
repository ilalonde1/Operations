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
    internal partial class PmCapacityWindow : Window
    {
        private readonly PmToolsViewModel _vm;
        private readonly WorkloadMeetingPanelViewModel _meetingPanel;
        private DeltekOdbcOptions? _odbcOptions;
        private CancellationTokenSource? _cts;
        private bool _isSyncingMeetingPriorities;
        // Round 39a (T1.001): stored handlers so Window_Closing can unsubscribe
        // from the singleton meeting VM. Without this, every close/reopen of
        // the capacity window left a closure capturing `this` reachable from
        // the singleton — closed window, visual tree, and VM references could
        // not be GC'd, and a single priority change ran the sync method
        // against every leaked window in turn.
        private PropertyChangedEventHandler? _meetingPanelPropertyChangedHandler;
        private System.Collections.Specialized.NotifyCollectionChangedEventHandler? _meetingPanelCurrentProjectsHandler;

        // Round 38a: both VMs now arrive from DI as singletons so the upcoming
        // PM Tools window split can share them across two windows. EngRate /
        // DraftRate / TargetBilling are applied in AppModule's VM factory, no
        // longer here. Ctor is `internal` because PmToolsViewModel is internal
        // (its members reference internal types like BulkObservableCollection
        // and PmProjectRow). DI's ActivatorUtilities calls internal ctors in
        // the same assembly without trouble.
        internal PmCapacityWindow(PmToolsViewModel vm, WorkloadMeetingPanelViewModel meetingPanel, DeltekOdbcOptions odbcOptions)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            _meetingPanel = meetingPanel ?? throw new ArgumentNullException(nameof(meetingPanel));
            _odbcOptions = odbcOptions;
            InitializeComponent();
            DataContext = _vm;

            var contextBuilder = Kor.Operations.Services.AppServices.Get<Kor.Operations.Services.AppAiContextBuilder>();
            contextBuilder.Register(_vm);
            AiPanel.Initialize(Kor.Operations.Services.AppServices.Get<Kor.Operations.Services.AppAiService>(), _vm);

            _meetingPanelPropertyChangedHandler = (_, e) =>
            {
                if (e.PropertyName is nameof(WorkloadMeetingPanelViewModel.CurrentProjects)
                                    or nameof(WorkloadMeetingPanelViewModel.SelectedMeeting))
                    SyncMeetingPrioritiesToRows();
            };
            _meetingPanelCurrentProjectsHandler = (_, _) => SyncMeetingPrioritiesToRows();
            _meetingPanel.PropertyChanged += _meetingPanelPropertyChangedHandler;
            _meetingPanel.CurrentProjects.CollectionChanged += _meetingPanelCurrentProjectsHandler;
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

        private async void Window_Closing(object? sender, CancelEventArgs e)
        {
            // Round 39a (T1.001): drop our two subscriptions to the singleton
            // meeting VM before anything else; the lambdas capture `this`, so
            // leaving them attached pins the closed window in memory and runs
            // the sync method against a dead visual tree on every priority
            // change. Idempotent — null-coalescing handles a torn-down ctor.
            if (_meetingPanelPropertyChangedHandler is not null)
            {
                _meetingPanel.PropertyChanged -= _meetingPanelPropertyChangedHandler;
                _meetingPanelPropertyChangedHandler = null;
            }
            if (_meetingPanelCurrentProjectsHandler is not null)
            {
                _meetingPanel.CurrentProjects.CollectionChanged -= _meetingPanelCurrentProjectsHandler;
                _meetingPanelCurrentProjectsHandler = null;
            }

            Kor.Operations.Services.AppServices.Get<Kor.Operations.Services.AppAiContextBuilder>().Unregister(_vm);
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            try
            {
                // Small yield so any in-flight priority ComboBox SelectionChanged can reach UpsertPriorityFromUiAsync
                // before we start the flush. Without this, a change made <600ms before close can be lost.
                await Task.Delay(100).ConfigureAwait(true);
                using var flushCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _meetingPanel.ForceSaveAllAsync(flushCts.Token);
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Failed to save meeting data on window close."); }
            // Round 38a: meetingPanel is a Singleton in DI now, so the DI
            // container disposes it on app shutdown. Disposing here would kill
            // the VM for any other window (chooser, future Workload Meeting /
            // PM Capacity windows) that still wants to use it.
        }

        private void SyncMeetingPrioritiesToRows()
        {
            _isSyncingMeetingPriorities = true;
            try
            {
                // Step 1: badge the PM Capacity grid rows with their current priority.
                var lookup = _meetingPanel.CurrentProjects
                    .ToDictionary(p => p.Wbs1, p => p.Priority, StringComparer.OrdinalIgnoreCase);
                foreach (var row in _vm.ProjectRows)
                    row.MeetingPriority = lookup.TryGetValue(row.Wbs1, out var p) ? p : 0;

                // Step 2: enrich the meeting VM's PriorityProjects with ProjectName
                // and PM from our PmToolsViewModel rows. Round 39b (T2.001): the VM
                // already does a basic projection on its own so the meeting window
                // shows something when opened alone; this just overwrites with the
                // richer ProjectName / PmName while we're here.
                var projectLookup = _vm.ProjectRows
                    .ToDictionary(r => r.Wbs1, r => r, StringComparer.OrdinalIgnoreCase);
                _meetingPanel.RefreshPriorityProjects(wbs1 =>
                    projectLookup.TryGetValue(wbs1, out var proj)
                        ? ((string?)proj.Name, (string?)proj.Pm)
                        : (null, null));
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

            // Round 39b (T2.002): the ComboBox two-way binding has already updated
            // row.MeetingPriority before this handler ran. If the store rejects the
            // save, roll the row back so the grid doesn't keep showing a priority
            // the database refused. Snapshot the previous value from the meeting
            // VM's CurrentProjects (the canonical source) before awaiting.
            var previousPriority = _meetingPanel.CurrentProjects
                .FirstOrDefault(p => string.Equals(p.Wbs1, row.Wbs1, StringComparison.OrdinalIgnoreCase))?.Priority ?? 0;

            var ok = await _meetingPanel.UpsertPriorityFromUiAsync(row.Wbs1, priority);
            if (!ok)
            {
                _isSyncingMeetingPriorities = true;
                try { row.MeetingPriority = previousPriority; }
                finally { _isSyncingMeetingPriorities = false; }
                MessageBox.Show(this,
                    $"Failed to save priority for {row.Wbs1}.\n\n{_meetingPanel.MeetingError ?? "See logs for details."}",
                    "PM Capacity & Risk — Priority Save Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
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
