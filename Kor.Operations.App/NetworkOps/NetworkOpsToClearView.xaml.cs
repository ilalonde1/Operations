#nullable enable
using System;
using System.Collections.Generic;
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

    // Park (acknowledge): the third disposition, for a state that is known/expected (a drive removed on purpose) or won't
    // be chased now. On one machine it parks that one; on several the ellipsis signals a picker (choose which machines).
    public string AckLabel => Issue.Count == 1 ? "Acknowledge" : $"Acknowledge · {Issue.Count} machines…";

    // Set once a fix is queued on this issue: the button is replaced by a "running" note until the finding clears.
    public string? Fixing { get; set; }
    public bool ShowFix => CanFix && Fixing is null;
    public bool ShowFixing => Fixing is not null;
    public string FixingText => "⏳ " + Fixing;
    public string FixHint => Issue.Fix is not { } f
        ? "No one-click fix — Ask Claude, or hands-on."
        : !CanFix ? $"{f.Title} needs an input — open a machine to run it."
        : f.Disruptive ? $"{f.Title}: restarts the PC (warns first; asks if someone's on it)."
        : $"{f.Title}: one click, nothing restarts.";
}

/// <summary>One category on the overview strip: a kind of problem, how many machines have it, a dot in its worst colour.
/// The Ninja "Device health issues" rollup Ian asked for -- the whole worklist read in one glance before the detail below.</summary>
public sealed class ToClearCategoryRow
{
    public required ToClearCategory Category { get; init; }
    public string Name => Category.Name;
    public string CountText => Category.Machines.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public Brush Dot => NetworkOpsBrushes.For(Category.Severity);
    public string Tip => $"{Category.Issues} issue{(Category.Issues == 1 ? "" : "s")} on {Category.Machines} machine{(Category.Machines == 1 ? "" : "s")}: {string.Join(", ", Category.RuleKeys.Take(6))}";
}

/// <summary>The path to all-green: every live finding across the fleet, grouped by issue and ranked, with "Fix on all N"
/// for the ones a catalog fix clears, and Ask Claude for the rest. Parked (acknowledged/snoozed) findings do not appear.</summary>
public partial class NetworkOpsToClearView : UserControl
{
    private static readonly ILogger Log = Serilog.Log.ForContext<NetworkOpsToClearView>();
    private readonly NetworkOpsClient _client;
    private readonly ObservableCollection<ToClearRow> _rows = new();
    private readonly ObservableCollection<ToClearCategoryRow> _categories = new();   // the at-a-glance rollup above the list
    private readonly System.Collections.Generic.Dictionary<string, string> _fixing = new(StringComparer.OrdinalIgnoreCase);   // ruleKey -> running note, until the finding clears
    private System.Windows.Threading.DispatcherTimer? _auto;

    public NetworkOpsToClearView(NetworkOpsClient client)
    {
        _client = client;
        InitializeComponent();
        IssueList.ItemsSource = _rows;
        CategoryStrip.ItemsSource = _categories;
        Loaded += async (_, _) => { StartAuto(); await ReloadAsync(); };
        Unloaded += (_, _) => _auto?.Stop();
    }

    // A fix lands server-side, runs, then the service re-checks -- minutes later. Refresh on a timer so cleared issues
    // drop off on their own, but only while this tab is actually showing (it is a visibility-toggled panel, never unloaded).
    private void StartAuto()
    {
        if (_auto is not null) return;
        _auto = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _auto.Tick += async (_, _) => { if (IsVisible) await ReloadAsync(); };
        _auto.Start();
    }

    private async Task ReloadAsync()
    {
        try { Apply(await _client.GetToClearAsync(CancellationToken.None).ConfigureAwait(true)); }
        catch (Exception ex)
        {
            Log.Warning(ex, "To clear: load failed");
            Status("Could not read the fleet: " + ex.Message);
        }
    }

    /// <summary>Fill the category strip, the issue list and the headline from one worklist read. Internal so the render test
    /// can drive it without the service.</summary>
    internal void Apply(ToClearView v)
    {
        // A "fixing" mark lives only while its issue is still open: a cleared one drops off the list and stops fixing.
        var openKeys = new System.Collections.Generic.HashSet<string>(v.Open.Select(i => i.RuleKey), StringComparer.OrdinalIgnoreCase);
        foreach (var k in _fixing.Keys.Where(k => !openKeys.Contains(k)).ToList()) _fixing.Remove(k);
        _rows.Clear();
        var fixingCount = 0;
        foreach (var i in v.Open)
        {
            // What we just queued (specific note) wins; otherwise the SERVER tells us a fix is in flight -- so a fix
            // started last session, or from another machine, still shows as "fixing" here.
            var note = _fixing.TryGetValue(i.RuleKey, out var f) ? f
                : i.Running ? "running on the affected machines" : null;
            if (note is not null) fixingCount++;
            _rows.Add(new ToClearRow { Issue = i, Fixing = note });
        }
        // The at-a-glance rollup: the same open issues grouped into broad categories with a machine count (the Ninja view).
        _categories.Clear();
        foreach (var c in ToClearCategories.Of(v.Open)) _categories.Add(new ToClearCategoryRow { Category = c });
        var green = v.Issues == 0;
        var fixingNote = fixingCount > 0 ? $" · {fixingCount} fixing" : "";
        HeadlineText.Text = green ? "All green" : v.Issues.ToString();
        HeadlineDetail.Text = green
            ? (v.Parked > 0 ? $"Nothing live to clear. {v.Parked} parked (acknowledged or snoozed)." : "Nothing to clear.")
            : $"issue{(v.Issues == 1 ? "" : "s")} to clear · {v.OneClickIssues} one-click · {v.Parked} parked{fixingNote}";
        HeadlineAccent.Fill = green ? NetworkOpsBrushes.Healthy
            : v.Open[0].Severity == Severity.Critical ? NetworkOpsBrushes.Critical
            : NetworkOpsBrushes.Attention;
        Status($"Updated {DateTime.Now:HH:mm:ss} · {(green ? "all green 🎉" : $"{v.Issues} to clear{fixingNote}")} · refreshes every 20s");
    }

    private async void Fix_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ToClearRow row || row.Issue.Fix is not { } fix) return;
        if (sender is Button btn) btn.IsEnabled = false;   // instant feedback; stops a double-fire while it queues
        var ids = row.Issue.Machines.Select(m => m.DeviceId).Where(x => x > 0).Distinct().ToList();
        if (ids.Count == 0) return;
        try
        {
            Status($"Queuing {fix.Title} on {ids.Count}…");
            var outcomes = await _client.RunFixManyAsync(fix.Id, ids, fix.PrefilledParam, confirmed: false, CancellationToken.None).ConfigureAwait(true);
            var need = outcomes.Where(o => o.NeedsConfirmation).Select(o => o.Name).ToList();
            if (need.Count > 0 &&
                MessageBox.Show($"{fix.Title} will interrupt {need.Count} machine(s): {string.Join(", ", need)}. Go ahead?",
                    "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                outcomes = await _client.RunFixManyAsync(fix.Id, ids, fix.PrefilledParam, confirmed: true, CancellationToken.None).ConfigureAwait(true);
            var queued = outcomes.Count(o => o.ActionId is not null);
            var refused = outcomes.Count(o => o.Refused is not null);
            Log.Information("To clear: {Fix} on {Count} -> queued {Queued}, refused {Refused}", fix.Id, ids.Count, queued, refused);
            if (queued > 0)
            {
                // Mark the issue "running" until it clears (the service re-checks after each fix). Disruptive fixes restart.
                _fixing[row.Issue.RuleKey] = $"queued on {queued} — running ({(fix.Disruptive ? "restart pending" : "a few min")})";
                Status($"Queued {fix.Title} on {queued} machine(s){(refused > 0 ? $"; {refused} held (someone's using them — open the PC to confirm)" : "")}. Running now — the list clears each as it's fixed and re-checked (auto-refreshing).");
            }
            else
                Status($"{fix.Title}: nothing queued ({refused} held/refused). Open a machine to confirm, or Ask Claude.");
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "To clear: fix-on-all failed for {Fix}", fix.Id);
            Status("Could not run the fix: " + ex.Message);
        }
    }

    // Park the issue by acknowledging it on the machines you choose. It becomes quiet (acknowledged) -> the fleet stops
    // flagging it and it drops off this list on the reload. Use it when the state is known/expected or won't be chased now.
    private async void Acknowledge_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ToClearRow row) return;
        if (!TryPickMachines(row, "Acknowledge", out var ids)) return;
        try
        {
            Status($"Acknowledging {row.Title} on {ids.Count}…");
            var r = await _client.AcknowledgeManyAsync(ids, null, CancellationToken.None).ConfigureAwait(true);
            Log.Information("To clear: acknowledged {Rule} on {Count} -> {Ok} parked", row.Issue.RuleKey, ids.Count, r.Ok);
            Status($"Acknowledged {row.Title} on {r.Ok} machine(s). It stops counting against green.");
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { Log.Warning(ex, "To clear: acknowledge failed for {Rule}", row.Issue.RuleKey); Status("Could not acknowledge: " + ex.Message); }
    }

    // Hide the issue for a week on the machines you choose; it comes back if still open then.
    private async void Snooze_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ToClearRow row) return;
        if (!TryPickMachines(row, "Snooze", out var ids)) return;
        try
        {
            Status($"Snoozing {row.Title} on {ids.Count}…");
            var r = await _client.SnoozeManyAsync(ids, DateTime.UtcNow.AddDays(7), null, CancellationToken.None).ConfigureAwait(true);
            Log.Information("To clear: snoozed {Rule} on {Count} -> {Ok} for 7d", row.Issue.RuleKey, ids.Count, r.Ok);
            Status($"Snoozed {row.Title} on {r.Ok} machine(s) for a week.");
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { Log.Warning(ex, "To clear: snooze failed for {Rule}", row.Issue.RuleKey); Status("Could not snooze: " + ex.Message); }
    }

    // Which machines a disposition applies to. One machine: that one (a Critical asks to confirm first). Several: a picker,
    // so you can clear the PC you've handled (208's removed drive) and leave the one that still needs attention (206's).
    // False = cancelled, or nothing chosen.
    private bool TryPickMachines(ToClearRow row, string verb, out List<long> ids)
    {
        ids = new List<long>();
        var machines = row.Issue.Machines.Where(m => m.FindingId > 0).ToList();
        if (machines.Count == 0) return false;
        if (machines.Count == 1)
        {
            if (verb == "Acknowledge" && row.Issue.Severity == Severity.Critical &&
                MessageBox.Show($"Acknowledge “{row.Title}” on {machines[0].Name}? It stops counting against green and drops off this list (reopen it from the machine's page).",
                    "Acknowledge", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                return false;
            ids = machines.Select(m => m.FindingId).ToList();
            return true;
        }
        var picker = new NetworkOpsMachinePickerWindow(verb, row.Title, machines.Select(m => m.Name).ToList()) { Owner = Window.GetWindow(this) };
        if (picker.ShowDialog() != true || picker.SelectedIndexes.Count == 0) return false;
        ids = picker.SelectedIndexes.Select(i => machines[i].FindingId).ToList();
        return true;
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
