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
    private readonly IBdDashboardStore _bdDashboard;
    private string _totalPipelineDisplay = "0";
    private string _openRfpsDisplay = "0";
    private string _upcomingProjectsDisplay = "0";
    private string _closingSoonDisplay = "0";
    private string _pipelineValueDisplay = "$0";
    private int _radarCount;
    private int _bidWindowCount;
    private int _openSeatCount;
    private string _radarCountDisplay = "0";
    private string _bidWindowCountDisplay = "0";
    private string _openSeatCountDisplay = "0";
    private string _statusMessage = "Ready.";

    public DashboardViewModel(IPrimePipelineStore primePipeline, IOpportunityStore opportunities, IBdDashboardStore bdDashboard)
    {
        _primePipeline = primePipeline ?? throw new ArgumentNullException(nameof(primePipeline));
        _opportunities = opportunities ?? throw new ArgumentNullException(nameof(opportunities));
        _bdDashboard = bdDashboard ?? throw new ArgumentNullException(nameof(bdDashboard));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public sealed record ChartBar(string Label, double Value, double Max);

    public sealed record FeedRow(string Name, string Buyer, string? Province, string Status, string? Deadline);

    public sealed record DeadlineRow(string Name, string? Province, string? Deadline, int DaysLeft);

    public sealed record OpenStructuralSeatRow(string DisplayName, string? Market, string? Status, string? DisplacementRead);

    public sealed record CompetitorWatchPanelRow(string DisplayName, string? CapacityRead);

    public sealed record ForwardPipelinePanelRow(long Id, string ProjectName, string? ProponentName, string? Province, string CostDisplay);

    public ObservableCollection<FeedRow> LatestRfps { get; } = new();

    public ObservableCollection<ChartBar> SectorBars { get; } = new();

    public ObservableCollection<ChartBar> MarketBars { get; } = new();

    public ObservableCollection<ChartBar> StageBars { get; } = new();

    public ObservableCollection<ChartBar> MarketValueBars { get; } = new();

    public ObservableCollection<ChartBar> DeadlineMonthBars { get; } = new();

    public ObservableCollection<DeadlineRow> Deadlines { get; } = new();

    public ObservableCollection<OpenStructuralSeatRow> OpenStructuralSeats { get; } = new();

    public ObservableCollection<CompetitorWatchPanelRow> CompetitorWatch { get; } = new();

    public ObservableCollection<ForwardPipelinePanelRow> ForwardPipeline { get; } = new();

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

    public int RadarCount
    {
        get => _radarCount;
        private set => SetField(ref _radarCount, value);
    }

    public int BidWindowCount
    {
        get => _bidWindowCount;
        private set => SetField(ref _bidWindowCount, value);
    }

    public int OpenSeatCount
    {
        get => _openSeatCount;
        private set => SetField(ref _openSeatCount, value);
    }

    public string RadarCountDisplay
    {
        get => _radarCountDisplay;
        private set => SetField(ref _radarCountDisplay, value);
    }

    public string BidWindowCountDisplay
    {
        get => _bidWindowCountDisplay;
        private set => SetField(ref _bidWindowCountDisplay, value);
    }

    public string OpenSeatCountDisplay
    {
        get => _openSeatCountDisplay;
        private set => SetField(ref _openSeatCountDisplay, value);
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
            var openStructuralSeats = await _bdDashboard.GetOpenStructuralSeatsAsync(12, ct).ConfigureAwait(true);
            var competitorWatch = await _bdDashboard.GetCompetitorWatchAsync(12, ct).ConfigureAwait(true);
            var forwardPipeline = await _bdDashboard.GetForwardPipelineAsync(12, ct).ConfigureAwait(true);
            var funnel = await _bdDashboard.GetPipelineFunnelAsync(ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();

            var now = DateTimeOffset.UtcNow;
            var sevenDays = now.AddDays(7);
            var thirtyDays = now.AddDays(30);
            var sixMonths = now.AddMonths(6);

            TotalPipelineDisplay = pipeline.Count.ToString("N0", CanadianCulture);
            OpenRfpsDisplay = pipeline.Count(r => string.Equals(r.PipelineType, "Open RFP", StringComparison.OrdinalIgnoreCase)).ToString("N0", CanadianCulture);
            UpcomingProjectsDisplay = pipeline.Count(r => string.Equals(r.PipelineType, "Pipeline Project", StringComparison.OrdinalIgnoreCase)).ToString("N0", CanadianCulture);
            ClosingSoonDisplay = opps.Count(o => o.SubmissionDeadlineUtc >= now && o.SubmissionDeadlineUtc <= sevenDays).ToString("N0", CanadianCulture);
            PipelineValueDisplay = string.Format(CanadianCulture, "{0:C0}", pipeline.Sum(r => r.EstimatedValueCad ?? 0m));
            RadarCount = funnel.RadarCount;
            BidWindowCount = funnel.BidWindowCount;
            OpenSeatCount = funnel.OpenSeatCount;
            RadarCountDisplay = funnel.RadarCount.ToString("N0", CanadianCulture);
            BidWindowCountDisplay = funnel.BidWindowCount.ToString("N0", CanadianCulture);
            OpenSeatCountDisplay = funnel.OpenSeatCount.ToString("N0", CanadianCulture);

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

            Replace(StageBars, BuildBars(pipeline
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Stage) ? "" : r.Stage!)
                .Select(g => (Label: g.Key, Count: g.Count()))));

            Replace(MarketValueBars, BuildValueBars(pipeline
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Province) ? "" : r.Province!)
                .Select(g => (Label: g.Key, Value: (double)(g.Sum(r => r.EstimatedValueCad ?? 0m) / 1_000_000m)))));

            Replace(DeadlineMonthBars, BuildChronologicalBars(opps
                .Where(o => o.SubmissionDeadlineUtc >= now && o.SubmissionDeadlineUtc <= sixMonths)
                .GroupBy(o =>
                {
                    var deadline = o.SubmissionDeadlineUtc!.Value.LocalDateTime;
                    return new DateTime(deadline.Year, deadline.Month, 1);
                })
                .OrderBy(g => g.Key)
                .Select(g => (Label: g.Key.ToString("MMM yy", CultureInfo.InvariantCulture), Count: g.Count()))));

            Replace(Deadlines, opps
                .Where(o => o.SubmissionDeadlineUtc >= now && o.SubmissionDeadlineUtc <= thirtyDays)
                .OrderBy(o => o.SubmissionDeadlineUtc)
                .Select(o => new DeadlineRow(
                    o.Name,
                    o.ProjectProvince,
                    o.SubmissionDeadlineUtc?.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    o.SubmissionDeadlineUtc.HasValue ? (int)Math.Floor((o.SubmissionDeadlineUtc.Value - now).TotalDays) : 0)));

            Replace(OpenStructuralSeats, openStructuralSeats.Select(r => new OpenStructuralSeatRow(
                r.DisplayName,
                r.Market,
                r.Status,
                r.DisplacementRead)));

            Replace(CompetitorWatch, competitorWatch.Select(r => new CompetitorWatchPanelRow(
                r.DisplayName,
                r.CapacityRead)));

            Replace(ForwardPipeline, forwardPipeline.Select(r => new ForwardPipelinePanelRow(
                r.Id,
                r.ProjectName,
                r.ProponentName,
                r.Province,
                FormatCurrency(r.EstimatedCostCad))));

            StatusMessage = $"Loaded {pipeline.Count:N0} pipeline rows and {opps.Count:N0} opportunities.";
        }
        catch (OperationCanceledException)
        {
            throw;
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

    private static ChartBar[] BuildValueBars(IEnumerable<(string Label, double Value)> groups)
    {
        var list = groups
            .OrderByDescending(g => g.Value)
            .ThenBy(g => g.Label, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
        var max = list.Length == 0 ? 0 : list.Max(g => g.Value);
        return list.Select(g => new ChartBar(g.Label, g.Value, max)).ToArray();
    }

    private static ChartBar[] BuildChronologicalBars(IEnumerable<(string Label, int Count)> groups)
    {
        var list = groups.ToArray();
        var max = list.Length == 0 ? 0 : list.Max(g => g.Count);
        return list.Select(g => new ChartBar(g.Label, g.Count, max)).ToArray();
    }

    private static string FormatCurrency(decimal? value)
        => value.HasValue ? string.Format(CanadianCulture, "{0:C0}", value.Value) : "";

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
