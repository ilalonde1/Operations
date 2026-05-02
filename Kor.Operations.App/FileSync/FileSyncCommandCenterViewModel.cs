#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
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
    private bool _showFailuresOnly;
    private readonly List<JobRunRow> _allRecentRuns = new();
    // Baseline RunId for the unacked-failures bell. Failures with RunId
    // greater than this are "new since last acknowledgement". Seeded to the
    // max RunId on the first refresh so previously-known failures don't
    // light the bell on app open.
    private long _ackedRunIdBaseline = -1;
    private int _unackedFailureCount;

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

    // Next 5 cron fires across all enabled jobs, sorted ascending by time.
    // Recomputed each refresh from the (now refreshed) Jobs collection.
    public ObservableCollection<UpcomingFireRow> UpcomingFires { get; } = new();

    // KPI tiles. All three are computed from existing collections; no extra
    // SQL. RefreshAsync raises PropertyChanged on these at the end of each
    // tick so the strip stays in sync with the rest of the view.
    public string HostsKpiHeadline { get; private set; } = "—";

    public string HostsKpiSubline { get; private set; } = "Hosts live";

    public Brush HostsKpiBrush { get; private set; } = KpiNeutral;

    public string JobsKpiHeadline { get; private set; } = "—";

    public string JobsKpiSubline { get; private set; } = "Jobs in Live mode";

    public Brush JobsKpiBrush { get; private set; } = KpiNeutral;

    public string FailuresKpiHeadline { get; private set; } = "—";

    public string FailuresKpiSubline { get; private set; } = "Failures (24h)";

    public Brush FailuresKpiBrush { get; private set; } = KpiNeutral;

    private static readonly Brush KpiGood    = Freeze(new SolidColorBrush(Color.FromRgb(0x22, 0x8B, 0x22)));
    private static readonly Brush KpiWarning = Freeze(new SolidColorBrush(Color.FromRgb(0xE5, 0xA8, 0x00)));
    private static readonly Brush KpiBad     = Freeze(new SolidColorBrush(Color.FromRgb(0xC1, 0x1E, 0x1E)));
    private static readonly Brush KpiNeutral = Freeze(new SolidColorBrush(Color.FromRgb(0x60, 0x9B, 0xD1)));

    private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

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

    // Ribbon-only filter. When true, RecentRuns is rebuilt from
    // _allRecentRuns keeping only Failed/TimedOut entries. The auto-refresh
    // tick honours the current toggle state without re-querying SQL.
    public bool ShowFailuresOnly
    {
        get => _showFailuresOnly;
        set
        {
            if (SetField(ref _showFailuresOnly, value))
                RebuildRecentRuns();
        }
    }

    public int UnackedFailureCount
    {
        get => _unackedFailureCount;
        private set
        {
            if (SetField(ref _unackedFailureCount, value))
                OnPropertyChanged(nameof(HasUnackedFailures));
        }
    }

    public bool HasUnackedFailures => _unackedFailureCount > 0;

    private bool _isConnectionLost;
    private DateTimeOffset? _lastSuccessfulRefreshAt;
    private string _connectionLostMessage = string.Empty;

    // True when the most recent RefreshAsync attempt threw. The XAML banner
    // binds visibility here. Cleared automatically on the next successful
    // refresh.
    public bool IsConnectionLost
    {
        get => _isConnectionLost;
        private set => SetField(ref _isConnectionLost, value);
    }

    public string ConnectionLostMessage
    {
        get => _connectionLostMessage;
        private set => SetField(ref _connectionLostMessage, value);
    }

    // Called by the status-bar bell. Lifts the baseline to the current max
    // RunId so the bell goes dark, then flips the ribbon into failures-only
    // so the operator's eye lands on the dots that drove the alert.
    public void AcknowledgeFailures()
    {
        if (_allRecentRuns.Count > 0)
            _ackedRunIdBaseline = _allRecentRuns.Max(r => r.RunId);
        UnackedFailureCount = 0;
        ShowFailuresOnly = true;
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

            // Group the recent runs by job and snapshot the last 10 runs
            // (oldest -> newest) onto each JobRow so the Trend column has
            // something to render. recentRuns already arrives StartedAt DESC.
            var trendByJob = recentRuns
                .GroupBy(r => r.JobName, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<JobRunRow>)g.Take(10).Reverse().ToList(),
                    StringComparer.Ordinal);

            Heartbeats.Clear();
            foreach (var h in heartbeats)
                Heartbeats.Add(h);

            Jobs.Clear();
            foreach (var j in jobs)
            {
                var trend = trendByJob.TryGetValue(j.JobName, out var t) ? t : Array.Empty<JobRunRow>();
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
                    RecentRunsTrend = trend,
                });
            }

            PendingTriggers.Clear();
            foreach (var p in pending)
                PendingTriggers.Add(p);

            // Filter to last 24h client-side -- the reader returns the latest
            // 200 regardless. Keeps the ribbon's window definition local.
            var cutoff = DateTimeOffset.Now.AddHours(-24);
            _allRecentRuns.Clear();
            foreach (var run in recentRuns)
                if (run.StartedAt >= cutoff)
                    _allRecentRuns.Add(run);
            RebuildRecentRuns();

            PendingTriggerCount = pending.Count;
            var failureCount = _allRecentRuns.Count(r => r.Status is "Failed" or "TimedOut");
            StatusMessage = $"Loaded at {DateTime.Now:HH:mm:ss}. Pending triggers: {pending.Count}. Runs in last 24h: {_allRecentRuns.Count} ({failureCount} failed).";
            RefreshKpis();
            RefreshUpcomingFires();
            _lastSuccessfulRefreshAt = DateTimeOffset.Now;
            IsConnectionLost = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Load failed: {ex.GetType().Name}: {ex.Message}";
            var since = _lastSuccessfulRefreshAt.HasValue
                ? $"last good data from {_lastSuccessfulRefreshAt.Value.LocalDateTime:HH:mm:ss}"
                : "no data has loaded yet";
            ConnectionLostMessage = $"Connection lost — {since} — auto-retry in ~15s. ({ex.GetType().Name}: {ex.Message})";
            IsConnectionLost = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void RefreshKpis()
    {
        // Hosts: green when every host's heartbeat is fresh; amber if any
        // are stale (between 2 and 5 min); red if any are flat-out down.
        var hostsTotal = Heartbeats.Count;
        var hostsLive = Heartbeats.Count(h => h.HealthStatus == "Live");
        var anyDown = Heartbeats.Any(h => h.HealthStatus == "Down");
        var anyStale = Heartbeats.Any(h => h.HealthStatus == "Stale");
        HostsKpiHeadline = hostsTotal == 0 ? "0 / 0" : $"{hostsLive} / {hostsTotal}";
        HostsKpiBrush = hostsTotal == 0 ? KpiNeutral
                       : anyDown ? KpiBad
                       : anyStale ? KpiWarning
                       : KpiGood;

        // Jobs in Live: informational. Stays neutral while the cutover is in
        // progress; only flips amber when zero jobs are Live (suggests the
        // service was rolled back to Shadow universally).
        var enabledJobs = Jobs.Count(j => j.Enabled);
        var liveJobs = Jobs.Count(j => j.Enabled && j.Mode == "Live");
        JobsKpiHeadline = enabledJobs == 0 ? "0 / 0" : $"{liveJobs} / {enabledJobs}";
        JobsKpiBrush = enabledJobs == 0 ? KpiNeutral
                       : liveJobs == 0 ? KpiWarning
                       : KpiNeutral;

        // Failures: green when zero, amber for one or two, red for three or
        // more. Counts the full 24h window regardless of the failures-only
        // toggle on the ribbon.
        var failures = _allRecentRuns.Count(r => r.Status is "Failed" or "TimedOut");
        FailuresKpiHeadline = failures.ToString();
        FailuresKpiBrush = failures == 0 ? KpiGood
                          : failures < 3 ? KpiWarning
                          : KpiBad;

        OnPropertyChanged(nameof(HostsKpiHeadline));
        OnPropertyChanged(nameof(HostsKpiBrush));
        OnPropertyChanged(nameof(JobsKpiHeadline));
        OnPropertyChanged(nameof(JobsKpiBrush));
        OnPropertyChanged(nameof(FailuresKpiHeadline));
        OnPropertyChanged(nameof(FailuresKpiBrush));

        // Bell baseline: on first refresh, take whatever's there so the user
        // doesn't get hammered by yesterday's failures the first time the
        // window opens. After that, count anything new.
        if (_ackedRunIdBaseline < 0)
            _ackedRunIdBaseline = _allRecentRuns.Count > 0 ? _allRecentRuns.Max(r => r.RunId) : 0;
        UnackedFailureCount = _allRecentRuns.Count(r => (r.Status is "Failed" or "TimedOut") && r.RunId > _ackedRunIdBaseline);
    }

    private void RefreshUpcomingFires()
    {
        // Project up to 5 occurrences per job, then merge and trim to the
        // next 5 across the whole system. With ~6 jobs this stays cheap.
        var allFires = new List<UpcomingFireRow>();
        var nowUtc = DateTime.UtcNow;
        foreach (var j in Jobs)
        {
            if (!j.Enabled || string.IsNullOrWhiteSpace(j.CronExpression)) continue;
            try
            {
                var expr = Cronos.CronExpression.Parse(j.CronExpression, Cronos.CronFormat.IncludeSeconds);
                var cursorUtc = nowUtc;
                for (int i = 0; i < 5; i++)
                {
                    var n = expr.GetNextOccurrence(cursorUtc, TimeZoneInfo.Local);
                    if (!n.HasValue) break;
                    allFires.Add(new UpcomingFireRow
                    {
                        JobName = j.JobName,
                        // n.Value is Kind=Utc; build a UTC DTO then shift
                        // to local for display.
                        FireAt = new DateTimeOffset(n.Value).ToLocalTime(),
                    });
                    // Step a second past so the next call returns the next
                    // occurrence rather than the same one.
                    cursorUtc = n.Value.AddSeconds(1);
                }
            }
            catch
            {
                // Bad cron string -- skip silently; the Jobs grid already
                // shows the cron as text so the operator can spot it.
            }
        }

        UpcomingFires.Clear();
        foreach (var f in allFires.OrderBy(f => f.FireAt).Take(5))
            UpcomingFires.Add(f);
    }

    private void RebuildRecentRuns()
    {
        RecentRuns.Clear();
        foreach (var run in _allRecentRuns)
        {
            if (_showFailuresOnly && run.Status is not ("Failed" or "TimedOut"))
                continue;
            RecentRuns.Add(run);
        }
    }

    // Bulk rollback. Walks the current Jobs collection and flips every
    // enabled Live job to Shadow. Returns the names of jobs that flipped so
    // the caller can post a status message; jobs already Shadow are skipped.
    public async Task<IReadOnlyList<string>> RollbackAllToShadowAsync(CancellationToken ct)
    {
        var flipped = new List<string>();
        // Snapshot first -- SetJobModeAsync writes to the DB but the
        // in-memory Jobs collection won't update until the next refresh, so
        // iterating a snapshot avoids surprises.
        var liveJobs = Jobs.Where(j => j.Enabled && j.Mode == "Live").Select(j => j.JobName).ToList();
        foreach (var name in liveJobs)
        {
            try
            {
                await _reader.SetJobModeAsync(name, "Shadow", CurrentUserUpn, ct).ConfigureAwait(true);
                flipped.Add(name);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Rollback partial: '{name}' failed ({ex.GetType().Name}: {ex.Message}).";
                return flipped;
            }
        }
        StatusMessage = flipped.Count == 0
            ? "Rollback skipped: no Live jobs to flip."
            : $"Rollback complete: {flipped.Count} job(s) set to Shadow.";
        return flipped;
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
