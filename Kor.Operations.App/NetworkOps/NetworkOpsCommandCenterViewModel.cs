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

    private readonly NetworkOpsReader _reader;
    private readonly List<FleetRow> _allRows = new();
    private string _statusMessage = "Ready.";
    private bool _isLoading;
    private bool _autoRefresh = true;
    private string _filterText = string.Empty;
    private bool _problemsOnly;
    private bool _isConnectionLost;
    private string _connectionLostMessage = string.Empty;
    private DateTimeOffset? _lastSuccessfulRefreshAt;

    public NetworkOpsCommandCenterViewModel(NetworkOpsReader reader)
    {
        _reader = reader;
    }

    public NetworkOpsReader Reader => _reader;

    /// <summary>The last fleet read, handed to a PC's window so it opens on the same data.</summary>
    public FleetSnapshot? Snapshot { get; private set; }

    public ObservableCollection<FleetRow> Fleet { get; } = new();

    public ObservableCollection<PatternRow> Patterns { get; } = new();

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
            var snapshot = await _reader.GetFleetAsync(ct).ConfigureAwait(true);
            Apply(snapshot, DateTime.UtcNow);
            StatusMessage = $"Loaded at {DateTime.Now:HH:mm:ss}. {snapshot.Devices.Count} PCs, {snapshot.OpenFindings.Count} open findings, {snapshot.Patterns.Count} fleet patterns.";
            _lastSuccessfulRefreshAt = DateTimeOffset.Now;
            IsConnectionLost = false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = $"Load failed: {ex.GetType().Name}: {ex.Message}";
            var since = _lastSuccessfulRefreshAt.HasValue
                ? $"last good data from {_lastSuccessfulRefreshAt.Value.LocalDateTime:HH:mm:ss}"
                : "no data has loaded yet";
            ConnectionLostMessage = _reader.IsConfigured
                ? $"Connection lost — {since} — auto-retry in ~15s. ({ex.GetType().Name}: {ex.Message})"
                : ex.Message;
            IsConnectionLost = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Builds every row, tile and pattern from one fleet read. Internal so tests can drive it without a database.</summary>
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

    private void RebuildFleet()
    {
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
