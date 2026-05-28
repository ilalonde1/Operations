#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Ingestion;
using Kor.Opportunities.Data.MajorProjects;
using Kor.Opportunities.Data.Sources;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public sealed class AdminViewModel : INotifyPropertyChanged
{
    private readonly IOpportunitySourceStore _sources;
    private readonly IIngestionRunStore _runs;
    private readonly IBdDashboardStore _dashboard;
    private string _statusMessage = "Ready.";

    public AdminViewModel(IOpportunitySourceStore sources, IIngestionRunStore runs, IBdDashboardStore dashboard)
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _runs = runs ?? throw new ArgumentNullException(nameof(runs));
        _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<OpportunitySource> Sources { get; } = new();

    public ObservableCollection<IngestionRunRow> RecentRuns { get; } = new();

    public ObservableCollection<DataHealthRow> Health { get; } = new();

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
            var health = await _dashboard.GetDataHealthAsync(ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();

            Replace(Sources, sources);
            Replace(RecentRuns, runs.Select(ToRunRow));
            Replace(Health, health);
            StatusMessage = $"Loaded {sources.Count:N0} sources, {runs.Count:N0} recent runs, {health.Count:N0} health rows.";
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
}
