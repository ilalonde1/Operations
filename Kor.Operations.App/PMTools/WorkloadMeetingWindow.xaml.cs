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
    internal partial class WorkloadMeetingWindow : Window
    {
        private readonly PmToolsViewModel _vm;
        private readonly WorkloadMeetingPanelViewModel _meetingPanel;
        private DeltekOdbcOptions? _odbcOptions;
        private CancellationTokenSource? _cts;
        private bool _isSyncingMeetingPriorities;

        // Round 52d/52e: both VMs are singletons and this window is transient —
        // every handler subscribed to their events must be stored and
        // unsubscribed in Window_Closing or each open/close cycle leaks the
        // window (audit T1.001 class). Worse than memory: a leaked window's
        // sync handler keeps mutating the shared PmProjectRows under ITS OWN
        // _isSyncingMeetingPriorities guard, so a live window's priority
        // ComboBoxes see those mutations as user edits and fire phantom
        // UpsertPriorityFromUiAsync calls.
        private PropertyChangedEventHandler? _meetingPanelPropertyChangedHandler;
        private System.Collections.Specialized.NotifyCollectionChangedEventHandler? _currentProjectsChangedHandler;
        private System.Collections.Specialized.NotifyCollectionChangedEventHandler? _pmGroupsChangedHandler;
        private Action<string, string>? _projectNotesUpdatedHandler;

        // Round 38a: both VMs now arrive from DI as singletons so the upcoming
        // PM Tools window split can share them across two windows. EngRate /
        // DraftRate / TargetBilling are applied in AppModule's VM factory, no
        // longer here. Ctor is `internal` because PmToolsViewModel is internal
        // (its members reference internal types like BulkObservableCollection
        // and PmProjectRow). DI's ActivatorUtilities calls internal ctors in
        // the same assembly without trouble.
        internal WorkloadMeetingWindow(PmToolsViewModel vm, WorkloadMeetingPanelViewModel meetingPanel, DeltekOdbcOptions odbcOptions)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            _meetingPanel = meetingPanel ?? throw new ArgumentNullException(nameof(meetingPanel));
            _odbcOptions = odbcOptions;
            InitializeComponent();
            DataContext = _vm;

            // Round 52: hand the meeting VM to the column-visibility proxy.
            // DataGrid columns live outside the visual tree, so Meeting Mode
            // bindings reach IsMeetingMode through this Freezable instead of
            // ElementName/RelativeSource.
            ((Kor.Operations.Converters.BindingProxy)Resources["MeetingPanelProxy"]).Data = _meetingPanel;

            var contextBuilder = Kor.Operations.Services.AppServices.Get<Kor.Operations.Services.AppAiContextBuilder>();
            contextBuilder.Register(_vm);
            AiPanel.Initialize(Kor.Operations.Services.AppServices.Get<Kor.Operations.Services.AppAiService>(), _vm);

            _meetingPanelPropertyChangedHandler = (_, e) =>
            {
                if (e.PropertyName is nameof(WorkloadMeetingPanelViewModel.CurrentProjects)
                                    or nameof(WorkloadMeetingPanelViewModel.SelectedMeeting))
                    SyncMeetingPrioritiesToRows();
                else if (e.PropertyName is nameof(WorkloadMeetingPanelViewModel.IsMeetingMode))
                    SetAllPmGroupsExpanded(_meetingPanel.IsMeetingMode);
            };
            _meetingPanel.PropertyChanged += _meetingPanelPropertyChangedHandler;
            _currentProjectsChangedHandler = (_, _) => SyncMeetingPrioritiesToRows();
            _meetingPanel.CurrentProjects.CollectionChanged += _currentProjectsChangedHandler;
            // BuildPmGroups() recreates the group VMs (default collapsed) on
            // every filter/sort/refresh — re-expand them while Meeting Mode is on.
            _pmGroupsChangedHandler = (_, _) =>
            {
                if (_meetingPanel.IsMeetingMode)
                    SetAllPmGroupsExpanded(true);
            };
            _vm.PmGroups.CollectionChanged += _pmGroupsChangedHandler;

            // Round 52f (review finding 4): mirror notes edits from any surface
            // (board, grid, orphan panel) onto the matching grid row. The
            // sync guard stops the row's TextChanged from echoing the
            // programmatic write back as a user edit; same-value sets no-op in
            // the MeetingNotes setter, so the grid-originated echo is inert.
            _projectNotesUpdatedHandler = (wbs1, notes) =>
            {
                var row = _vm.ProjectRows.FirstOrDefault(r => string.Equals(r.Wbs1, wbs1, StringComparison.OrdinalIgnoreCase));
                if (row == null) return;
                _isSyncingMeetingPriorities = true;
                try { row.MeetingNotes = notes; }
                finally { _isSyncingMeetingPriorities = false; }
            };
            _meetingPanel.ProjectNotesUpdated += _projectNotesUpdatedHandler;
        }

        // Round 52: Meeting Mode wants every PM's list open so the room can
        // scan top to bottom; exiting returns to the compact default.
        private void SetAllPmGroupsExpanded(bool expanded)
        {
            foreach (var group in _vm.PmGroups)
                group.IsExpanded = expanded;
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

            // Round 52i: open in the simple meeting view with every PM expanded.
            // The PmGroups.CollectionChanged handler covers the fresh-load path;
            // this also covers reopening with the singleton VM's cached groups,
            // where no collection-changed fires.
            if (_meetingPanel.IsMeetingMode)
                SetAllPmGroupsExpanded(true);
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

        private void ProjectGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGrid { SelectedItem: PmProjectRow row } || !IsDataGridRowDoubleClick(e))
                return;

            // Interactive columns (Hotlist 0, Priority 1, Notes 2) — don't treat
            // their double-click as a row open. Pre-Round-52 this only excluded
            // index 0; the comment claimed Priority but Hotlist holds that slot.
            if (GetClickedCell(e)?.Column.DisplayIndex is 0 or 1 or 2)
                return;

            var counts = BuildPortfolioCounts();
            var win = new Financials.ProjectFinancialDetailWindow(row.Source, counts) { Owner = this };
            win.Show();
        }

        // Round 48: PmGroupExpand_Click was accidentally stripped by the
        // PowerShell-driven legacy-template extraction (adjacent one-liner
        // Show*CapacityRisk_Click methods threw off the brace-depth counter
        // and ate the next multi-line method). Restored to match legacy.
        private void PmGroupExpand_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: PmGroupViewModel group })
                group.IsExpanded = !group.IsExpanded;
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
            Kor.Operations.Services.AppServices.Get<Kor.Operations.Services.AppAiContextBuilder>().Unregister(_vm);
            // Round 52d/52e: detach every handler from the singleton VMs so the
            // closed window isn't kept alive and its handlers stop firing.
            if (_meetingPanelPropertyChangedHandler != null)
            {
                _meetingPanel.PropertyChanged -= _meetingPanelPropertyChangedHandler;
                _meetingPanelPropertyChangedHandler = null;
            }
            if (_currentProjectsChangedHandler != null)
            {
                _meetingPanel.CurrentProjects.CollectionChanged -= _currentProjectsChangedHandler;
                _currentProjectsChangedHandler = null;
            }
            if (_pmGroupsChangedHandler != null)
            {
                _vm.PmGroups.CollectionChanged -= _pmGroupsChangedHandler;
                _pmGroupsChangedHandler = null;
            }
            if (_projectNotesUpdatedHandler != null)
            {
                _meetingPanel.ProjectNotesUpdated -= _projectNotesUpdatedHandler;
                _projectNotesUpdatedHandler = null;
            }
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
                // Round 52g (review finding 10): tolerant builds — CurrentProjects
                // is DB-unique on (MeetingId, Wbs1) but ProjectRows relies on an
                // unenforced Deltek snapshot invariant; a duplicate Wbs1 must not
                // crash the window. First occurrence wins.
                var lookup = new System.Collections.Generic.Dictionary<string, (int Priority, string Notes)>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in _meetingPanel.CurrentProjects)
                    lookup.TryAdd(p.Wbs1, (p.Priority, p.Notes ?? string.Empty));
                foreach (var row in _vm.ProjectRows)
                {
                    if (lookup.TryGetValue(row.Wbs1, out var m))
                    {
                        row.MeetingPriority = m.Priority;
                        row.MeetingNotes = m.Notes;
                        row.HasMeetingRow = true;
                    }
                    else
                    {
                        row.MeetingPriority = 0;
                        row.MeetingNotes = string.Empty;
                        row.HasMeetingRow = false;
                    }
                }

                var projectLookup = new System.Collections.Generic.Dictionary<string, PmProjectRow>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in _vm.ProjectRows)
                    projectLookup.TryAdd(r.Wbs1, r);

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
            // Round 52g (review finding 6): SelectionChanged also fires for
            // binding-driven changes — container realization (group expand,
            // Meeting Mode expand-all) and re-binds after column sorts. Only a
            // dropdown pick or a keyboard change on a focused combo is a user
            // edit; everything else fired a phantom upsert + full meeting
            // reload per realized row.
            if (!cb.IsDropDownOpen && !cb.IsKeyboardFocusWithin) return;
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
            // (This fix lived in PmCapacityWindow's handler and was dropped when
            // Round 48b moved the meeting board here — restored verbatim.)
            var previousPriority = _meetingPanel.CurrentProjects
                .FirstOrDefault(p => string.Equals(p.Wbs1, row.Wbs1, StringComparison.OrdinalIgnoreCase))?.Priority ?? 0;
            var attemptedPriority = priority;

            var ok = await _meetingPanel.UpsertPriorityFromUiAsync(row.Wbs1, priority);
            if (!ok)
            {
                // Round 40 (R4-T2.002): if the user changed priority again before
                // our failure landed, the row has moved on — reverting would
                // clobber a later successful save. Log the stale failure and
                // leave the UI alone.
                if (row.MeetingPriority != attemptedPriority)
                {
                    Serilog.Log.Information(
                        "PM Tools: priority save for {Wbs1} failed (attempted {Attempted}) but row has moved to {Current}; skipping revert.",
                        row.Wbs1, attemptedPriority, row.MeetingPriority);
                    return;
                }
                _isSyncingMeetingPriorities = true;
                try { row.MeetingPriority = previousPriority; }
                finally { _isSyncingMeetingPriorities = false; }
                MessageBox.Show(this,
                    $"Failed to save priority for {row.Wbs1}.\n\n{_meetingPanel.MeetingError ?? "See logs for details."}",
                    "Workload Meeting — Priority Save Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        // Round 52L: chip click — scroll the (filter-independent) priority list
        // to the first project at the clicked priority. The old version searched
        // the FILTERED PM grid, so a P-level whose projects were hidden by the
        // Watchlist/Phase filter found nothing and silently no-op'd. The
        // priority list contains every prioritized project, so this always hits.
        private void PriorityChip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: Kor.Operations.App.PMTools.WorkloadMeetingPriorityChip chip }) return;

            var target = _meetingPanel.PriorityProjects.FirstOrDefault(p => p.Priority == chip.Priority);
            if (target == null) return;

            // The priority list ItemsControl is not virtualized, so its
            // containers are realized — bring the row into view directly.
            if (PriorityListItems.ItemContainerGenerator.ContainerFromItem(target) is FrameworkElement fe)
                fe.BringIntoView();
        }

        // Round 52: toggles the row-details meeting-notes editor for the
        // clicked row. Walking up to the DataGridRow (instead of binding
        // DetailsVisibility) keeps the toggle local to the physical row.
        private void MeetingNotesToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            DependencyObject? cur = btn;
            while (cur != null && cur is not DataGridRow)
                cur = VisualTreeHelper.GetParent(cur);
            if (cur is DataGridRow row)
                row.DetailsVisibility = row.DetailsVisibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;
        }

        // Round 52: write path for the grid notes editor. The TextBox binding
        // already pushed the text onto PmProjectRow.MeetingNotes; this routes
        // it into the meeting VM's per-Wbs1 debounce. _isSyncingMeetingPriorities
        // guards against programmatic sync writes re-entering as user edits
        // (binding target updates fire TextChanged synchronously inside the
        // sync loop, so the flag is still set).
        private void MeetingNotesTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isSyncingMeetingPriorities) return;
            if (sender is not TextBox tb || tb.DataContext is not PmProjectRow row) return;
            if (!_meetingPanel.IsCurrentMeeting || _meetingPanel.SelectedMeeting == null) return;
            _meetingPanel.QueueProjectNotesSaveFromUi(row.Wbs1, tb.Text);
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
