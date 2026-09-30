#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Kor.Operations.Core;
using Kor.Operations.NetworkOps.Core.Learning;

namespace Kor.Operations.App.NetworkOps;

// The fleet at a glance: every PC as one colour and one line, the fleet patterns the service has
// found, and whether the service itself is alive. Read-only; everything done to one PC happens in
// its own window (NetworkOpsDeviceWindow).
//
// IAiContextProvider intentionally NOT implemented: this is infrastructure, like FileSync, and is
// kept out of the AI panel's scope until Ian decides otherwise.
public sealed class NetworkOpsCommandCenterViewModel : ObservableObject
{
    /// <summary>A heartbeat older than this means the service is not running.</summary>
    private static readonly TimeSpan ServiceSilentAfter = TimeSpan.FromMinutes(3);

    private readonly NetworkOpsClient _client;
    private readonly List<FleetRow> _allRows = new();
    private readonly List<FleetRow> _allRack = new();
    private string _statusMessage = "Ready.";
    private bool _isLoading;
    private bool _autoRefresh = true;
    private string _filterText = string.Empty;
    private bool _problemsOnly;
    private bool _isConnectionLost;
    private string _connectionLostMessage = string.Empty;
    private DateTimeOffset? _lastSuccessfulRefreshAt;

    public NetworkOpsCommandCenterViewModel(NetworkOpsClient client)
    {
        _client = client;
    }

    public NetworkOpsClient Client => _client;

    /// <summary>The last fleet read, handed to a PC's window so it opens on the same data.</summary>
    public FleetSnapshot? Snapshot { get; private set; }

    public ObservableCollection<FleetRow> Fleet { get; } = new();

    public ObservableCollection<PatternRow> Patterns { get; } = new();

    // ---- The rack: hosts, storage, UPSes, backups, network, internet -- one row each, worst first.
    public ObservableCollection<FleetRow> Rack { get; } = new();
    public FleetSnapshot? RackSnapshot { get; private set; }
    public string RackHeadline { get; private set; } = "—";
    public string RackSubline { get; private set; } = "Rack";
    public Brush RackBrush { get; private set; } = NetworkOpsBrushes.Unknown;

    // ---- Rack power: both UPSes, the verdict, the chain.
    public ObservableCollection<UpsLine> Ups { get; } = new();
    public string PowerHeadline { get; private set; } = "—";
    public Brush PowerBrush { get; private set; } = NetworkOpsBrushes.Unknown;
    public string PowerReason { get; private set; } = "";
    public string ChainText { get; private set; } = "";
    public string LastRehearsalText { get; private set; } = "";

    public string CriticalHeadline { get; private set; } = "—";
    public string AttentionHeadline { get; private set; } = "—";
    public string HealthyHeadline { get; private set; } = "—";
    public string StaleHeadline { get; private set; } = "—";
    public string ServiceHeadline { get; private set; } = "—";
    public string ServiceSubline { get; private set; } = "Service";
    public Brush ServiceBrush { get; private set; } = NetworkOpsBrushes.Unknown;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    public bool AutoRefresh
    {
        get => _autoRefresh;
        set => SetField(ref _autoRefresh, value);
    }

    /// <summary>Filters the grid by PC name, model or what is wrong ("P340", "disk", "KOR-2").</summary>
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetField(ref _filterText, value ?? string.Empty))
                RebuildFleet();
        }
    }

    /// <summary>Hide PCs with nothing live on them.</summary>
    public bool ProblemsOnly
    {
        get => _problemsOnly;
        set
        {
            if (SetField(ref _problemsOnly, value))
                RebuildFleet();
        }
    }

    public bool IsConnectionLost
    {
        get => _isConnectionLost;
        private set => SetField(ref _isConnectionLost, value);
    }

    public string ConnectionLostMessage
    {
        get => _connectionLostMessage;
        private set => SetField(ref _connectionLostMessage, value);
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        if (IsLoading)
            return;
        IsLoading = true;
        StatusMessage = "Loading...";
        try
        {
            var snapshot = await _client.GetFleetAsync(ct).ConfigureAwait(true);
            Apply(snapshot, DateTime.UtcNow);
            // The rack and power are their own reads: a service without them must not blank the PCs.
            try { ApplyRack(await _client.GetRackAsync(ct).ConfigureAwait(true), DateTime.UtcNow); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RackSnapshot = null; _allRack.Clear(); Rack.Clear();
                (RackHeadline, RackSubline, RackBrush) = ("—", $"Rack not available: {ex.Message}", NetworkOpsBrushes.Unknown);
                foreach (var name in new[] { nameof(RackHeadline), nameof(RackSubline), nameof(RackBrush) }) OnPropertyChanged(name);
            }
            try { ApplyPower(await _client.GetPowerAsync(ct).ConfigureAwait(true), DateTime.UtcNow); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ApplyPower(new PowerSnapshot([], "Off", $"Could not read rack power: {ex.Message}", null, false, []), DateTime.UtcNow);
            }
            StatusMessage = $"Loaded at {DateTime.Now:HH:mm:ss}. {Rack.Count} rack devices, {snapshot.Devices.Count} PCs, " +
                            $"{snapshot.OpenFindings.Count + (RackSnapshot?.OpenFindings.Count ?? 0)} open findings, {snapshot.Patterns.Count} fleet patterns.";
            _lastSuccessfulRefreshAt = DateTimeOffset.Now;
            IsConnectionLost = false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = $"Load failed: {ex.GetType().Name}: {ex.Message}";
            var since = _lastSuccessfulRefreshAt.HasValue
                ? $"last good data from {_lastSuccessfulRefreshAt.Value.LocalDateTime:HH:mm:ss}"
                : "no data has loaded yet";
            ConnectionLostMessage = _client.IsConfigured
                ? $"Connection lost — {since} — auto-retry in ~15s. ({ex.GetType().Name}: {ex.Message})"
                : ex.Message;
            IsConnectionLost = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Builds every row, tile and pattern from one fleet read. Internal so tests can drive it without the service.</summary>
    internal void Apply(FleetSnapshot snapshot, DateTime nowUtc)
    {
        Snapshot = snapshot;
        _allRows.Clear();
        _allRows.AddRange(FleetRow.Ordered(snapshot.Devices.Select(d => FleetRow.From(d, snapshot, nowUtc))));
        RebuildFleet();

        Patterns.Clear();
        foreach (var p in snapshot.Patterns)
        {
            var members = CommandCenterView.MembersOf(p, snapshot.FactsByDevice, snapshot.OpenFindings);
            Patterns.Add(new PatternRow
            {
                Summary = p.Summary,
                MembersText = members.Count == 0 ? "" : string.Join(", ", members),
                SinceText = $"seen since {NetworkOpsText.When(p.FirstSeenUtc)}",
            });
        }

        var total = _allRows.Count;
        CriticalHeadline = _allRows.Count(r => r.State == HealthState.Critical).ToString();
        AttentionHeadline = _allRows.Count(r => r.State == HealthState.Attention).ToString();
        HealthyHeadline = $"{_allRows.Count(r => r.State == HealthState.Healthy)} / {total}";
        StaleHeadline = _allRows.Count(r => r.IsStale || r.State == HealthState.Unknown).ToString();

        if (snapshot.Service is { } beat)
        {
            var silent = nowUtc - beat.LastBeatUtc > ServiceSilentAfter;
            ServiceHeadline = silent ? "Silent" : "Running";
            ServiceSubline = $"Service on {beat.Host} · {beat.Version ?? "?"} · heartbeat {CommandCenterView.Ago(beat.LastBeatUtc, nowUtc)}";
            ServiceBrush = silent ? NetworkOpsBrushes.Critical : NetworkOpsBrushes.Healthy;
        }
        else
        {
            ServiceHeadline = "Never ran";
            ServiceSubline = "No service heartbeat recorded";
            ServiceBrush = NetworkOpsBrushes.Critical;
        }

        foreach (var name in new[] { nameof(CriticalHeadline), nameof(AttentionHeadline), nameof(HealthyHeadline), nameof(StaleHeadline),
                                     nameof(ServiceHeadline), nameof(ServiceSubline), nameof(ServiceBrush), nameof(Snapshot) })
            OnPropertyChanged(name);
    }

    /// <summary>
    /// Fills the Rack section and its tile. The tile's colour is the WORST device's, so one red host makes the rack
    /// red however many are green. Worst first, then in the order an outage travels: the line in, the hosts, the
    /// storage under them, the backups, the power, the network. Internal so tests can drive it without the service.
    /// </summary>
    internal void ApplyRack(FleetSnapshot rack, DateTime nowUtc)
    {
        RackSnapshot = rack;
        string[] order = ["Internet", "Host", "Storage", "Server", "Backup", "UPS", "Network"];
        var rows = rack.Devices.Select(d => FleetRow.From(d, rack, nowUtc))
            .OrderByDescending(r => r.State).ThenBy(r => Array.IndexOf(order, r.Kind) is var i && i >= 0 ? i : 99).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        _allRack.Clear();
        _allRack.AddRange(rows);
        RebuildRack();

        var healthy = rows.Count(r => r.State is HealthState.Healthy or HealthState.Watch);
        var worst = rows.Count == 0 ? HealthState.Unknown : rows.Max(r => r.State);
        var stale = rows.Count(r => r.IsStale);
        RackHeadline = rows.Count == 0 ? "—" : $"{healthy} / {rows.Count}";
        RackSubline = rows.Count == 0 ? "Rack: nothing read yet"
            : worst is HealthState.Critical or HealthState.Attention
                ? $"Rack: {rows.Count(r => r.State == HealthState.Critical)} critical, {rows.Count(r => r.State == HealthState.Attention)} need attention"
                : "Rack healthy" + (stale > 0 ? $" · {stale} not read recently" : "");
        RackBrush = rows.Count == 0 ? NetworkOpsBrushes.Unknown : NetworkOpsBrushes.For(worst);
        foreach (var name in new[] { nameof(RackHeadline), nameof(RackSubline), nameof(RackBrush), nameof(RackSnapshot) })
            OnPropertyChanged(name);
    }

    /// <summary>Fills the Rack power card. Internal so tests can drive it without the service.</summary>
    internal void ApplyPower(PowerSnapshot p, DateTime nowUtc)
    {
        Ups.Clear();
        foreach (var u in p.Ups) Ups.Add(UpsLine.From(u, nowUtc));

        (PowerHeadline, PowerBrush) = p.Level switch
        {
            "Normal" => ("Mains OK", NetworkOpsBrushes.Healthy),
            "Degraded" => ("Needs attention", NetworkOpsBrushes.Attention),
            "Trigger" => ("SHUTTING DOWN", NetworkOpsBrushes.Critical),
            _ => ("Not watched", NetworkOpsBrushes.Unknown),
        };
        PowerReason = p.LevelSinceUtc is { } since ? $"{p.Reason} (since {NetworkOpsText.When(since)})" : p.Reason;
        ChainText = p.Armed
            ? "Shutdown chain ARMED: a real outage shuts the rack down cleanly."
            : "Shutdown chain NOT armed: a real outage runs it as a dry run only (until the live test passes).";
        var rehearsal = p.RecentEvents.FirstOrDefault(e => e.Kind == "ChainEnd");
        LastRehearsalText = rehearsal is null
            ? "No chain run recorded yet."
            : $"Last chain run {NetworkOpsText.When(rehearsal.AtUtc)}{(rehearsal.DryRun ? " (dry run)" : "")}: {(rehearsal.Ok ? "" : "PROBLEMS — ")}{rehearsal.Text}";

        foreach (var name in new[] { nameof(PowerHeadline), nameof(PowerBrush), nameof(PowerReason), nameof(ChainText), nameof(LastRehearsalText) })
            OnPropertyChanged(name);
    }

    /// <summary>Queues a dry run of the shutdown chain; its result shows on the card within a few minutes.</summary>
    public async Task RehearseAsync(CancellationToken ct)
    {
        try
        {
            await _client.QueuePowerRehearsalAsync(ct).ConfigureAwait(true);
            StatusMessage = "Rehearsal queued: a dry run of the whole chain against the live rack (nothing is shut down). The result shows on the Rack power card in about 2 minutes.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = $"Rehearsal not queued: {ex.Message}";
        }
    }

    /// <summary>The same Find box filters the rack ("veeam", "ups", "host .10") -- and "problems only" hides healthy devices.</summary>
    private void RebuildRack()
    {
        var filter = _filterText.Trim();
        Rack.Clear();
        foreach (var row in _allRack)
        {
            if (_problemsOnly && row.LiveCount == 0) continue;
            if (filter.Length > 0 && !(row.Matches(filter) || row.Kind.Contains(filter, StringComparison.OrdinalIgnoreCase))) continue;
            Rack.Add(row);
        }
    }

    private void RebuildFleet()
    {
        RebuildRack();
        var filter = _filterText.Trim();
        Fleet.Clear();
        foreach (var row in _allRows)
        {
            if (_problemsOnly && row.LiveCount == 0) continue;
            if (filter.Length > 0 && !row.Matches(filter)) continue;
            Fleet.Add(row);
        }
    }
}
