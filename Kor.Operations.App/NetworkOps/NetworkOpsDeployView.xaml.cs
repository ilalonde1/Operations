#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Kor.Operations.NetworkOps.Core.Learning;

namespace Kor.Operations.App.NetworkOps;

/// <summary>One machine in the Fleet Deploy view: whether it is ticked, and how its deployment is going.</summary>
public sealed class DeployRowView(DeviceRow device) : INotifyPropertyChanged
{
    private bool _ticked;
    private string? _progress;

    public DeviceRow Device { get; } = device;
    public string Name => Device.Name;
    public string KindText => Device.Kind;
    public string Who => string.Join(" · ", new[]
    {
        Device.Presence is { Length: > 0 } p ? p : null,
        Device.AgentConnected ? "agent connected" : Device.AgentVersion is null ? "no agent" : "agent quiet",
    }.Where(s => s is { Length: > 0 }));

    // A PC can take a deployment; the rack (hosts, storage, switches) cannot.
    public bool CanTick => !Kor.Operations.NetworkOps.Core.Rack.RackKinds.IsRack(Device.Kind);
    public bool IsTicked { get => _ticked; set { if (_ticked != value && (CanTick || !value)) { _ticked = value; Changed(); } } }

    public long? ActionId { get; set; }
    public string Progress { get => _progress ?? ""; set { _progress = value; Changed(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public partial class NetworkOpsDeployView : UserControl
{
    private readonly NetworkOpsClient _client;
    private readonly CancellationTokenSource _cts = new();
    private readonly DispatcherTimer _follow = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly Dictionary<long, DeployRowView> _running = new();
    private List<DeployRowView> _all = [];
    private IReadOnlyList<DeployOpView> _ops = [];

    public NetworkOpsDeployView(NetworkOpsClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        InitializeComponent();
        _follow.Tick += async (_, _) => await Guard(FollowAsync).ConfigureAwait(true);
        // The tab is created once and hidden/shown by visibility, so Unloaded fires when the CONSOLE WINDOW closes (not on
        // a tab switch): the right place to stop the follow timer and cancel in-flight reads.
        Unloaded += (_, _) => { _follow.Stop(); _cts.Cancel(); };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await Guard(LoadAsync).ConfigureAwait(true);

    private async Task LoadAsync(CancellationToken ct)
    {
        Status("Reading…");
        var ops = await _client.GetDeployOpsAsync(ct).ConfigureAwait(true);
        var fleet = await _client.GetFleetAsync(ct).ConfigureAwait(true);
        Apply(ops, fleet);
        Status("");
    }

    /// <summary>Fills the view (public so a render test can fill it without a service). Ticks and progress survive a refresh.</summary>
    public void Apply(IReadOnlyList<DeployOpView> ops, FleetSnapshot fleet)
    {
        _ops = ops;
        OpBox.ItemsSource = _ops;
        if (OpBox.SelectedIndex < 0 && _ops.Count > 0) OpBox.SelectedIndex = 0;
        var ticked = _all.Where(r => r.IsTicked).Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var progress = _all.Where(r => r.Progress.Length > 0).ToDictionary(r => r.Name, r => (r.ActionId, r.Progress), StringComparer.OrdinalIgnoreCase);
        _all = fleet.Devices
            .Where(d => !Kor.Operations.NetworkOps.Core.Rack.RackKinds.IsRack(d.Kind))
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(d => { var v = new DeployRowView(d) { IsTicked = ticked.Contains(d.Name) };
                if (progress.TryGetValue(d.Name, out var pr)) { v.ActionId = pr.ActionId; v.Progress = pr.Progress; } return v; })
            .ToList();
        foreach (var r in _all) r.PropertyChanged += (_, ev) => { if (ev.PropertyName == nameof(DeployRowView.IsTicked)) UpdateButton(); };
        Grid.ItemsSource = _all;
        HeadlineText.Text = $"{_all.Count} machines";
        UpdateButton();
    }

    private DeployOpView? SelectedOp => OpBox.SelectedItem as DeployOpView;

    private void Op_Changed(object sender, SelectionChangedEventArgs e)
    {
        OpExplain.Text = SelectedOp?.Explain ?? "";
        UpdateButton();
    }

    private void Tick_Click(object sender, RoutedEventArgs e) => UpdateButton();
    private void TickAll_Click(object sender, RoutedEventArgs e) { foreach (var r in _all) r.IsTicked = r.CanTick; UpdateButton(); }
    private void UntickAll_Click(object sender, RoutedEventArgs e) { foreach (var r in _all) r.IsTicked = false; UpdateButton(); }

    private void UpdateButton()
    {
        var n = _all.Count(r => r.IsTicked);
        RunBtn.Content = $"Run on {n} ticked";
        RunBtn.IsEnabled = n > 0 && SelectedOp is not null;
    }

    private async void Run_Click(object sender, RoutedEventArgs e) => await Guard(RunAsync).ConfigureAwait(true);

    private async Task RunAsync(CancellationToken ct)
    {
        if (SelectedOp is not { } op) return;
        var ticked = _all.Where(r => r.IsTicked).ToList();
        if (ticked.Count == 0) return;
        var warn = op.Disruptive ? "\n\nThis closes the app and Outlook on each for about a minute." : "";
        if (MessageBox.Show(Window.GetWindow(this), $"Run \"{op.Title}\" on {ticked.Count} machine{(ticked.Count == 1 ? "" : "s")}?{warn}\n\n{string.Join(", ", ticked.Select(t => t.Name))}",
                op.Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;

        Status($"Queuing {ticked.Count}…");
        var outcomes = await _client.RunDeployAsync(op.Key, ticked.Select(t => t.Name).ToList(), ct).ConfigureAwait(true);
        var byName = _all.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var o in outcomes)
        {
            if (!byName.TryGetValue(o.Device, out var view)) continue;
            view.IsTicked = false;
            if (o.ActionId is { } id) { view.ActionId = id; view.Progress = "queued"; _running[id] = view; }
            else view.Progress = $"not run: {o.Refused}";
        }
        var queued = outcomes.Count(o => o.ActionId is not null);
        Status($"{queued} of {outcomes.Count} queued; each runs through its agent as SYSTEM and reports back here." +
               (outcomes.Any(o => o.ActionId is null) ? " The others say why on their row." : ""));
        UpdateButton();
        if (_running.Count > 0) _follow.Start();
    }

    private async Task FollowAsync(CancellationToken ct)
    {
        foreach (var (id, view) in _running.ToList())
        {
            if (await _client.GetActionAsync(id, ct).ConfigureAwait(true) is not { } a) continue;
            view.Progress = a.Status switch
            {
                "Requested" => "queued",
                "Running" => "running… (closing Outlook, swapping the install, re-registering the add-in)",
                "Done" => $"done: {a.Detail}",
                _ => $"{a.Status}: {a.Detail}",
            };
            if (a.Status is "Done" or "Failed" or "Refused") _running.Remove(id);
        }
        if (_running.Count == 0) { _follow.Stop(); Status("Every machine has finished. Each row shows how it went."); }
    }

    private void Status(string t) => StatusText.Text = t;

    private async Task Guard(Func<CancellationToken, Task> work)
    {
        try { await work(_cts.Token).ConfigureAwait(true); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status($"Could not do that: {ex.Message}"); }
    }
}
