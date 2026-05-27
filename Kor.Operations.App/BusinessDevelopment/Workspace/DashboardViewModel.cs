#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.MajorProjects;
using Kor.Opportunities.Data.Opportunities;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public sealed class DashboardViewModel : INotifyPropertyChanged
{
    private static readonly CultureInfo CanadianCulture = CultureInfo.GetCultureInfo("en-CA");

    private readonly IPrimePipelineStore _primePipeline;
    private readonly IOpportunityStore _opportunities;
    private string _totalPipelineDisplay = "0";
    private string _openRfpsDisplay = "0";
    private string _upcomingProjectsDisplay = "0";
    private string _closingSoonDisplay = "0";
    private string _pipelineValueDisplay = "$0";
    private string _statusMessage = "Ready.";

    public DashboardViewModel(IPrimePipelineStore primePipeline, IOpportunityStore opportunities)
    {
        _primePipeline = primePipeline ?? throw new ArgumentNullException(nameof(primePipeline));
        _opportunities = opportunities ?? throw new ArgumentNullException(nameof(opportunities));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public sealed record ChartBar(string Label, double Value, double Max);

    public sealed record FeedRow(string Name, string Buyer, string? Province, string Status, string? Deadline);

    public sealed record DeadlineRow(string Name, string? Province, string? Deadline, int DaysLeft);

    public ObservableCollection<FeedRow> LatestRfps { get; } = new();

    public ObservableCollection<ChartBar> SectorBars { get; } = new();

    public ObservableCollection<ChartBar> MarketBars { get; } = new();

    public ObservableCollection<DeadlineRow> Deadlines { get; } = new();

    public string TotalPipelineDisplay
    {
        get => _totalPipelineDisplay;
        private set => SetField(ref _totalPipelineDisplay, value);
    }

    public string OpenRfpsDisplay
    {
        get => _openRfpsDisplay;
        private set => SetField(ref _openRfpsDisplay, value);
    }

    public string UpcomingProjectsDisplay
    {
        get => _upcomingProjectsDisplay;
        private set => SetField(ref _upcomingProjectsDisplay, value);
    }

    public string ClosingSoonDisplay
    {
        get => _closingSoonDisplay;
        private set => SetField(ref _closingSoonDisplay, value);
    }

    public string PipelineValueDisplay
    {
        get => _pipelineValueDisplay;
        private set => SetField(ref _pipelineValueDisplay, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        try
        {
            StatusMessage = "Loading BD dashboard...";
            var pipeline = await _primePipeline.GetAllAsync(ct).ConfigureAwait(true);
            var opps = await _opportunities.ListAsync(ct).ConfigureAwait(true);

            var now = DateTimeOffset.UtcNow;
            var sevenDays = now.AddDays(7);
            var thirtyDays = now.AddDays(30);

            TotalPipelineDisplay = pipeline.Count.ToString("N0", CanadianCulture);
            OpenRfpsDisplay = pipeline.Count(r => string.Equals(r.PipelineType, "Open RFP", StringComparison.OrdinalIgnoreCase)).ToString("N0", CanadianCulture);
            UpcomingProjectsDisplay = pipeline.Count(r => string.Equals(r.PipelineType, "Pipeline Project", StringComparison.OrdinalIgnoreCase)).ToString("N0", CanadianCulture);
            ClosingSoonDisplay = opps.Count(o => o.SubmissionDeadlineUtc >= now && o.SubmissionDeadlineUtc <= sevenDays).ToString("N0", CanadianCulture);
            PipelineValueDisplay = string.Format(CanadianCulture, "{0:C0}", pipeline.Sum(r => r.EstimatedValueCad ?? 0m));

            Replace(LatestRfps, opps
                .OrderByDescending(o => o.CreatedAtUtc)
                .Take(15)
                .Select(o => new FeedRow(
                    o.Name,
                    o.BuyerName,
                    o.ProjectProvince,
                    o.Status.ToString(),
                    o.SubmissionDeadlineUtc?.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));

            Replace(SectorBars, BuildBars(pipeline
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Sector) ? "Other" : r.Sector!)
                .Select(g => (Label: g.Key, Count: g.Count()))));

            Replace(MarketBars, BuildBars(pipeline
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Province) ? "" : r.Province!)
                .Select(g => (Label: g.Key, Count: g.Count()))));

            Replace(Deadlines, opps
                .Where(o => o.SubmissionDeadlineUtc >= now && o.SubmissionDeadlineUtc <= thirtyDays)
                .OrderBy(o => o.SubmissionDeadlineUtc)
                .Select(o => new DeadlineRow(
                    o.Name,
                    o.ProjectProvince,
                    o.SubmissionDeadlineUtc?.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    o.SubmissionDeadlineUtc.HasValue ? (int)Math.Floor((o.SubmissionDeadlineUtc.Value - now).TotalDays) : 0)));

            StatusMessage = $"Loaded {pipeline.Count:N0} pipeline rows and {opps.Count:N0} opportunities.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Dashboard load failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static ChartBar[] BuildBars(IEnumerable<(string Label, int Count)> groups)
    {
        var list = groups
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Label, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
        var max = list.Length == 0 ? 0 : list.Max(g => g.Count);
        return list.Select(g => new ChartBar(g.Label, g.Count, max)).ToArray();
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
}
