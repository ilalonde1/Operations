#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.MajorProjects;

namespace Kor.Operations.App.Opportunities;

public sealed class CompetitionInfoViewModel : INotifyPropertyChanged, Kor.Operations.Services.IAiContextProvider
{
    // BD-Audit-2026-06-09 M16: expose Competition Info screen state to AI.
    // Registered alongside Rfps/Awards children, which carry the filter detail.
    public string ProviderName => "BD Competition Info";
    public bool HasData => true;
    public string BuildContext()
        => $"BD Competition Info screen open — Historical RFPs tab: {Rfps.Rows.Count:N0} rows ({Rfps.StatusText}); Awards tab: {Awards.Rows.Count:N0} rows ({Awards.StatusText}); Competitor watch tab: {CompetitorWatch.Count:N0} firms.";
    public string BuildLocalContext() => BuildContext();

    private readonly IBdDashboardStore _dashboard;
    private string _competitorWatchNote = "Loading…";

    public CompetitionInfoViewModel(
        CompetitionRfpsViewModel rfps,
        CompetitionAwardsViewModel awards,
        IBdDashboardStore dashboard)
    {
        Rfps = rfps;
        Awards = awards;
        _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
    }

    public CompetitionRfpsViewModel Rfps { get; }
    public CompetitionAwardsViewModel Awards { get; }

    /// <summary>Structural competitors with a research capacity read. Moved here
    /// from the BD dashboard on 2026-10-13 as the third tab of this page.</summary>
    public ObservableCollection<BdCompetitorWatchRow> CompetitorWatch { get; } = new();

    public string CompetitorWatchNote
    {
        get => _competitorWatchNote;
        private set
        {
            if (_competitorWatchNote != value)
            {
                _competitorWatchNote = value;
                OnPropertyChanged();
            }
        }
    }

    public async Task InitializeAsync()
    {
        await Rfps.InitializeAsync().ConfigureAwait(true);
        await Awards.InitializeAsync().ConfigureAwait(true);
        await LoadCompetitorWatchAsync(CancellationToken.None).ConfigureAwait(true);
    }

    public async Task LoadCompetitorWatchAsync(CancellationToken ct)
    {
        try
        {
            // Every firm with a read: 12 by name cut WSP and Weiler Smith Bowers off the end (2026-10-22).
            var rows = await _dashboard.GetCompetitorWatchAsync(200, ct).ConfigureAwait(true);
            CompetitorWatch.Clear();
            foreach (var row in rows)
            {
                CompetitorWatch.Add(row);
            }

            CompetitorWatchNote = rows.Count == 0
                ? "No competitor has a capacity read on file."
                : $"{rows.Count} firms with a capacity read from research. A read is an opinion with a date, not a verified fact.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            CompetitorWatchNote = "Could not retrieve the list: " + ex.Message;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
