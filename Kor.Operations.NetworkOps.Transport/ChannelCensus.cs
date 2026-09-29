#nullable enable
using System.ComponentModel;
using System.DirectoryServices;
using System.Net.Sockets;

namespace Kor.Operations.NetworkOps.Transport;

// Step 0 of NetworkOps (roadmap §6): which management channel answers on which machine,
// measured per host. Records disagreed for months because transport is per-machine and nobody
// had measured it fleet-wide: KOR-305 answered WSMan, KOR-218N neither, the rest SMB only.
public static class ChannelCensus
{
    public static async Task<CensusRow> ProbeAsync(string host, CancellationToken ct = default)
    {
        var reach = await SmbReachability.ProbeAsync(host, ct: ct).ConfigureAwait(false);
        if (!reach.Reachable)
            return new CensusRow(host, reach.Resolved.Select(a => a.ToString()).ToArray(), null, false, false, false, null, false, false);

        var target = reach.AnsweredOn!.ToString();
        var adminShare = Directory.Exists($@"\\{host}\c$\Windows");
        var adminWrite = false;
        if (adminShare)
        {
            var probe = $@"\\{host}\c$\Windows\Temp\_kor_census_{Guid.NewGuid():N}.tmp";
            try { await File.WriteAllTextAsync(probe, "probe", ct).ConfigureAwait(false); File.Delete(probe); adminWrite = true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { adminWrite = false; }
        }

        var serviceControl = false;
        string? remoteRegistry = null;
        try
        {
            using var scm = ServiceControlManager.OpenManager(host, forCreate: false);
            serviceControl = true;
            remoteRegistry = ServiceControlManager.QueryState(scm, "RemoteRegistry")?.ToString() ?? "absent";
        }
        catch (Win32Exception) { serviceControl = false; }

        var winrm = await PortOpenAsync(target, 5985, ct).ConfigureAwait(false);
        var rdp = await PortOpenAsync(target, 3389, ct).ConfigureAwait(false);
        return new CensusRow(host, reach.Resolved.Select(a => a.ToString()).ToArray(), target,
            adminShare, adminWrite, serviceControl, remoteRegistry, winrm, rdp);
    }

    private static async Task<bool> PortOpenAsync(string address, int port, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(2));
        using var c = new TcpClient();
        try { await c.ConnectAsync(address, port, cts.Token).ConfigureAwait(false); return c.Connected; }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException) { return false; }
    }
}

/// <param name="AnsweredOn">The address that answered SMB; null when the host is unreachable.</param>
public sealed record CensusRow(
    string Host, IReadOnlyList<string> Resolved, string? AnsweredOn,
    bool AdminShare, bool AdminWrite, bool ServiceControl, string? RemoteRegistry, bool WinRm, bool Rdp)
{
    public bool Reachable => AnsweredOn is not null;
}

// The fleet is whatever Active Directory says it is: enabled, non-server computer accounts.
// Same filter the 09-15 Revit sweep and the 09-28 fleet runs used (38 machines).
public static class ActiveDirectoryFleet
{
    public static IReadOnlyList<string> Workstations()
    {
        using var searcher = new DirectorySearcher(
            "(&(objectCategory=computer)(!(operatingSystem=*Server*))(!(userAccountControl:1.2.840.113556.1.4.803:=2)))")
        { PageSize = 500 };
        searcher.PropertiesToLoad.Add("name");
        using var results = searcher.FindAll();
        return results.Cast<SearchResult>()
            .Select(r => r.Properties["name"].Count > 0 ? r.Properties["name"][0]?.ToString() : null)
            .OfType<string>()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
