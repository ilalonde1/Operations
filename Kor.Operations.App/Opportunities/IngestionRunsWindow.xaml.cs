#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using Kor.Opportunities.Data.Ingestion;

namespace Kor.Operations.App.Opportunities;

/// <summary>
/// Modeless admin viewer for opportunities.IngestionRuns. Refresh-on-demand;
/// reuses IngestionRunRowView for display projections so the same pill
/// colours / count format match the rest of the BD UI.
/// </summary>
public partial class IngestionRunsWindow : Window
{
    private const int MaxRows = 50;

    private readonly IIngestionRunStore _store;

    public IngestionRunsWindow(IIngestionRunStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        InitializeComponent();
        RunsGrid.ItemsSource = Runs;
    }

    public ObservableCollection<IngestionRunRowView> Runs { get; } = new();

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadAsync().ConfigureAwait(true);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadAsync().ConfigureAwait(true);
    }

    private async System.Threading.Tasks.Task LoadAsync()
    {
        try
        {
            StatusLine.Text = "Loading…";
            var runs = await _store.ListRecentAsync(MaxRows, CancellationToken.None).ConfigureAwait(true);

            Runs.Clear();
            foreach (var r in runs)
            {
                Runs.Add(new IngestionRunRowView(r));
            }

            // Compact summary so the operator sees signal at a glance.
            var ok = runs.Count(r => r.EndedAtUtc.HasValue && r.Success);
            var failed = runs.Count(r => r.EndedAtUtc.HasValue && !r.Success);
            var running = runs.Count(r => !r.EndedAtUtc.HasValue);
            var totalNew = runs.Sum(r => r.InsertedCount);

            SummaryLine.Text = $"{runs.Count} runs — {ok} OK / {failed} failed / {running} running; {totalNew} new observations across this window.";
            StatusLine.Text = $"Loaded at {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            StatusLine.Text = $"Load failed: {ex.GetType().Name}: {ex.Message}";
        }
    }
}
