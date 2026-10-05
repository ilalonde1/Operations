#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Learning;
using Serilog;

namespace Kor.Operations.App.NetworkOps;

/// <summary>One issue on the "To clear" worklist, shaped for the list: the fix label, where it is open, and the one-line
/// hint for how it clears.</summary>
public sealed class ToClearRow
{
    public required ToClearIssue Issue { get; init; }

    public string SeverityText => Issue.Severity.ToString();
    public Brush SeverityBrush => NetworkOpsBrushes.For(Issue.Severity);
    public string Title => Issue.Title;
    public string Evidence => Issue.Evidence;

    public string Where
    {
        get
        {
            var names = Issue.Machines.Select(m => m.Name).ToList();
            if (names.Count == 1) return "on " + names[0];
            var shown = string.Join(", ", names.Take(5));
            return names.Count <= 5 ? $"on {names.Count} machines: {shown}" : $"on {names.Count} machines: {shown} +{names.Count - 5} more";
        }
    }

    // Offer the fix only when it can run without asking for an input (the ruleKey carries the param when it needs one).
    public bool CanFix => Issue.Fix is { } f && (f.ParamLabel is null || !string.IsNullOrEmpty(f.PrefilledParam));
    public string FixLabel => Issue.Fix is not { } f ? "" : Issue.Count == 1 ? f.Title : $"{f.Title} · all {Issue.Count}";
    public string FixHint => Issue.Fix is not { } f
        ? "No one-click fix — Ask Claude, or hands-on."
        : !CanFix ? $"{f.Title} needs an input — open a machine to run it."
        : f.Disruptive ? $"{f.Title}: restarts the PC (warns first; asks if someone's on it)."
        : $"{f.Title}: one click, nothing restarts.";
}

/// <summary>The path to all-green: every live finding across the fleet, grouped by issue and ranked, with "Fix on all N"
/// for the ones a catalog fix clears, and Ask Claude for the rest. Parked (acknowledged/snoozed) findings do not appear.</summary>
public partial class NetworkOpsToClearView : UserControl
{
    private static readonly ILogger Log = Serilog.Log.ForContext<NetworkOpsToClearView>();
    private readonly NetworkOpsClient _client;
    private readonly ObservableCollection<ToClearRow> _rows = new();

    public NetworkOpsToClearView(NetworkOpsClient client)
    {
        _client = client;
        InitializeComponent();
        IssueList.ItemsSource = _rows;
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            Status("Reading the fleet…");
            var v = await _client.GetToClearAsync(CancellationToken.None).ConfigureAwait(true);
            _rows.Clear();
            foreach (var i in v.Open) _rows.Add(new ToClearRow { Issue = i });
            var green = v.Issues == 0;
            HeadlineText.Text = green ? "All green" : v.Issues.ToString();
            HeadlineDetail.Text = green
                ? (v.Parked > 0 ? $"Nothing live to clear. {v.Parked} parked (acknowledged or snoozed)." : "Nothing to clear.")
                : $"issue{(v.Issues == 1 ? "" : "s")} to clear · {v.OneClickIssues} one-click · {v.Parked} parked";
            HeadlineAccent.Fill = green ? NetworkOpsBrushes.Healthy
                : v.Open[0].Severity == Severity.Critical ? NetworkOpsBrushes.Critical
                : NetworkOpsBrushes.Attention;
            Status(green ? "All green 🎉" : $"{v.Issues} to clear across the fleet");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "To clear: load failed");
            Status("Could not read the fleet: " + ex.Message);
        }
    }

    private async void Fix_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ToClearRow row || row.Issue.Fix is not { } fix) return;
        var ids = row.Issue.Machines.Select(m => m.DeviceId).Where(x => x > 0).Distinct().ToList();
        if (ids.Count == 0) return;
        try
        {
            Status($"Queuing {fix.Title} on {ids.Count}…");
            var outcomes = await _client.RunFixManyAsync(fix.Id, ids, fix.PrefilledParam, confirmed: false, CancellationToken.None).ConfigureAwait(true);
            var need = outcomes.Where(o => o.NeedsConfirmation).Select(o => o.Name).ToList();
            if (need.Count > 0 &&
                MessageBox.Show($"{fix.Title} restarts the PC. Someone is using: {string.Join(", ", need)}. Go ahead anyway?",
                    "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                outcomes = await _client.RunFixManyAsync(fix.Id, ids, fix.PrefilledParam, confirmed: true, CancellationToken.None).ConfigureAwait(true);
            var queued = outcomes.Count(o => o.ActionId is not null);
            var refused = outcomes.Count(o => o.Refused is not null);
            Log.Information("To clear: {Fix} on {Count} -> queued {Queued}, refused {Refused}", fix.Id, ids.Count, queued, refused);
            Status($"{fix.Title}: queued on {queued}{(refused > 0 ? $", {refused} refused/held" : "")}. A re-check shows whether it cleared.");
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "To clear: fix-on-all failed for {Fix}", fix.Id);
            Status("Could not run the fix: " + ex.Message);
        }
    }

    private void Ask_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ToClearRow row) return;
        var first = row.Issue.Machines.FirstOrDefault(m => m.DeviceId > 0);
        var request = first is null ? null : new PromptRequest("finding", null, first.DeviceId, first.FindingId);
        new PromptLibraryWindow(_client, request) { Owner = Window.GetWindow(this) }.Show();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async void Recheck_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Status("Re-checking every PC… (a few minutes)");
            await _client.QueueFleetSweepAsync(CancellationToken.None).ConfigureAwait(true);
            Status("Re-check queued. Refresh in a few minutes to see the updated list.");
        }
        catch (Exception ex) { Status("Could not start the re-check: " + ex.Message); }
    }

    private void Status(string s) => StatusText.Text = s;
}
