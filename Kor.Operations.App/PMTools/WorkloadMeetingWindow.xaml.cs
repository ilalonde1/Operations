#nullable enable
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Kor.Operations.App.PMTools;
using Kor.Operations.Services; // HeaderLoader

namespace Kor.Operations.PMTools
{
    /// <summary>
    /// Round 38b — workload-meeting half of the legacy PmToolsWindow. Hosts the
    /// bi-weekly meeting picker, priority-projects board, per-project notes
    /// and meeting export. Shares its <see cref="WorkloadMeetingPanelViewModel"/>
    /// with <see cref="PmToolsWindow"/> / future PmCapacityWindow via a DI
    /// singleton, so priorities assigned in the capacity window's per-row
    /// ComboBox surface here live (no refresh needed).
    /// </summary>
    internal partial class WorkloadMeetingWindow : Window
    {
        private readonly WorkloadMeetingPanelViewModel _meetingPanel;
        private readonly PmToolsViewModel _pmToolsVm;

        // The export-to-Excel path writes both meeting data AND the PM groups
        // (filtered or full) so the receiver gets a workbook that mirrors the
        // capacity-window view. PmToolsViewModel is also a Singleton, so we
        // pull it here purely for the export — this window does NOT render
        // PM groups, doesn't refresh PmToolsVm, doesn't register it with the
        // AI context (that's the PmCapacityWindow's job).
        internal WorkloadMeetingWindow(WorkloadMeetingPanelViewModel meetingPanel, PmToolsViewModel pmToolsVm)
        {
            _meetingPanel = meetingPanel ?? throw new ArgumentNullException(nameof(meetingPanel));
            _pmToolsVm = pmToolsVm ?? throw new ArgumentNullException(nameof(pmToolsVm));
            InitializeComponent();
            DataContext = _meetingPanel;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _ = ApplyHeaderAsync();
            await _meetingPanel.LoadAsync();
        }

        private async Task ApplyHeaderAsync()
        {
            try
            {
                await HeaderLoader.ApplyAsync(HeaderBar);
            }
            catch (Exception ex)
            {
                // Best-effort header decoration; meeting still works without it.
                Serilog.Log.Warning(ex, "WorkloadMeetingWindow: header load failed.");
            }
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            // Re-pull the meetings list (e.g. someone added a new meeting from
            // another seat). The meeting VM owns this; we do not refresh the
            // shared PmToolsViewModel from here — the user does that from the
            // PM Capacity window.
            try
            {
                await _meetingPanel.LoadAsync();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "WorkloadMeetingWindow: meeting refresh failed.");
                MessageBox.Show(this, $"Refresh failed:\n{ex.Message}",
                    "Workload Meeting", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

        private async void DeleteMeetingBtn_Click(object sender, RoutedEventArgs e)
        {
            var meeting = _meetingPanel.SelectedMeeting;
            if (meeting == null) return;

            var label = meeting.MeetingDate.ToString("MMM d, yyyy");
            var result = MessageBox.Show(
                this,
                $"Delete the meeting from {label} and all its priority data?\n\nThis cannot be undone.",
                "Workload Meeting — Delete Meeting",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            await _meetingPanel.DeleteMeetingAsync();
        }

        private async void ExportMeetingBtn_Click(object sender, RoutedEventArgs e)
        {
            var meeting = _meetingPanel.SelectedMeeting;
            if (meeting == null) return;

            // The capacity window's filters (Phase / Scope / Construction Type /
            // My Projects / Search) decide which PM groups are visible there. We
            // surface that to the user before they export from here so they don't
            // get a partial workbook by surprise.
            var activeFilters = new System.Collections.Generic.List<string>();
            if (_pmToolsVm.ShowWatchlistOnly)
                activeFilters.Add("Scope: Watchlist only (default)");
            if (!string.IsNullOrWhiteSpace(_pmToolsVm.SelectedPhase)
                && !string.Equals(_pmToolsVm.SelectedPhase, "All", StringComparison.OrdinalIgnoreCase))
                activeFilters.Add($"Phase: {_pmToolsVm.SelectedPhase}");
            if (!string.IsNullOrWhiteSpace(_pmToolsVm.SelectedConstructionType)
                && !string.Equals(_pmToolsVm.SelectedConstructionType, "All", StringComparison.OrdinalIgnoreCase))
                activeFilters.Add($"Construction Type: {_pmToolsVm.SelectedConstructionType}");
            if (_pmToolsVm.ShowMyProjectsOnly)
                activeFilters.Add("My Projects Only");
            if (!string.IsNullOrWhiteSpace(_pmToolsVm.ProjectSearchText))
                activeFilters.Add($"Search: \"{_pmToolsVm.ProjectSearchText}\"");

            if (activeFilters.Count > 0)
            {
                var msg = "The export will only include projects matching the PM Capacity & Risk window's current filters:\n\n  "
                        + string.Join("\n  ", activeFilters)
                        + "\n\nContinue?";
                var result = MessageBox.Show(this, msg, "Workload Meeting — Export Filters Active",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes) return;
            }

            var sfd = new SaveFileDialog
            {
                Title = "Export Workload Meeting",
                Filter = "Excel Workbook|*.xlsx",
                FileName = $"WorkloadMeeting_{meeting.MeetingDate:yyyyMMdd}.xlsx",
                AddExtension = true,
                OverwritePrompt = true,
            };

            if (sfd.ShowDialog(this) != true) return;

            var dateLabel = meeting.MeetingDate.ToString("MMM d, yyyy");
            var priorityByWbs1 = new System.Collections.Generic.Dictionary<string, WorkloadMeetingProjectRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var pr in _meetingPanel.PriorityProjects.ToList())
                priorityByWbs1.TryAdd(pr.Wbs1, pr);

            var groups = _pmToolsVm.PmGroups.ToList();
            var notes = _meetingPanel.MeetingNotes ?? string.Empty;
            var path = sfd.FileName;

            try
            {
                await Task.Run(() => PmToolsExportService.ExportMeeting(path, dateLabel, groups, priorityByWbs1, notes))
                    .ConfigureAwait(true);
                MessageBox.Show(this, "Export completed.", "Workload Meeting — Export to Excel",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Export failed:\n{ex.Message}", "Workload Meeting — Export to Excel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Window_Closing(object? sender, CancelEventArgs e)
        {
            try
            {
                // Yield so any in-flight notes-textbox debounce + ComboBox-driven
                // priority upserts reach the meeting store before we flush; matches
                // the legacy PmToolsWindow.Window_Closing pattern.
                await Task.Delay(100).ConfigureAwait(true);
                using var flushCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _meetingPanel.ForceSaveAllAsync(flushCts.Token);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "WorkloadMeetingWindow: meeting flush on close failed.");
            }
            // Round 38a: do NOT dispose _meetingPanel — it's a shared singleton.
            // DI container disposes on app shutdown.
        }
    }
}
