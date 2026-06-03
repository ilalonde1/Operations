#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Kor.Opportunities.Data.Ingestion;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public sealed class JobRunHistoryViewModel : INotifyPropertyChanged
{
    private readonly IJobScheduleStore _store;
    private string _statusMessage = "Loading...";

    public JobRunHistoryViewModel(IJobScheduleStore store, string jobName)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        JobName = string.IsNullOrWhiteSpace(jobName) ? throw new ArgumentException("Job name is required.", nameof(jobName)) : jobName;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string JobName { get; }

    public ObservableCollection<JobRunRow> Runs { get; } = new();

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        try
        {
            StatusMessage = "Loading job history...";
            var rows = await _store.GetRecentRunsAsync(JobName, 20, ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();

            Application.Current.Dispatcher.Invoke(() =>
            {
                Runs.Clear();
                foreach (var row in rows)
                {
                    Runs.Add(row);
                }
            });

            StatusMessage = $"Loaded {rows.Count:N0} run(s).";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Job history load failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
