#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core;

namespace Kor.Operations.App.FileSync;

// Cross-job recent-runs feed. Same JobRunRow POCO as Job Detail; this VM
// just queries with no JobName filter and exposes a "failures only" toggle.
public sealed class FileSyncActivityViewModel : ObservableObject
{
    private const int DefaultTopN = 50;

    private readonly FileSyncControlPlaneReader _reader;
    private bool _failuresOnly;
    private bool _isLoading;
    private string _statusMessage = "Ready.";
    private JobRunRow? _selectedRun;

    public FileSyncActivityViewModel(FileSyncControlPlaneReader reader)
    {
        _reader = reader;
    }

    public ObservableCollection<JobRunRow> Runs { get; } = new();

    public bool FailuresOnly
    {
        get => _failuresOnly;
        set
        {
            if (SetField(ref _failuresOnly, value))
            {
                // Refresh on toggle so the user sees the filter take effect immediately.
                _ = RefreshAsync(CancellationToken.None);
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public JobRunRow? SelectedRun
    {
        get => _selectedRun;
        set => SetField(ref _selectedRun, value);
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = "Loading...";
        try
        {
            var runs = await _reader.GetRecentRunsAcrossAllJobsAsync(DefaultTopN, _failuresOnly, ct).ConfigureAwait(true);
            Runs.Clear();
            foreach (var r in runs) Runs.Add(r);
            if (SelectedRun is null && Runs.Count > 0) SelectedRun = Runs[0];
            StatusMessage = $"Loaded {Runs.Count} run(s) at {DateTime.Now:HH:mm:ss}.";
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
}
