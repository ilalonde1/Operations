#nullable enable
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using Kor.Operations.NetworkOps.Core.Power;
using Kor.Operations.NetworkOps.Core.Rack;
using Kor.Operations.NetworkOps.Service.Power;
using Kor.Operations.NetworkOps.Transport;
using Microsoft.Extensions.Options;

namespace Kor.Operations.NetworkOps.Service.Rack;

// Reads one rack device through its own least-privilege channel and hands the raw answer to its Core rule set:
//   Esxi       SSH key (from APP01 only) -> Rack/esxi-health.py run in hostd with a local ticket
//   Synology   SNMPv3 SHA/AES walk of the Synology MIBs
//   Veeam      REST as a Backup Viewer (cannot start a job)
//   UniFi      SSH as `netops`, whose ONLY permitted command prints the controller's status
//   Internet   from APP01 itself: public IP, DNS, pings through the firewall
//   CoreSwitch SNMPv3 SHA/DES (all the EdgeSwitch firmware offers)
//   Ups        the in-process UPS watcher's latest reading
//   Mesh       a server NetworkOps cannot read itself (no admin rights there): only whether its Mesh agent is connected,
//              from the last MeshCentral read -- enough to show it and Connect to it, and it says its health is not read
//   MeshServer KOR-MESH01: MeshCentral itself answering, and how many agents it has connected
// No collector writes to any device. A device that cannot be read comes back Unreachable with the reason.
internal sealed class RackCollector(IOptions<NetworkOpsOptions> options, PowerState power, Mesh.MeshState mesh, MacDirectory? macs = null,
    Network.NetworkMapService? map = null)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public async Task<RackResult> CollectAsync(RackDevice d, IReadOnlyDictionary<string, string> previousFacts, CancellationToken ct)
    {
        try
        {
            using var cap = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cap.CancelAfter(TimeSpan.FromSeconds(120));
            return d.Collector switch
            {
                "Esxi" => await EsxiAsync(d, cap.Token).ConfigureAwait(false),
                "Synology" => SynologyRules.Evaluate(await SnmpChannel.WalkAsync(d.Address, Snmp(sha256: false, des: false), SynologyRules.Tables, TimeSpan.FromSeconds(8), cap.Token).ConfigureAwait(false), d.VolumeFreeWarnPct),
                "Veeam" => await VeeamAsync(d, previousFacts, cap.Token).ConfigureAwait(false),
                "UniFi" => await UniFiAsync(d, cap.Token).ConfigureAwait(false),
                "Internet" => await InternetAsync(cap.Token).ConfigureAwait(false),
                "CoreSwitch" => await CoreSwitchAsync(d, previousFacts, cap.Token).ConfigureAwait(false),
                "Ups" => Ups(d),
                "WindowsServer" => await ServerAsync(d, cap.Token).ConfigureAwait(false),
                // A printer: read-only SNMP v1 "public" (SnmpChannel.GetV1Async says why v1 here), no password stored.
                "Printer" => PrinterRules.Evaluate(await SnmpChannel.GetV1Async(d.Address, "public", PrinterRules.Oids, [PrinterRules.ErrorState],
                    TimeSpan.FromSeconds(2), cap.Token).ConfigureAwait(false)),
                "Mesh" => RemoteOnly(d),
                "MeshServer" => MeshServer(),
                _ => RackResult.Unreachable($"no collector named '{d.Collector}'"),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return RackResult.Unreachable(ex is OperationCanceledException ? "timed out" : ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>A server seen only through remote control: up = its Mesh agent is connected. Says plainly that health is not read.</summary>
    private RackResult RemoteOnly(RackDevice d)
    {
        var name = d.MeshName.Length > 0 ? d.MeshName : d.Address;
        if (!mesh.Fresh) return RackResult.Unreachable("no recent MeshCentral read to judge it by" + (mesh.LastError is { } e ? $" ({e})" : ""));
        if (mesh.NodeNamed(name) is not { } node) return RackResult.Unreachable($"MeshCentral has no device named {name}");
        if (!node.AgentConnected) return RackResult.Unreachable("its Mesh agent is not connected");
        return new RackResult(true, null, new Dictionary<string, string> { ["mesh.node"] = node.Id }, [], [],
            "remote control connected; health not read (it is set up as remote-only: Collector \"Mesh\")");
    }

    /// <summary>KOR-MESH01: MeshCentral answered the read-only account recently; how many agents it has connected.</summary>
    private RackResult MeshServer()
    {
        if (!options.Value.MeshEnabled) return RackResult.Unreachable("remote control is not configured on APP01");
        if (!mesh.Fresh) return RackResult.Unreachable(mesh.LastError ?? "MeshCentral has not been read in 15 minutes");
        var nodes = mesh.Nodes;
        var connected = nodes.Count(n => n.AgentConnected);
        var facts = new Dictionary<string, string> { ["mesh.devices"] = nodes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        return new RackResult(true, null, facts, [], [], $"MeshCentral answering · {connected} of {nodes.Count} agents connected");
    }

    private SnmpV3Credentials Snmp(bool sha256, bool des)
    {
        var o = options.Value;
        return new SnmpV3Credentials(o.SnmpUser, o.SnmpAuthPassword, o.SnmpPrivPassword, sha256, des);
    }

    private async Task<RackResult> EsxiAsync(RackDevice d, CancellationToken ct)
    {
        var o = options.Value;
        using var sh = EsxiShell.Connect(d.Address, o.EsxiKeyPath, d.HostKeys.Count > 0 ? d.HostKeys : o.EsxiHostKeys.GetValueOrDefault(d.Address) ?? [], TimeSpan.FromSeconds(20));
        var (w, _, we) = await sh.RunWithInputAsync(HostScript.WriteStdinTo("/tmp/kor-health.py"), EsxiRules.Script, Timeout, ct).ConfigureAwait(false);
        if (w != 0) throw new InvalidOperationException("could not write the health script: " + we.Trim());
        var (exit, json, err) = await sh.RunAsync("python /tmp/kor-health.py", Timeout, ct).ConfigureAwait(false);
        if (exit != 0) throw new InvalidOperationException("health script failed: " + err.Trim());
        var production = o.PowerChain.Waves.SelectMany(x => x).Append(o.PowerChain.ControllerVm).Where(x => x.Length > 0).ToList();
        return EsxiRules.Evaluate(json, production, DateTime.UtcNow);
    }

    private async Task<RackResult> VeeamAsync(RackDevice d, IReadOnlyDictionary<string, string> previousFacts, CancellationToken ct)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.VeeamUser) || string.IsNullOrWhiteSpace(o.VeeamPassword)) return RackResult.Unreachable("KOR_NETWORKOPS_VEEAMUSER / _VEEAMPASSWORD not set");
        // BK01's REST certificate is Veeam's self-signed one on a workgroup box with no CA, so it is PINNED by its
        // SHA-256: the viewer password is only ever sent to that exact certificate.
        if (string.IsNullOrWhiteSpace(d.CertSha256)) return RackResult.Unreachable("no CertSha256 pinned for the Veeam REST API");
        using var http = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
                cert is not null && Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(cert.RawData)).Equals(d.CertSha256, StringComparison.OrdinalIgnoreCase),
        }) { BaseAddress = new Uri($"https://{d.Address}:9419/"), Timeout = Timeout };
        http.DefaultRequestHeaders.Add("x-api-version", "1.2-rev0");
        using var tokRes = await http.PostAsync("api/oauth2/token", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["grant_type"] = "password", ["username"] = o.VeeamUser, ["password"] = o.VeeamPassword }), ct).ConfigureAwait(false);
        var tokJson = await tokRes.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!tokRes.IsSuccessStatusCode) throw new InvalidOperationException($"Veeam login {(int)tokRes.StatusCode}");
        using var tok = System.Text.Json.JsonDocument.Parse(tokJson);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tok.RootElement.GetProperty("access_token").GetString());
        var jobs = await http.GetStringAsync("api/v1/jobs/states", ct).ConfigureAwait(false);
        var repos = await http.GetStringAsync("api/v1/backupInfrastructure/repositories/states", ct).ConfigureAwait(false);
        return VeeamRules.Evaluate(jobs, repos, DateTime.UtcNow, previousFacts);
    }

    private async Task<RackResult> UniFiAsync(RackDevice d, CancellationToken ct)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.UniFiStatusKeyPath)) return RackResult.Unreachable("KOR_NETWORKOPS_UNIFISTATUSKEYPATH not set");
        using var sh = EsxiShell.Connect(d.Address, "netops", o.UniFiStatusKeyPath, d.HostKeys, TimeSpan.FromSeconds(20));
        var (exit, json, err) = await sh.RunAsync("status", Timeout, ct).ConfigureAwait(false);   // the forced command runs whatever is asked
        if (exit != 0 || json.Length == 0) throw new InvalidOperationException("UniFi status command failed: " + err.Trim());
        var result = UniFiRules.Evaluate(json);
        map?.SetUniFi(json);   // the same read builds the port map after the sweep (Network/NetworkMapService)
        // The live API: ports up now, clients connected now. A failure leaves the map on the database read and says why.
        if (map is not null)
        {
            if (!o.UniFiApiEnabled) map.SetLive(null, "the UniFi live API is not set up (KOR_NETWORKOPS_UNIFIAPIUSER / _UNIFIAPIPASSWORD)");
            else if (string.IsNullOrWhiteSpace(d.CertSha256)) map.SetLive(null, "no CertSha256 pinned for the UniFi API");
            else
            {
                try { map.SetLive(await UniFiApi.ReadLiveAsync(d.Address, o.UniFiApiPort, o.UniFiSite, o.UniFiApiUser, o.UniFiApiPassword, d.CertSha256, ct).ConfigureAwait(false), null); }
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or System.Text.Json.JsonException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
                { map.SetLive(null, "the UniFi live API could not be read: " + ex.Message); }   // a shutdown cancel is NOT a read failure: let it propagate
            }
        }
        return result;
    }

    private async Task<RackResult> InternetAsync(CancellationToken ct)
    {
        var o = options.Value;
        string? ip = null;
        try { using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) }; ip = (await http.GetStringAsync("https://api.ipify.org", ct).ConfigureAwait(false)).Trim(); }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested)) { }   // shutdown propagates, a timeout does not
        var dns = false;
        try { dns = (await System.Net.Dns.GetHostAddressesAsync("www.microsoft.com", ct).ConfigureAwait(false)).Length > 0; }
        catch (System.Net.Sockets.SocketException) { }
        var pings = new Dictionary<string, (int, int, double)>();
        foreach (var target in o.InternetPingTargets)
        {
            using var ping = new Ping();
            int got = 0; double total = 0;
            for (var i = 0; i < 5; i++)
            {
                try { var r = await ping.SendPingAsync(target, 2000).ConfigureAwait(false); if (r.Status == IPStatus.Success) { got++; total += r.RoundtripTime; } }
                catch (PingException) { }
            }
            pings[target] = (5, got, got == 0 ? 0 : total / got);
        }
        return InternetRules.Evaluate(new InternetCheck(ip, o.ExpectedPublicIp, dns, pings));
    }

    private async Task<RackResult> CoreSwitchAsync(RackDevice d, IReadOnlyDictionary<string, string> previousFacts, CancellationToken ct)
    {
        var creds = Snmp(sha256: false, des: true);
        var sys = await SnmpChannel.GetAsync(d.Address, creds, [EdgeSwitchRules.SysDescr, EdgeSwitchRules.SysUpTime], TimeSpan.FromSeconds(8), ct).ConfigureAwait(false);
        var tables = await SnmpChannel.WalkAsync(d.Address, creds, EdgeSwitchRules.Tables, TimeSpan.FromSeconds(8), ct).ConfigureAwait(false);
        // What is plugged into each port, named (MacDirectory: UniFi, ARP + rack addresses + DNS); without it, the MACs.
        var label = macs is null ? null : await macs.LabellerAsync(ct).ConfigureAwait(false);
        var walk = sys.Concat(tables).ToDictionary(kv => kv.Key, kv => kv.Value);
        // The same read is the core's panel in the port map: it is an EdgeSwitch, so UniFi knows nothing of its ports.
        map?.SetCore(new Kor.Operations.NetworkOps.Core.Network.CoreSwitchRead(d.Name, d.Address, EdgeSwitchRules.Ports(walk), DateTime.UtcNow));
        return EdgeSwitchRules.Evaluate(walk, previousFacts, label);
    }

    /// <summary>A Windows server: Probes/server.ps1 through the same one-shot SCM channel the PC probes use (needs the
    /// service account in the server's local Administrators -- true today for APP01 and DC01).</summary>
    private static async Task<RackResult> ServerAsync(RackDevice d, CancellationToken ct)
    {
        var run = await new OnTargetChannel(TimeSpan.FromSeconds(90)).RunAsync(d.Address, Kor.Operations.NetworkOps.Core.Probes.ProbeLibrary.Get("server"), ct).ConfigureAwait(false);
        return run.Status == OnTargetStatus.Ok && run.OutputJson is { } json
            ? ServerRules.Evaluate(json)
            : RackResult.Unreachable($"{run.Status}: {run.Error}");
    }

    private RackResult Ups(RackDevice d)
    {
        if (!power.Configured) return RackResult.Unreachable("the UPS watcher is not running");
        var view = power.Views().FirstOrDefault(v => v.Latest.Ups.Equals(d.UpsName, StringComparison.OrdinalIgnoreCase));
        if (view is null) return RackResult.Unreachable("no reading yet");
        if (!view.Latest.Reachable) return RackResult.Unreachable(view.Latest.Error ?? "not answering");
        if (DateTime.UtcNow - view.Latest.AtUtc > TimeSpan.FromMinutes(3)) return RackResult.Unreachable($"no answer since {view.Latest.AtUtc.ToLocalTime():HH:mm}");
        return UpsRules.Evaluate(view.Latest);
    }
}
