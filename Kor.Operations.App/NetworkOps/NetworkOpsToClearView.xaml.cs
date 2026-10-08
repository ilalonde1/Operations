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
    /// <summary>Plain-English "what this means", from the issue's knowledge entry (empty when none) -- shown inline so the
    /// worklist explains an issue without a trip to the PC's page.</summary>
    public string Meaning => Issue.Meaning ?? "";
    public bool ShowMeaning => !string.IsNullOrEmpty(Issue.Meaning);

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
    // No one-click fix and nothing running: Ask Claude is the real path, so it takes the primary slot rather than hiding
    // behind "More". (A fixable issue keeps Fix primary and tucks Ask/Acknowledge/Snooze into the menu.)
    public bool ShowAskPrimary => !CanFix && Fixing is null;
    public string FixingText => "⏳ " + Fixing;
    // The hint below the evidence. For a one-click fix the button already names it, so the hint only adds what the button
    // can't: whether it restarts. For no fix / an input-needed fix it says why there's no button.
    public string FixHint => Issue.Fix is not { } f
        ? "No one-click fix — Ask Claude, or handle it hands-on."
        : !CanFix ? $"{f.Title} needs an input — open a machine to run it."
        : f.Disruptive ? "Restarts the machine — warns first, and asks if someone's on it."
        : "One click, nothing restarts.";
}

/// <summary>One category on the overview strip: a kind of problem, how many machines have it, a dot in its worst colour.
/// The Ninja "Device health issues" rollup Ian asked for -- the whole worklist read in one glance before the detail below.</summary>
public sealed class ToClearCategoryRow : System.ComponentModel.INotifyPropertyChanged
{
    public required ToClearCategory Category { get; init; }
    public string Name => Category.Name;
    public string CountText => Category.Machines.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public Brush Dot => NetworkOpsBrushes.For(Category.Severity);
    public string Tip => $"{Category.Issues} issue{(Category.Issues == 1 ? "" : "s")} on {Category.Machines} machine{(Category.Machines == 1 ? "" : "s")} — click to show only these: {string.Join(", ", Category.RuleKeys.Take(6))}";

    private bool _isSelected;
    /// <summary>The chip the worklist is filtered to: highlighted, so which filter is live is never a guess.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); }
    }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>One "clear in one action" row: a single fix (restart, free disk…) that clears SEVERAL reasons at once, over the
/// de-duplicated union of machines. It answers the question the per-issue counts hide -- "13 + 9 + 6 reboots, but how many
/// machines overlap?" -- by showing the union, the findings cleared, and how many machines carry 2+ of the reasons.</summary>
public sealed class ClearActionRow
{
    public required ClearAction Action { get; init; }
    // The reasons folded into this action, as full rows for the disclosure. Each reuses ToClearRow, so the per-reason
    // Fix / Ask / Acknowledge / Snooze handlers resolve them from DataContext unchanged. Built in Apply so each carries
    // its "fixing" note -- this is where a reboot reason is now acted on, since it no longer has a card in the list below.
    public required IReadOnlyList<ToClearRow> ReasonRows { get; init; }

    public string Title => Action.Fix.Title;
    public Brush SeverityBrush => NetworkOpsBrushes.For(Action.Severity);
    // The headline: the de-duplicated machine count and the findings it clears -- NOT the sum of the per-reason counts.
    public string Summary => $"{Action.Machines} machine{(Action.Machines == 1 ? "" : "s")} · clears {Action.Findings} finding{(Action.Findings == 1 ? "" : "s")}";
    // The reasons that fold into this one action, worst first, each with its own count: "not-restarted (13) · reboot-overdue (9)…".
    public string ReasonsSummary => string.Join("   ·   ", Action.Reasons.Select(r => $"{r.Title} ({r.Count})"));
    // The saving, stated plainly: the machines carried by 2+ of the reasons, cleared by this one run.
    public bool ShowOverlap => Action.OverlapMachines > 0;
    public string Overlap => Action.OverlapMachines == 0 ? ""
        : $"{Action.OverlapMachines} of the {Action.Machines} carry 2 or more of these — one {Verb} clears every reason on each.";
    // Runnable on all at once only when the fix needs no per-machine input. A restart is runnable (it warns/confirms first);
    // an input-needed fix (start a named service) is not, so it shows the breakdown without a run-all button.
    public bool CanRun => Action.Fix.ParamLabel is null;
    public string RunLabel => $"{Action.Fix.Title} · all {Action.Machines}";
    public string ExpandLabel => $"Show {Action.Reasons.Count} reasons ▾";
    public string CollapseLabel => "Hide reasons ▴";
    // Every machine this action would touch, counted once (the union), as device ids for the run.
    public IReadOnlyList<int> UnionDeviceIds =>
        Action.Reasons.SelectMany(i => i.Machines.Select(m => m.DeviceId)).Where(x => x > 0).Distinct().ToList();
    private string Verb => Action.Fix.Id == "restart-pc" ? "restart" : "run";
}

/// <summary>The path to all-green: every live finding across the fleet, grouped by issue and ranked, with "Fix on all N"
/// for the ones a catalog fix clears, and Ask Claude for the rest. Parked (acknowledged/snoozed) findings do not appear.</summary>
public partial class NetworkOpsToClearView : UserControl
{
    private static readonly ILogger Log = Serilog.Log.ForContext<NetworkOpsToClearView>();
    private readonly NetworkOpsClient _client;
    private readonly ObservableCollection<ToClearRow> _rows = new();
    private readonly ObservableCollection<ToClearCategoryRow> _categories = new();   // the at-a-glance rollup above the list
    private readonly ObservableCollection<ClearActionRow> _actions = new();          // "clear in one action": issues grouped by the fix that clears them, machines de-duplicated
    private readonly System.Collections.Generic.Dictionary<string, string> _fixing = new(StringComparer.OrdinalIgnoreCase);   // ruleKey -> running note, until the finding clears
    private System.Windows.Threading.DispatcherTimer? _auto;
    private string? _selectedCategory;   // the category chip the worklist is filtered to (null = show all)

    public NetworkOpsToClearView(NetworkOpsClient client)
    {
        _client = client;
        InitializeComponent();
        IssueList.ItemsSource = _rows;
        CategoryStrip.ItemsSource = _categories;
        ActionStrip.ItemsSource = _actions;
        // Clicking a category chip filters the worklist to its rules (the Core comment's intent). The filter lives on the
        // list's own default view so it survives the 20s auto-refresh, which clears and refills the rows underneath it.
        System.Windows.Data.CollectionViewSource.GetDefaultView(_rows).Filter = o =>
            _selectedCategory is null || (o is ToClearRow r && ToClearCategories.CategoryOf(r.Issue.RuleKey) == _selectedCategory);
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

        // What we just queued (specific note) wins; otherwise the SERVER tells us a fix is in flight -- so a fix started
        // last session, or from another machine, still shows as "fixing", whether the issue sits in the panel or the list.
        string? NoteFor(ToClearIssue i) => _fixing.TryGetValue(i.RuleKey, out var f) ? f : i.Running ? "running on the affected machines" : null;
        var fixingCount = v.Open.Count(i => NoteFor(i) is not null);

        // "Clear in one action": the open issues regrouped by the fix that clears them, machines de-duplicated across the
        // reasons, kept only where it CONSOLIDATES (2+ reasons). Those reasons move OUT of the per-issue list and live,
        // expandable, on the action row -- so one restart clearing three reboot reasons is read and acted on once.
        var actions = ClearByAction.Of(v.Open).Where(a => a.Consolidates).ToList();
        var covered = new System.Collections.Generic.HashSet<string>(
            actions.SelectMany(a => a.Reasons.Select(r => r.RuleKey)), StringComparer.OrdinalIgnoreCase);
        _actions.Clear();
        foreach (var a in actions)
            _actions.Add(new ClearActionRow { Action = a, ReasonRows = a.Reasons.Select(i => new ToClearRow { Issue = i, Fixing = NoteFor(i) }).ToList() });
        ActionSection.Visibility = _actions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // The per-issue list is now only what no consolidating action covers: critical one-offs, single-reason fixes, and
        // the Ask-Claude issues. Everything reboot-shaped is up in the panel.
        var remainder = v.Open.Where(i => !covered.Contains(i.RuleKey)).ToList();
        _rows.Clear();
        foreach (var i in remainder) _rows.Add(new ToClearRow { Issue = i, Fixing = NoteFor(i) });

        // The chips roll up (and filter) the LIST -- the remainder below the panel -- so the chips, the list and the filter
        // all speak about the same set, and a chip can never filter the list to empty.
        _categories.Clear();
        foreach (var c in ToClearCategories.Of(remainder)) _categories.Add(new ToClearCategoryRow { Category = c });
        // Keep the active chip filter across the refresh; drop it if that category has cleared off the list.
        if (_selectedCategory is not null && _categories.All(c => !string.Equals(c.Name, _selectedCategory, StringComparison.Ordinal)))
            _selectedCategory = null;
        ApplyCategoryFilter();

        var green = v.Issues == 0;
        var fixingNote = fixingCount > 0 ? $" · {fixingCount} fixing" : "";
        HeadlineText.Text = green ? "All green" : v.Issues.ToString();
        HeadlineDetail.Text = green
            ? (v.Parked > 0 ? $"Nothing live to clear. {v.Parked} parked (acknowledged or snoozed)." : "Nothing to clear.")
            : $"issue{(v.Issues == 1 ? "" : "s")} to clear · {v.OneClickIssues} one-click · {v.Parked} parked{fixingNote}";
        HeadlineAccent.Fill = green ? NetworkOpsBrushes.Healthy
            : v.Open[0].Severity == Severity.Critical ? NetworkOpsBrushes.Critical
            : NetworkOpsBrushes.Attention;

        // The list can be empty while the panel is full (everything consolidated); only say "all green" when truly nothing
        // is open, and otherwise point to the panel rather than leaving a bare "nothing to clear" over a list of actions.
        if (green) { EmptyText.Text = "All green — nothing to clear. 🎉"; EmptyText.Visibility = Visibility.Visible; }
        else if (_rows.Count == 0) { EmptyText.Text = "Everything to clear is up in “Clear in one action”."; EmptyText.Visibility = Visibility.Visible; }
        else EmptyText.Visibility = Visibility.Collapsed;

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

    // "Clear in one action": run the one fix over the de-duplicated union of machines, clearing every reason it covers on
    // each. Same queue + confirm path as a single issue's Fix, but across the union rather than one reason's machines.
    private async void RunAction_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ClearActionRow row) return;
        var fix = row.Action.Fix;
        var ids = row.UnionDeviceIds;
        if (ids.Count == 0) return;
        if (sender is Button btn) btn.IsEnabled = false;   // instant feedback; stops a double-fire while it queues
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
            Log.Information("To clear: action {Fix} over union of {Count} -> queued {Queued}, refused {Refused}", fix.Id, ids.Count, queued, refused);
            if (queued > 0)
            {
                // Mark every reason this action covers as running, so each of its cards shows "fixing" until it clears.
                foreach (var reason in row.Action.Reasons)
                    _fixing[reason.RuleKey] = $"queued on {queued} — running ({(fix.Disruptive ? "restart pending" : "a few min")})";
                Status($"Queued {fix.Title} on {queued} machine(s){(refused > 0 ? $"; {refused} held (someone's using them — open the PC to confirm)" : "")}. One run clears every reason it covers on each (auto-refreshing).");
            }
            else
                Status($"{fix.Title}: nothing queued ({refused} held/refused). Open a machine to confirm, or Ask Claude.");
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "To clear: clear-by-action failed for {Fix}", fix.Id);
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

    // The per-issue overflow: Ask Claude / Acknowledge / Snooze tuck behind one "More" so each card shows one clear verb.
    // The menu's items reuse the handlers below unchanged -- they resolve the row from their own DataContext, which we set
    // to the button's (the bound ToClearRow) as the menu opens, because a ContextMenu is outside the card's visual tree.
    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.ContextMenu is { } menu)
        {
            menu.DataContext = b.DataContext;
            menu.PlacementTarget = b;
            menu.IsOpen = true;
        }
    }

    private void Ask_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ToClearRow row) return;
        var first = row.Issue.Machines.FirstOrDefault(m => m.DeviceId > 0);
        var request = first is null ? null : new PromptRequest("finding", null, first.DeviceId, first.FindingId);
        new PromptLibraryWindow(_client, request) { Owner = Window.GetWindow(this) }.Show();
    }

    // A category chip toggles the worklist filter: click to show only that kind of problem, click the live one again for all.
    private void Chip_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ToClearCategoryRow cat) return;
        _selectedCategory = string.Equals(_selectedCategory, cat.Name, StringComparison.Ordinal) ? null : cat.Name;
        ApplyCategoryFilter();
    }

    private void ClearFilter_Click(object sender, RoutedEventArgs e)
    {
        _selectedCategory = null;
        ApplyCategoryFilter();
    }

    // Reflect the active filter everywhere at once: the highlighted chip, the rows shown, and the "showing X — show all" line.
    private void ApplyCategoryFilter()
    {
        foreach (var c in _categories) c.IsSelected = _selectedCategory is not null && string.Equals(c.Name, _selectedCategory, StringComparison.Ordinal);
        System.Windows.Data.CollectionViewSource.GetDefaultView(_rows).Refresh();
        var filtered = _selectedCategory is not null;
        HelpCaption.Visibility = filtered ? Visibility.Collapsed : Visibility.Visible;
        ClearFilterBtn.Visibility = filtered ? Visibility.Visible : Visibility.Collapsed;
        if (filtered) ClearFilterBtn.Content = $"Showing: {_selectedCategory} — show all";
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
