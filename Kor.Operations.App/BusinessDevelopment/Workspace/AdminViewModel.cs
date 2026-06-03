#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Ingestion;
using Kor.Opportunities.Data.MajorProjects;
using Kor.Opportunities.Data.Sources;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public sealed class AdminViewModel : INotifyPropertyChanged, Kor.Operations.Services.IAiContextProvider
{
    // T5.001 audit fix (2026-05-30): expose Admin state to AI.
    public string ProviderName => "BD Admin";
    public bool HasData => Sources.Count > 0 || RecentRuns.Count > 0 || ScheduledJobs.Count > 0;
    public string BuildContext()
        => $"BD Admin — {Sources.Count:N0} ingestion sources, {RecentRuns.Count:N0} recent runs visible, {ScheduledJobs.Count:N0} scheduled jobs.";
    public string BuildLocalContext() => BuildContext();

    private readonly IOpportunitySourceStore _sources;
    private readonly IIngestionRunStore _runs;
    private readonly IJobScheduleStore _schedules;
    private readonly IBdDashboardStore _dashboard;
    private string _statusMessage = "Ready.";

    public AdminViewModel(
        IOpportunitySourceStore sources,
        IIngestionRunStore runs,
        IJobScheduleStore schedules,
        IBdDashboardStore dashboard)
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _runs = runs ?? throw new ArgumentNullException(nameof(runs));
        _schedules = schedules ?? throw new ArgumentNullException(nameof(schedules));
        _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));

        RunNowCommand = new RelayCommand(o => _ = RunNowAsync(o as ScheduledJobRow));
        ToggleEnabledCommand = new RelayCommand(o => _ = ToggleEnabledAsync(o as ScheduledJobRow));
        OpenReportCommand = new RelayCommand(
            o => OpenReport(o as ScheduledJobRow),
            o => (o as ScheduledJobRow)?.HasReportPath == true);
        ViewHistoryCommand = new RelayCommand(o => ShowHistory(o as ScheduledJobRow));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<OpportunitySource> Sources { get; } = new();

    public ObservableCollection<IngestionRunRow> RecentRuns { get; } = new();

    public ObservableCollection<ScheduledJobRow> ScheduledJobs { get; } = new();

    public ObservableCollection<DataHealthRow> Health { get; } = new();

    public ICommand RunNowCommand { get; }

    public ICommand ToggleEnabledCommand { get; }

    public ICommand OpenReportCommand { get; }

    public ICommand ViewHistoryCommand { get; }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        try
        {
            StatusMessage = "Loading admin cockpit...";
            var sources = await _sources.ListEnabledAsync(ct).ConfigureAwait(true);
            var runs = await _runs.ListRecentAsync(50, ct).ConfigureAwait(true);
            var schedules = await _schedules.ListWithLastRunAsync(ct).ConfigureAwait(true);
            var health = await _dashboard.GetDataHealthAsync(ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();

            Replace(Sources, sources);
            Replace(RecentRuns, runs.Select(ToRunRow));
            Replace(ScheduledJobs, schedules.Select(ToScheduledJobRow));
            Replace(Health, health);
            StatusMessage = $"Loaded {sources.Count:N0} sources, {schedules.Count:N0} job schedules, {runs.Count:N0} recent runs, {health.Count:N0} health rows.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Admin load failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static IngestionRunRow ToRunRow(IngestionRun run)
        => new(
            run.ProviderName,
            run.StartedAtUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm"),
            run.Success ? "ok" : "failed",
            run.InsertedCount,
            run.DuplicateCount,
            run.SkippedCount,
            run.FailedCount,
            FormatDuration(run.StartedAtUtc, run.EndedAtUtc));

    private static ScheduledJobRow ToScheduledJobRow(JobScheduleRow row)
        => new(
            row.JobName,
            row.CronSchedule ?? "",
            row.Enabled,
            row.Category,
            row.LastReportPath,
            row.LastRunAtUtc?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? "",
            FormatScheduleResult(row.LastSuccess, row.LastSummary));

    private async Task RunNowAsync(ScheduledJobRow? row)
    {
        if (row is null)
        {
            return;
        }

        try
        {
            await _schedules.EnqueueTriggerAsync(row.JobName, "AdminUI", CancellationToken.None).ConfigureAwait(true);
            StatusMessage = $"Queued {row.JobName} - should fire within 15s.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to queue {row.JobName}: {ex.Message}";
        }
    }

    private async Task ToggleEnabledAsync(ScheduledJobRow? row)
    {
        if (row is null)
        {
            return;
        }

        try
        {
            await _schedules.UpdateEnabledAsync(row.JobName, row.IsEnabled, CancellationToken.None).ConfigureAwait(true);
            StatusMessage = $"{row.JobName}: Enabled = {row.IsEnabled}. Takes effect within ~10s.";
        }
        catch (Exception ex)
        {
            row.IsEnabled = !row.IsEnabled;
            StatusMessage = $"Failed to toggle {row.JobName}: {ex.Message}";
        }
    }

    private void OpenReport(ScheduledJobRow? row)
    {
        if (string.IsNullOrWhiteSpace(row?.LastReportPath))
        {
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = row.LastReportPath,
                UseShellExecute = true,
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open report: {ex.Message}";
        }
    }

    private void ShowHistory(ScheduledJobRow? row)
    {
        if (row is null)
        {
            return;
        }

        var vm = new JobRunHistoryViewModel(_schedules, row.JobName);
        var win = new JobRunHistoryWindow
        {
            DataContext = vm,
            Owner = Application.Current?.MainWindow,
        };
        _ = vm.LoadAsync(CancellationToken.None);
        win.ShowDialog();
    }

    private static string FormatScheduleResult(bool? success, string? summary)
    {
        var status = success switch
        {
            true => "ok",
            false => "failed",
            _ => "not run",
        };

        return string.IsNullOrWhiteSpace(summary) ? status : $"{status}: {summary}";
    }

    private static string FormatDuration(DateTimeOffset startedAtUtc, DateTimeOffset? endedAtUtc)
    {
        if (!endedAtUtc.HasValue)
        {
            return "";
        }

        var duration = endedAtUtc.Value - startedAtUtc;
        return duration.TotalMinutes >= 1
            ? duration.ToString(@"m\:ss")
            : duration.ToString(@"s\.fff\s");
    }

    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> rows)
    {
        collection.Clear();
        foreach (var row in rows)
        {
            collection.Add(row);
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

    public sealed record IngestionRunRow(
        string ProviderName,
        string Started,
        string Status,
        int InsertedCount,
        int DuplicateCount,
        int SkippedCount,
        int FailedCount,
        string Duration);

    public sealed class ScheduledJobRow : INotifyPropertyChanged
    {
        private bool _isEnabled;
        private string? _category;
        private string? _lastReportPath;

        public ScheduledJobRow(
            string jobName,
            string cronSchedule,
            bool isEnabled,
            string? category,
            string? lastReportPath,
            string lastRun,
            string result)
        {
            JobName = jobName;
            CronSchedule = cronSchedule;
            _isEnabled = isEnabled;
            _category = category;
            _lastReportPath = lastReportPath;
            LastRun = lastRun;
            Result = result;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string JobName { get; }

        public string CronSchedule { get; }

        public string? Category
        {
            get => _category;
            set => SetField(ref _category, value);
        }

        public string? LastReportPath
        {
            get => _lastReportPath;
            set
            {
                if (SetField(ref _lastReportPath, value))
                {
                    OnPropertyChanged(nameof(HasReportPath));
                }
            }
        }

        public bool HasReportPath => !string.IsNullOrWhiteSpace(LastReportPath);

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetField(ref _isEnabled, value);
        }

        public string LastRun { get; }

        public string Result { get; }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => _execute(parameter);
    }
}
