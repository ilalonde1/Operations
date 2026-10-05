#nullable enable
using System;
using System.Linq;
using System.Windows;
using Kor.Operations.NetworkOps.Core.Learning;

namespace Kor.Operations.App.NetworkOps;

/// <summary>The console (Command Center) that hosts the Network surface as a tab. The navigator drives it there instead of
/// owning a standalone Network window: bring the console forward, switch to the Network tab, and focus a switch/port when
/// asked. Implemented by <see cref="NetworkOpsCommandCenterWindow"/>; the navigator holds it as its owner.</summary>
public interface INetworkTabHost
{
    void ShowNetwork(string? focusMac, int? port);
}

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

    /// <summary>The Network surface -- a tab of the console, brought forward -- at a switch or access point (by MAC, or
    /// "core"), and at one of its ports when given. Folded from a standalone window into a tab (2026-10-04): the navigator
    /// drives the console there, so there is still exactly one Network surface and one way into it.</summary>
    public void OpenNetwork(string? focusMac = null, int? port = null)
        => (_owner as INetworkTabHost)?.ShowNetwork(focusMac is { Length: > 0 } ? focusMac : null, port);

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
