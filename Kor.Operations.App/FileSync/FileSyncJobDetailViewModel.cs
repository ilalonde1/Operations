#nullable enable
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core;

namespace Kor.Operations.App.FileSync;

// Backs FileSyncJobDetailWindow. Owns the run history list, the
// currently-selected run, and the resolved log/shadow folder paths
// the window's "Open log folder" / "Open shadow folder" buttons launch.
public sealed class FileSyncJobDetailViewModel : ObservableObject
{
    private readonly FileSyncControlPlaneReader _reader;
    private JobRow _job;
    private JobRunRow? _selectedRun;
    private string _statusMessage = "Ready.";
    private bool _isLoading;

    public FileSyncJobDetailViewModel(FileSyncControlPlaneReader reader, JobRow job)
    {
        _reader = reader;
        _job = job ?? throw new ArgumentNullException(nameof(job));
    }

    public JobRow Job
    {
        get => _job;
        private set => SetField(ref _job, value);
    }

    public ObservableCollection<JobRunRow> Runs { get; } = new();

    public ObservableCollection<JobKnobRow> Knobs { get; } = new();

    public JobRunRow? SelectedRun
    {
        get => _selectedRun;
        set => SetField(ref _selectedRun, value);
    }

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

    // Where the service writes its rolling log. Resolved via
    // SpecialFolder.CommonApplicationData so it works both on the local
    // box (running under user profile) and on KOR-APP01 (running under
    // app-admin). Mirrors SerilogBootstrap.cs in the service.
    public string LogsFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "KorOperations", "FileSync", "logs");

    public string ShadowFolderForJob => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "KorOperations", "FileSync", "shadow", _job.JobName);

    public async Task RefreshAsync(CancellationToken ct)
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = "Loading...";
        try
        {
            var runs = await _reader.GetRecentRunsAsync(_job.JobName, 30, ct).ConfigureAwait(true);
            var knobs = await _reader.GetKnobsAsync(_job.JobName, ct).ConfigureAwait(true);

            Runs.Clear();
            foreach (var run in runs) Runs.Add(run);
            if (SelectedRun is null && Runs.Count > 0) SelectedRun = Runs[0];

            Knobs.Clear();
            foreach (var k in knobs) Knobs.Add(k);

            StatusMessage = $"Loaded {Runs.Count} run(s), {Knobs.Count} knob(s) at {DateTime.Now:HH:mm:ss}.";
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

    public async Task SaveKnobAsync(string knobName, string? value, string by, CancellationToken ct)
    {
        await _reader.UpsertKnobAsync(_job.JobName, knobName, value, by, ct).ConfigureAwait(true);
        StatusMessage = $"Saved knob '{knobName}' = '{value ?? "(null)"}' at {DateTime.Now:HH:mm:ss}.";
    }

    public async Task<bool> DeleteKnobAsync(string knobName, CancellationToken ct)
    {
        var ok = await _reader.DeleteKnobAsync(_job.JobName, knobName, ct).ConfigureAwait(true);
        StatusMessage = ok
            ? $"Deleted knob '{knobName}' at {DateTime.Now:HH:mm:ss}."
            : $"Knob '{knobName}' not found.";
        return ok;
    }
}
