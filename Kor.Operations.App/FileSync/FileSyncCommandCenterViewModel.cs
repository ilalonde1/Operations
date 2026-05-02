#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core;
using Kor.Operations.Services;

namespace Kor.Operations.App.FileSync;

public sealed class FileSyncCommandCenterViewModel : ObservableObject, IAiContextProvider
{
    private readonly FileSyncControlPlaneReader _reader;
    private string _statusMessage = "Ready.";
    private bool _isLoading;
    private int _pendingTriggerCount;
    private string _currentUserUpn = $"{Environment.UserName}@korstructural.com";
    private bool _autoRefresh = true;

    public FileSyncCommandCenterViewModel(FileSyncControlPlaneReader reader)
    {
        _reader = reader;
    }

    // Exposed so the parent window can hand the same reader to the
    // detail window when the user double-clicks a job. Keeps the VM free
    // of WPF dependencies (no `new Window(...)` in VM code).
    public FileSyncControlPlaneReader Reader => _reader;

    public ObservableCollection<HeartbeatRow> Heartbeats { get; } = new();

    public ObservableCollection<JobRow> Jobs { get; } = new();

    public ObservableCollection<PendingTriggerRow> PendingTriggers { get; } = new();

    // Last-24h roll-up powering the run-history ribbon. Capped at 200 to keep
    // the SQL pull cheap; in practice we'd see ~20-30 runs/day across all jobs.
    public ObservableCollection<JobRunRow> RecentRuns { get; } = new();

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    public int PendingTriggerCount
    {
        get => _pendingTriggerCount;
        private set => SetField(ref _pendingTriggerCount, value);
    }

    public string CurrentUserUpn
    {
        get => _currentUserUpn;
        set => SetField(ref _currentUserUpn, value);
    }

    // Toggled by the Command Center's "Auto-refresh" checkbox. When false the
    // DispatcherTimer skips its tick body; the operator still has the manual
    // Refresh button.
    public bool AutoRefresh
    {
        get => _autoRefresh;
        set => SetField(ref _autoRefresh, value);
    }

    public string ProviderName => "FileSync Command Center";

    public bool HasData => Heartbeats.Count > 0 || Jobs.Count > 0;

    public async Task RefreshAsync(CancellationToken ct)
    {
        if (IsLoading)
            return;
        IsLoading = true;
        StatusMessage = "Loading...";
        try
        {
            var heartbeats = await _reader.GetHeartbeatsAsync(ct).ConfigureAwait(true);
            var jobs = await _reader.GetJobsAsync(ct).ConfigureAwait(true);
            var pending = await _reader.GetPendingTriggersAsync(ct).ConfigureAwait(true);
            var recentRuns = await _reader.GetRecentRunsAcrossAllJobsAsync(200, failuresOnly: false, ct).ConfigureAwait(true);

            // Group the recent runs by job and snapshot the last 10 statuses
            // (oldest -> newest) onto each JobRow so the Trend column has
            // something to render. recentRuns already arrives StartedAt DESC.
            var trendByJob = recentRuns
                .GroupBy(r => r.JobName, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<string>)g.Take(10).Reverse().Select(r => r.Status).ToList(),
                    StringComparer.Ordinal);

            Heartbeats.Clear();
            foreach (var h in heartbeats)
                Heartbeats.Add(h);

            Jobs.Clear();
            foreach (var j in jobs)
            {
                var trend = trendByJob.TryGetValue(j.JobName, out var t) ? t : Array.Empty<string>();
                Jobs.Add(new JobRow
                {
                    JobName = j.JobName,
                    DisplayName = j.DisplayName,
                    Mode = j.Mode,
                    CronExpression = j.CronExpression,
                    Enabled = j.Enabled,
                    LastConfigChangedAt = j.LastConfigChangedAt,
                    Notes = j.Notes,
                    LastRunId = j.LastRunId,
                    LastRunStatus = j.LastRunStatus,
                    LastRunStartedAt = j.LastRunStartedAt,
                    LastRunCompletedAt = j.LastRunCompletedAt,
                    LastRunSummary = j.LastRunSummary,
                    RecentRunStatuses = trend,
                });
            }

            PendingTriggers.Clear();
            foreach (var p in pending)
                PendingTriggers.Add(p);

            // Filter to last 24h client-side -- the reader returns the latest
            // 200 regardless. Keeps the ribbon's window definition local.
            var cutoff = DateTimeOffset.Now.AddHours(-24);
            RecentRuns.Clear();
            foreach (var run in recentRuns)
                if (run.StartedAt >= cutoff)
                    RecentRuns.Add(run);

            PendingTriggerCount = pending.Count;
            StatusMessage = $"Loaded at {DateTime.Now:HH:mm:ss}. Pending triggers: {pending.Count}. Runs in last 24h: {RecentRuns.Count}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Load failed: {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task ToggleModeAsync(JobRow row, CancellationToken ct)
    {
        var newMode = row.Mode == "Shadow" ? "Live" : "Shadow";
        try
        {
            await _reader.SetJobModeAsync(row.JobName, newMode, CurrentUserUpn, ct).ConfigureAwait(true);
            StatusMessage = $"Job '{row.JobName}' set to {newMode} by {CurrentUserUpn}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Mode change failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async Task<bool> CancelPendingTriggerAsync(PendingTriggerRow row, CancellationToken ct)
    {
        try
        {
            var ok = await _reader.CancelPendingTriggerAsync(row.TriggerId, CurrentUserUpn, ct).ConfigureAwait(true);
            StatusMessage = ok
                ? $"Cancelled trigger #{row.TriggerId} for '{row.JobName}'."
                : $"Trigger #{row.TriggerId} could not be cancelled (likely already claimed by the service).";
            return ok;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Cancel failed: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    public async Task<long?> QueueManualFireAsync(JobRow row, CancellationToken ct)
    {
        try
        {
            var triggerId = await _reader.QueueManualFireAsync(row.JobName, CurrentUserUpn, args: null, ct).ConfigureAwait(true);
            StatusMessage = $"Queued manual fire for '{row.JobName}' (trigger #{triggerId}). The service will pick it up within ~5 seconds.";
            return triggerId;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Manual fire failed: {ex.GetType().Name}: {ex.Message}";
            return null;
        }
    }

    public string BuildContext() => BuildLocalContext();

    public string BuildLocalContext()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("FileSync Command Center status:");
        if (Heartbeats.Count == 0)
        {
            sb.AppendLine("  No service heartbeats recorded yet.");
        }
        else
        {
            foreach (var h in Heartbeats)
            {
                sb.AppendLine($"  Host {h.HostName}: mode={h.GlobalMode} version={h.ServiceVersion ?? "?"} jobs={h.JobsRegistered} lastHeartbeat={h.LastHeartbeatAt:O} stale={h.IsStale}");
            }
        }

        if (Jobs.Count == 0)
        {
            sb.AppendLine("  No jobs registered.");
        }
        else
        {
            sb.AppendLine("  Jobs:");
            foreach (var j in Jobs)
            {
                sb.AppendLine($"    {j.JobName} [{j.Mode}] cron={j.CronExpression ?? "(manual)"} enabled={j.Enabled}");
            }
        }

        sb.AppendLine($"  Pending manual triggers: {PendingTriggerCount}");
        return sb.ToString();
    }
}
