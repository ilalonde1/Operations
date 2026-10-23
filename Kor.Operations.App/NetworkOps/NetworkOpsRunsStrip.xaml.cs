#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Kor.Operations.NetworkOps.Core.Learning;
using Serilog;

namespace Kor.Operations.App.NetworkOps;

/// <summary>One machine inside a run: its name and its state in words.</summary>
public sealed record RunMachineRow(string Name, string State);

/// <summary>One run on the strip: its progress line, its machines, and whether it is still going.</summary>
public sealed class RunRow
{
    public required string Key { get; init; }
    public required string Line { get; init; }
    public required string Started { get; init; }
    public required bool InFlight { get; init; }
    public required Brush Accent { get; init; }
    public required IReadOnlyList<RunMachineRow> Machines { get; init; }
    public bool IsExpanded { get; set; }
    public string ExpandLabel => $"Show {Machines.Count} machine{(Machines.Count == 1 ? "" : "s")} ▾";
    public string CollapseLabel => "Hide machines ▴";
    public Visibility DismissVisibility => InFlight ? Visibility.Collapsed : Visibility.Visible;
}

/// <summary>
/// The runs strip under the Command Center's tabs: every fix, install or deploy run in flight, and those finished in the
/// last hour, wherever it was started (any tab, the MCP, the CLI) -- one line each with its progress, machines on demand.
/// Ian, 2026-10-22, after 29 installs ran with the screen saying only "Loading…": "We need a better way to show what's
/// happening." The Updates and Deploy tabs still show progress on their own rows; this is the one place for all of it.
/// </summary>
public partial class NetworkOpsRunsStrip : UserControl
{
    private static readonly ILogger Log = Serilog.Log.ForContext<NetworkOpsRunsStrip>();
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(8);
    private static readonly Brush InFlightBrush = Frozen(Color.FromRgb(0x2F, 0x54, 0x96));   // the console's link blue: "going", not a verdict
    private readonly ObservableCollection<RunRow> _rows = new();
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dismissed = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _timer = new() { Interval = Interval };
    private NetworkOpsClient? _client;
    private bool _busy;

    public NetworkOpsRunsStrip()
    {
        InitializeComponent();
        RunList.ItemsSource = _rows;
        _timer.Tick += async (_, _) => { if (IsVisible || _rows.Count == 0) await ReloadAsync(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    /// <summary>Start following runs through this client (the window hands it over once it has one).</summary>
    public void Attach(NetworkOpsClient client)
    {
        _client = client;
        _timer.Start();
        _ = ReloadAsync();
    }

    /// <summary>Read again now (after a button queued a run, so it appears at once rather than on the next tick).</summary>
    public Task NudgeAsync() => ReloadAsync();

    private async Task ReloadAsync()
    {
        if (_client is null || _busy) return;
        _busy = true;
        try { Apply(await _client.GetActionRunsAsync(CancellationToken.None).ConfigureAwait(true), DateTime.UtcNow); }
        // silent-catch OK: the strip is a progress readout; a failed read keeps the last lines and tries again next tick.
        catch (Exception ex) { Log.Debug(ex, "Runs strip: read failed"); }
        finally { _busy = false; }
    }

    /// <summary>Fill the strip from the service's runs. Internal so a render test can drive it without the service.</summary>
    internal void Apply(IReadOnlyList<ActionRun> runs, DateTime nowUtc)
    {
        _rows.Clear();
        foreach (var r in runs)
        {
            var key = $"{r.Kind}|{r.RequestedBy}|{r.StartedUtc.Ticks}";
            if (_dismissed.Contains(key)) continue;
            _rows.Add(new RunRow
            {
                Key = key,
                Line = r.Line(nowUtc),
                Started = $"started {r.StartedUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)} by {r.RequestedBy}",
                InFlight = r.InFlight,
                // Green only when every machine finished Done; anything failed or not run is amber, so green is never a guess.
                Accent = r.InFlight ? InFlightBrush : r.Done == r.Total ? NetworkOpsBrushes.Healthy : NetworkOpsBrushes.Attention,
                Machines = r.Machines.OrderBy(m => m.Status switch { "Running" => 0, "Requested" => 1, "Done" => 3, _ => 2 })
                    .ThenBy(m => m.Device, StringComparer.OrdinalIgnoreCase)
                    .Select(m => new RunMachineRow(m.Device, ActionRun.MachineLine(m, nowUtc))).ToList(),
                IsExpanded = _expanded.Contains(key),
            });
        }
        Visibility = _rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Toggle_Changed(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RunRow row) return;
        row.IsExpanded = (sender as System.Windows.Controls.Primitives.ToggleButton)?.IsChecked == true;
        if (row.IsExpanded) _expanded.Add(row.Key); else _expanded.Remove(row.Key);
    }

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RunRow row) return;
        _dismissed.Add(row.Key);
        _rows.Remove(row);
        if (_rows.Count == 0) Visibility = Visibility.Collapsed;
    }
}
