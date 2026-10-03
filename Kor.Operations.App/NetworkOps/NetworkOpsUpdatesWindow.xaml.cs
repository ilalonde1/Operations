#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Learning;

namespace Kor.Operations.App.NetworkOps;

/// <summary>One machine in the Updates view: what the service sent, plus whether it is ticked and how its install is going.</summary>
public sealed class UpdateRowView(UpdateRow row, DateTime nowUtc) : INotifyPropertyChanged
{
    private bool _isTicked;
    private string? _progress;

    public UpdateRow Row { get; } = row;
    public string Name => Row.Name;
    public bool CanTick => Row.Target;
    public bool IsTicked { get => _isTicked; set { if (_isTicked != value && (CanTick || !value)) { _isTicked = value; Changed(); } } }

    public string KindText => Row.IsServer
        ? "Server" + (Row.Guard == "Alone" ? " · patch on its own" : Row.Guard == "NoRestart" ? " · never restarted from here" : "")
        : "PC";

    public int Security => Row.Pending.Count(p => p.Security);
    public bool HasUpdates => Row.Pending.Count > 0;
    public bool IsDue => Row.Due is Severity.Warning or Severity.Critical;

    public string DueText => !Row.Target ? "Can't reach" : Row.Due switch
    {
        Severity.Critical => "Overdue",
        Severity.Warning => "Due",
        Severity.Info when Security > 0 => "New (held)",
        Severity.Info => "Optional",
        _ => Row.ScanStatus is null ? "Not searched" : Row.ScanStatus == "Ok" ? "Up to date" : "Search failed",
    };

    public Brush DueBrush => Row.Due switch
    {
        Severity.Critical => Critical,
        Severity.Warning => Warning,
        _ => Muted,
    };

    public string WaitingText => !HasUpdates ? (Row.RebootPending ? "restart pending" : "–")
        : $"{Security} security, {Row.Pending.Count - Security} other" + (Row.RebootPending ? " · restart pending" : "");

    public string WhoText => Row.IsServer ? "–" : Row.Presence ?? "not known";

    public string SearchedText => Row.ScanStatus is null ? "never"
        : Row.ScanStatus == "Ok" ? CommandCenterView.Ago(Row.ScannedUtc, nowUtc)
        : $"{CommandCenterView.Ago(Row.ScannedUtc, nowUtc)}: {Short(Row.ScanStatus, 60)}";

    /// <summary>This window's install progress when there is one; otherwise the last install on record.</summary>
    public string InstallText => _progress ?? (!Row.Target ? Row.Why ?? "" : Row.LastInstall is { } l ? $"last: {Short(l, 140)} ({CommandCenterView.Ago(Row.LastInstallUtc, nowUtc)})" : "");

    public string? Progress { get => _progress; set { _progress = value; Changed(nameof(InstallText)); } }

    private static readonly Brush Critical = Frozen(0xC1, 0x1E, 0x1E), Warning = Frozen(0xB4, 0x6A, 0x00), Muted = Frozen(0x6B, 0x72, 0x80);

    private static Brush Frozen(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }

    private static string Short(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record PendingLine(string Title, string Line);

public partial class NetworkOpsUpdatesWindow : Window
{
    private readonly NetworkOpsClient _client;
    private readonly CancellationTokenSource _cts = new();
    private readonly DispatcherTimer _follow = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly Dictionary<long, UpdateRowView> _running = new();
    private List<UpdateRowView> _all = [];

    public NetworkOpsUpdatesWindow(NetworkOpsClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        InitializeComponent();
        _follow.Tick += async (_, _) => await Guard(FollowAsync).ConfigureAwait(true);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await Guard(LoadAsync).ConfigureAwait(true);

    private async Task LoadAsync(CancellationToken ct)
    {
        Status("Reading…");
        Apply(await _client.GetUpdatesAsync(ct).ConfigureAwait(true), DateTime.UtcNow);
        Status("");
    }

    /// <summary>Fills the window (public so a render test can fill it without a service). Ticks and progress survive a refresh.</summary>
    public void Apply(IReadOnlyList<UpdateRow> rows, DateTime nowUtc)
    {
        var ticked = _all.Where(r => r.IsTicked).Select(r => r.Row.DeviceId).ToHashSet();
        var progress = _all.Where(r => r.Progress is not null).ToDictionary(r => r.Row.DeviceId, r => r.Progress);
        _all = rows.Select(r => new UpdateRowView(r, nowUtc) { IsTicked = ticked.Contains(r.DeviceId), Progress = progress.GetValueOrDefault(r.DeviceId) })
            .OrderByDescending(r => r.Row.Due ?? (Severity)(-1)).ThenByDescending(r => r.Security).ThenBy(r => r.Row.IsServer).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var (id, view) in _running.ToList())
            if (_all.FirstOrDefault(r => r.Row.DeviceId == view.Row.DeviceId) is { } fresh) _running[id] = fresh;
        // The buttons count what is ticked, however it was ticked (the checkbox, "Tick everything due", a refresh).
        foreach (var r in _all) r.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(UpdateRowView.IsTicked)) UpdateButtons(); };

        var due = _all.Where(r => r.IsDue).ToList();
        var overdue = due.Count(r => r.Row.Due == Severity.Critical);
        HeadlineText.Text = due.Count == 0
            ? "Nothing is due" + (_all.Any(r => r.HasUpdates) ? ": some machines have optional or newly-released updates" : "")
            : $"Security updates are due on {due.Count} machine{(due.Count == 1 ? "" : "s")}" + (overdue > 0 ? $" ({overdue} overdue)" : "");
        var searched = _all.Where(r => r.Row.ScanStatus == "Ok").Select(r => r.Row.ScannedUtc).Max();
        var notSearched = _all.Count(r => r.CanTick && r.Row.ScanStatus != "Ok");
        SublineText.Text = $"{_all.Count(r => r.CanTick && r.Row.ScanStatus == "Ok")} of {_all.Count} machines searched, last {CommandCenterView.Ago(searched, nowUtc)}" +
                           (notSearched > 0 ? $" · {notSearched} could not be searched (off, or not reachable)" : "") +
                           " · nothing installs by itself: tick the machines and install.";
        ApplyFilter();
        UpdateButtons();
    }

    private void ApplyFilter()
    {
        var selected = (Grid.SelectedItem as UpdateRowView)?.Row.DeviceId;
        Grid.ItemsSource = OnlyWaitingBox.IsChecked == true ? _all.Where(r => r.HasUpdates || r.IsTicked || r.Progress is not null || !r.CanTick || (r.Row.ScanStatus is { } st && st != "Ok")).ToList() : _all;
        if (selected is { } s) Grid.SelectedItem = _all.FirstOrDefault(r => r.Row.DeviceId == s);
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) { if (Grid is not null) ApplyFilter(); }

    private void Tick_Click(object sender, RoutedEventArgs e) => UpdateButtons();

    private void TickDue_Click(object sender, RoutedEventArgs e)
    {
        // The domain controller is left out: it goes in a batch of its own.
        foreach (var r in _all) r.IsTicked = r.CanTick && r.IsDue && r.Row.Guard != "Alone";
        UpdateButtons();
    }

    private void UntickAll_Click(object sender, RoutedEventArgs e) { foreach (var r in _all) r.IsTicked = false; UpdateButtons(); }

    private void UpdateButtons()
    {
        var n = _all.Count(r => r.IsTicked);
        InstallBtn.Content = $"Install on {n} ticked";
        InstallRestartBtn.Content = $"Install on {n} and restart if needed";
        InstallBtn.IsEnabled = InstallRestartBtn.IsEnabled = n > 0;
    }

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Grid.SelectedItem is not UpdateRowView r) return;
        DetailTitle.Text = r.Name;
        DetailLine.Text = !r.CanTick ? r.Row.Why ?? ""
            : r.Row.ScanStatus is null ? "Not searched yet."
            : r.Row.ScanStatus != "Ok" ? $"The last search failed: {r.Row.ScanStatus}"
            : r.HasUpdates ? $"{r.Row.Pending.Count} waiting{(r.Row.RebootPending ? "; a restart is already pending" : "")}." + (r.Row.DueTitle is { } t ? $" {t}." : "")
            : r.Row.RebootPending ? "Up to date, but waiting on a restart to finish what was installed." : "Up to date.";
        DetailList.ItemsSource = r.Row.Pending.OrderByDescending(p => p.Security).ThenBy(p => p.Released)
            .Select(p => new PendingLine(p.Title,
                string.Join(" · ", new[] { p.Security ? "security" : "other", p.Severity, p.Released is { } d ? $"released {d:yyyy-MM-dd}" : null, p.NeedsReboot ? "needs a restart" : null }
                    .Where(x => !string.IsNullOrEmpty(x)))))
            .ToList();
    }

    private async void Install_Click(object sender, RoutedEventArgs e) => await Guard(ct => InstallAsync("none", ct)).ConfigureAwait(true);

    private async void InstallRestart_Click(object sender, RoutedEventArgs e) => await Guard(ct => InstallAsync("if-needed", ct)).ConfigureAwait(true);

    private async Task InstallAsync(string restart, CancellationToken ct)
    {
        var ticked = _all.Where(r => r.IsTicked).ToList();
        if (ticked.Count == 0) return;
        var what = restart == "none" ? "Install updates now (no restart)" : "Install updates now, and restart where an update needs it (5-minute warning on screen)";
        if (MessageBox.Show(this, $"{what} on {ticked.Count} machine{(ticked.Count == 1 ? "" : "s")}?\n\n{string.Join(", ", ticked.Select(t => t.Name))}",
                "Install updates", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;

        Status($"Queuing {ticked.Count}…");
        var outcomes = (await _client.InstallUpdatesAsync(new UpdateInstallRequest(ticked.Select(t => t.Row.DeviceId).ToList(), restart, false), ct).ConfigureAwait(true)).ToList();
        var ask = outcomes.Where(o => o.NeedsConfirmation).ToList();
        if (ask.Count > 0 && MessageBox.Show(this,
                $"Someone is using {(ask.Count == 1 ? "this machine" : "these machines")} right now:\n\n{string.Join("\n", ask.Select(a => $"{a.Name}: {a.Refused}"))}\n\n" +
                "Install and restart them anyway if an update needs it? They get a 5-minute warning on screen.",
                "Someone is using it", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes)
        {
            var confirmed = await _client.InstallUpdatesAsync(new UpdateInstallRequest(ask.Select(a => a.DeviceId).ToList(), restart, true), ct).ConfigureAwait(true);
            outcomes = outcomes.Where(o => !o.NeedsConfirmation).Concat(confirmed).ToList();
        }

        foreach (var o in outcomes)
        {
            var view = _all.FirstOrDefault(r => r.Row.DeviceId == o.DeviceId);
            if (view is null) continue;
            view.IsTicked = false;
            if (o.ActionId is { } id) { _running[id] = view; view.Progress = "queued" + (o.Note is { } n ? $" ({n})" : ""); }
            else view.Progress = $"not installed: {o.Refused}";
        }
        var queued = outcomes.Count(o => o.ActionId is not null);
        Status($"{queued} of {outcomes.Count} queued; they run a few at a time and each is searched again when it finishes." +
               (outcomes.Any(o => o.ActionId is null) ? " The others say why on their row." : ""));
        UpdateButtons();
        ApplyFilter();
        if (_running.Count > 0) _follow.Start();
    }

    /// <summary>Follows the queued installs until each has finished, then re-reads the list (each machine is searched again after it).</summary>
    private async Task FollowAsync(CancellationToken ct)
    {
        foreach (var (id, view) in _running.ToList())
        {
            if (await _client.GetActionAsync(id, ct).ConfigureAwait(true) is not { } a) continue;
            view.Progress = a.Status switch
            {
                "Requested" => "queued",
                "Running" => "installing… (this can take 10–40 minutes)",
                "Done" => $"done: {a.Detail}",
                _ => $"{a.Status}: {a.Detail}",
            };
            if (a.Status is "Done" or "Failed" or "Refused") _running.Remove(id);
        }
        if (_running.Count == 0)
        {
            _follow.Stop();
            Apply(await _client.GetUpdatesAsync(ct).ConfigureAwait(true), DateTime.UtcNow);
            Status("Every install has finished; the list shows what each machine has left.");
        }
    }

    private async void Scan_Click(object sender, RoutedEventArgs e) => await Guard(async ct =>
    {
        ScanBtn.IsEnabled = false;
        try
        {
            var trigger = await _client.QueueUpdateScanAsync(ct).ConfigureAwait(true);
            Status("Searching every machine… (a few minutes)");
            for (var i = 0; i < 120; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(true);
                if (await _client.GetTriggerAsync(trigger, ct).ConfigureAwait(true) is { CompletedUtc: not null } t)
                {
                    Apply(await _client.GetUpdatesAsync(ct).ConfigureAwait(true), DateTime.UtcNow);
                    Status($"Search finished: {t.Result}");
                    return;
                }
            }
            Status("The search is still running; Refresh in a few minutes.");
        }
        finally { ScanBtn.IsEnabled = true; }
    }).ConfigureAwait(true);

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await Guard(LoadAsync).ConfigureAwait(true);

    private void Status(string text) => StatusText.Text = text;

    // Every handler is async void: nothing may escape it but a cancel (which only ever means the window closed). A
    // narrower filter let an unexpected exception (e.g. a JsonException from a changed DTO) escape and crash the app.
    private async Task Guard(Func<CancellationToken, Task> work)
    {
        try { await work(_cts.Token).ConfigureAwait(true); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Status($"Could not do that: {ex.Message}");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _follow.Stop();
        _cts.Cancel();
        base.OnClosed(e);
    }
}
