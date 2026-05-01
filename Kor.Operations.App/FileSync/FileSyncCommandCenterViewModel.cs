#nullable enable
using System;
using System.Collections.ObjectModel;
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
            var pendingCount = await _reader.GetPendingTriggerCountAsync(ct).ConfigureAwait(true);

            Heartbeats.Clear();
            foreach (var h in heartbeats)
                Heartbeats.Add(h);

            Jobs.Clear();
            foreach (var j in jobs)
                Jobs.Add(j);

            PendingTriggerCount = pendingCount;
            StatusMessage = $"Loaded at {DateTime.Now:HH:mm:ss}. Pending triggers: {pendingCount}.";
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
