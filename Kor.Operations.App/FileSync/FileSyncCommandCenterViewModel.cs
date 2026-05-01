#nullable enable
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

    public FileSyncCommandCenterViewModel(FileSyncControlPlaneReader reader)
    {
        _reader = reader;
    }

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

            Heartbeats.Clear();
            foreach (var h in heartbeats)
                Heartbeats.Add(h);

            Jobs.Clear();
            foreach (var j in jobs)
                Jobs.Add(j);

            StatusMessage = $"Loaded at {System.DateTime.Now:HH:mm:ss}.";
        }
        catch (System.Exception ex)
        {
            StatusMessage = $"Load failed: {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
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

        return sb.ToString();
    }
}
