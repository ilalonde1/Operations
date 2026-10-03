#nullable enable
using System;
using System.Linq;
using System.Windows;
using Kor.Operations.NetworkOps.Core.Learning;

namespace Kor.Operations.App.NetworkOps;

/// <summary>
/// The ONE way NetworkOps windows open each other (Ian, 2026-10-02: "I do NOT want duplicate ways to see duplicated data I
/// want duplicate ways to get into the same data"). A device's page, and the Network window at a switch or access point,
/// each exist once; every place that shows a device or a switch -- a Command Center row, a port tile, a UniFi tile on the
/// network's own page -- goes there through this, instead of drawing its own copy.
/// </summary>
public sealed class NetworkOpsNavigator
{
    private readonly NetworkOpsClient _client;
    private readonly Func<FleetSnapshot?> _pcs;
    private readonly Func<FleetSnapshot?> _rack;
    private readonly Window _owner;
    private NetworkOpsNetworkWindow? _network;

    /// <param name="pcs">The Command Center's current fleet snapshot.</param>
    /// <param name="rack">Its current rack snapshot.</param>
    public NetworkOpsNavigator(NetworkOpsClient client, Func<FleetSnapshot?> pcs, Func<FleetSnapshot?> rack, Window owner)
    {
        _client = client;
        _pcs = pcs;
        _rack = rack;
        _owner = owner;
    }

    public NetworkOpsClient Client => _client;

    /// <summary>A device by the name the rest of NetworkOps gives it: a fleet PC, or a rack device ("ESXi host .16 (standby)").</summary>
    public (FleetSnapshot Snapshot, DeviceRow Device)? Find(string name)
    {
        if (_pcs() is { } pcs && pcs.Devices.FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } pc) return (pcs, pc);
        if (_rack() is { } rack && rack.Devices.FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } r) return (rack, r);
        return null;
    }

    public bool HasPage(string name) => Find(name) is not null;

    /// <summary>A device's page: its parts, findings, history, Connect.</summary>
    public bool OpenDevice(string name)
    {
        if (Find(name) is not { } d) return false;
        Open(d.Snapshot, d.Device);
        return true;
    }

    public void Open(FleetSnapshot snapshot, DeviceRow device)
        => new NetworkOpsDeviceWindow(new NetworkOpsDeviceViewModel(_client, snapshot, device)) { Owner = _owner, Navigator = this }.Show();

    /// <summary>The Network window -- one, brought forward if open -- at a switch or access point (by MAC, or "core"), and at
    /// one of its ports when given.</summary>
    public void OpenNetwork(string? focusMac = null, int? port = null)
    {
        if (_network is null || !_network.IsLoaded)
        {
            _network = new NetworkOpsNetworkWindow(_client, this) { Owner = _owner };
            _network.Closed += (_, _) => _network = null;
            _network.Show();
        }
        else
        {
            if (_network.WindowState == WindowState.Minimized) _network.WindowState = WindowState.Normal;
            _network.Activate();
        }
        if (focusMac is { Length: > 0 }) _network.FocusOn(focusMac, port);
    }

    /// <summary>Follows a part's <see cref="Kor.Operations.NetworkOps.Core.Health.PcComponent.Opens"/> -- "network:",
    /// "network:{mac}", "network:core", "network:{mac}#{port}" -- false when it opens nothing.</summary>
    public bool Follow(string? opens)
    {
        if (ParseOpens(opens) is not { } target) return false;
        OpenNetwork(target.Key, target.Port);
        return true;
    }

    /// <summary>"network:{key}#{port}" -> (key, port); null for anything that is not a Network window link.</summary>
    public static (string Key, int? Port)? ParseOpens(string? opens)
    {
        if (opens is null || !opens.StartsWith("network:", StringComparison.Ordinal)) return null;
        var rest = opens["network:".Length..];
        var hash = rest.IndexOf('#');
        return hash < 0 ? (rest, null)
            : (rest[..hash], int.TryParse(rest[(hash + 1)..], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : null);
    }
}
