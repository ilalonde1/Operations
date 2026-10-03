#nullable enable
using System.Text.Json;
using Kor.Operations.NetworkOps.Core.Learning;

namespace Kor.Operations.NetworkOps.Cli;

// The verbs a Claude session uses, all through NetworkOps on APP01 (AppServer.cs) -- nothing goes from this PC to the
// machine being read:
//
//   netops run        --script x.ps1 --hosts A,B [--timeout 90] [--purpose "why"] [--run <prompt run id>] [--out dir]
//   netops last-check --hosts A,B                the last full health check NetworkOps stored, as returned
//   netops check      --hosts A,B                a fresh full health check now (stored; the Command Center sees it)
//   netops knowledge  [--search words] [--all]   banked knowledge cards (accepted; --all adds proposed/rejected)
//   netops findings   [--hosts A,B]              open findings across the fleet (the Command Center's list), as JSON
//   netops fix        --hosts A,B --fix <id> [--param x] [--finding rule] [--purpose "why"] [--confirmed] [--timeout s]
//                                                queue a CATALOG fix (Core/Actions/FixCatalog) exactly as the Command
//                                                Center's Fix… does: same API, same refusals, audited; waits for it
//   netops history    --hosts A,B                a machine's past problems, fact changes, notes and the fixes run on it
//   netops readings   --hosts A,B                every latest reading it stored (CPU, volumes, disks, ports, jobs...), as JSON
//   netops action     --id N                     one fix or run: status, what it said, its full output
//   netops trigger    --id N                     one queued check or job: status and result
//   netops updates    [--scan]                   what Windows Update has waiting everywhere; --scan searches again first
//   netops changes    [--since 24h|7d|2026-10-02T01:45]   the morning brief: opened, cleared, done, open now
//   netops network    [--search KOR-207|markb|192.168.1.73]  the port map: every switch, port, device and person
//   netops add-pc     --hosts NAME           a PC outside the domain: its one-time code and the command to run on it
//
// --hosts takes a PC or a rack device (KOR-217, KOR-FS01: a rack name matches up to its bracket), "all" (every PC) or
// "rack" (every rack device): one resolver (Core/Learning/HostNames.Resolve) for every verb.
// `netops run --direct ...` is the old route (this PC straight to the machine over SMB): for the LAN, not for a session.
internal static class SessionVerbs
{
    public static bool Handles(string verb, string[] args)
        => verb is "check" or "last-check" or "knowledge" or "findings" or "fix" or "history" or "readings" or "action" or "trigger" or "updates" or "changes" or "network" or "add-pc" || (verb == "run" && !args.Contains("--direct"));

    public static async Task<int> RunAsync(string verb, string[] args)
    {
        string? hostsArg = null, script = null, outDir = null, purpose = null, search = null, fixId = null, param = null, finding = null, since = null;
        long? promptRun = null, id = null;
        var timeout = 90;
        var all = false;
        var confirmed = false;
        var scan = false;
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--hosts" when i + 1 < args.Length: hostsArg = args[++i]; break;
                case "--script" when i + 1 < args.Length: script = args[++i]; break;
                case "--out" when i + 1 < args.Length: outDir = args[++i]; break;
                case "--purpose" when i + 1 < args.Length: purpose = args[++i]; break;
                case "--search" when i + 1 < args.Length: search = args[++i]; break;
                case "--all": all = true; break;
                case "--fix" when i + 1 < args.Length: fixId = args[++i]; break;
                case "--param" when i + 1 < args.Length: param = args[++i]; break;
                case "--finding" when i + 1 < args.Length: finding = args[++i]; break;
                case "--confirmed": confirmed = true; break;
                case "--timeout" when i + 1 < args.Length && int.TryParse(args[i + 1], out var t): timeout = t; i++; break;
                case "--run" when i + 1 < args.Length && long.TryParse(args[i + 1], out var r): promptRun = r; i++; break;
                case "--id" when i + 1 < args.Length && long.TryParse(args[i + 1], out var n): id = n; i++; break;
                case "--since" when i + 1 < args.Length: since = args[++i]; break;
                case "--scan": scan = true; break;
                default: Console.Error.WriteLine($"Unknown argument for {verb}: {args[i]}"); return 2;
            }
        }

        using var server = new AppServer(TimeSpan.FromSeconds(Math.Max(timeout, 60) + 60));
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        var ct = cts.Token;
        try
        {
            if (verb == "knowledge") return await KnowledgeAsync(server, search, all, ct);
            if (verb == "findings") return await FindingsAsync(server, hostsArg, ct);
            if (verb == "updates") return await UpdatesAsync(server, scan, ct);
            if (verb == "changes") return await ChangesAsync(server, since, ct);
            if (verb == "add-pc")
            {
                // A PC outside the domain: the one-time code and the command to run on it as an administrator.
                if (hostsArg is not { Length: > 0 } pcName) { Console.Error.WriteLine("--hosts NAME is required (the PC's Windows computer name)."); return 2; }
                var enrol = await server.PostAsync<ManualPcAnswer>("/api/devices/manual", new { name = pcName }, ct);
                Console.WriteLine($"{enrol.Device}: code {enrol.Code}, good until {enrol.ExpiresUtc.ToLocalTime():HH:mm}, once.");
                Console.WriteLine("On that PC, in PowerShell run as administrator:");
                Console.WriteLine(enrol.Command);
                return 0;
            }
            if (verb == "network")
            {
                Console.Write(Kor.Operations.NetworkOps.Core.Network.NetworkMapText.Render(
                    await server.GetAsync<Kor.Operations.NetworkOps.Core.Network.NetworkMapResponse>("/api/network", ct), search));
                return 0;
            }
            if (verb is "action" or "trigger")
            {
                if (id is null) { Console.Error.WriteLine($"--id N is required (the {verb} id, as netops and the Command Center print it)."); return 2; }
                Console.WriteLine(verb == "action"
                    ? JsonSerializer.Serialize(await server.GetAsync<ActionRow>($"/api/actions/{id}", ct), Indented)
                    : JsonSerializer.Serialize(await server.GetAsync<TriggerState>($"/api/triggers/{id}", ct), Indented));
                return 0;
            }

            if (string.IsNullOrWhiteSpace(hostsArg)) { Console.Error.WriteLine("--hosts A,B is required (the machine names NetworkOps knows, e.g. KOR-217 or KOR-DC01; or all, or rack)."); return 2; }
            var known = await KnownAsync(server, ct);
            var hosts = HostNames.Resolve(known.Pcs, known.Rack, hostsArg, out var unknown);
            foreach (var u in unknown) Console.WriteLine($"=== {u}: NetworkOps does not know this machine");
            if (hosts.Count == 0) return 1;

            var code = verb switch
            {
                "run" => await RunScriptAsync(server, hosts.Select(h => h.Name).ToList(), script, timeout, purpose, promptRun, outDir, ct),
                "last-check" => await LastCheckAsync(server, hosts.Select(h => h.Name).ToList(), ct),
                "check" => await CheckAsync(server, hosts.Select(h => h.Name).ToList(), ct),
                "fix" => await FixAsync(server, hosts, fixId, param, finding, purpose, confirmed, timeout, ct),
                "history" => await HistoryAsync(server, hosts, ct),
                "readings" => await ReadingsAsync(server, hosts, ct),
                _ => 2,
            };
            return unknown.Count > 0 && code == 0 ? 1 : code;
        }
        catch (AppServerException ex) { Console.Error.WriteLine($"netops: {ex.Message}"); return 1; }
        catch (HttpRequestException ex) { Console.Error.WriteLine($"netops: could not reach NetworkOps on APP01 ({ex.Message}). On the VPN? curl.exe -sk {AppServer.BaseUrl}/api/ping"); return 1; }
        catch (Microsoft.Identity.Client.MsalException ex) { Console.Error.WriteLine($"netops: sign-in failed ({ex.ErrorCode}): {ex.Message}"); return 1; }
    }

    private static async Task<int> RunScriptAsync(AppServer server, IReadOnlyList<string> hosts, string? script, int timeout, string? purpose, long? promptRun, string? outDir, CancellationToken ct)
    {
        if (script is null || !File.Exists(script)) { Console.Error.WriteLine("--script <file.ps1> is required and must exist."); return 2; }
        var body = await File.ReadAllTextAsync(script, ct);
        var results = new System.Collections.Concurrent.ConcurrentBag<RemoteRunResult>();
        await Parallel.ForEachAsync(hosts, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (h, c) =>
        {
            try { results.Add(await server.PostAsync<RemoteRunResult>($"/api/devices/{Uri.EscapeDataString(h)}/run", new RemoteRunRequest(body, timeout, purpose, promptRun), c)); }
            catch (AppServerException ex) { results.Add(new RemoteRunResult(0, h, false, "-", 0, null, ex.Message)); }
        });
        var ordered = results.OrderBy(r => r.Device, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var r in ordered)
            Console.WriteLine($"=== {r.Device} [{(r.Ok ? "Ok" : "Failed")}] {r.Ms} ms via APP01 ({r.Route}), audited as action {r.ActionId}{(r.Error is null ? "" : $" -- {r.Error}")}{Environment.NewLine}{(r.OutputJson is null ? "" : Pretty(r.OutputJson))}");
        Console.WriteLine();
        Console.WriteLine($"Ok {ordered.Count(r => r.Ok)} of {ordered.Count}");
        if (outDir is not null)
        {
            Directory.CreateDirectory(outDir);
            var path = Path.Combine(outDir, $"netops-run-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(ordered, new JsonSerializerOptions { WriteIndented = true }), ct);
            Console.WriteLine($"wrote {path}");
        }
        return ordered.All(r => r.Ok) ? 0 : 1;
    }

    private static async Task<int> LastCheckAsync(AppServer server, IReadOnlyList<string> hosts, CancellationToken ct)
    {
        var failed = 0;
        foreach (var h in hosts)
        {
            try
            {
                var last = await server.GetAsync<LastCheckView>($"/api/devices/{Uri.EscapeDataString(h)}/last-check", ct);
                Console.WriteLine($"=== {last.Device}: last {last.Probe} check {last.AtUtc.ToLocalTime():yyyy-MM-dd HH:mm} ({(DateTime.UtcNow - last.AtUtc).TotalMinutes:0} min ago)");
                Console.WriteLine(Pretty(last.Json));
            }
            catch (AppServerException ex) { Console.WriteLine($"=== {h}: {ex.Message}"); failed++; }
        }
        return failed == 0 ? 0 : 1;
    }

    private sealed record Queued(long TriggerId);

    private static async Task<int> CheckAsync(AppServer server, IReadOnlyList<string> hosts, CancellationToken ct)
    {
        var queued = new List<(string Host, long Id)>();
        foreach (var h in hosts)
        {
            try { queued.Add((h, (await server.PostAsync<Queued>($"/api/devices/{Uri.EscapeDataString(h)}/check", new { }, ct)).TriggerId)); }
            catch (AppServerException ex) { Console.WriteLine($"=== {h}: {ex.Message}"); }
        }
        Console.Error.WriteLine($"netops: {queued.Count} check(s) queued on APP01; waiting (a PC with its agent answers in seconds, without it about a minute)...");
        var deadline = DateTime.UtcNow.AddMinutes(4);
        var pending = queued.ToList();
        while (pending.Count > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
            foreach (var q in pending.ToList())
            {
                var t = await server.GetAsync<TriggerState>($"/api/triggers/{q.Id}", ct);
                if (t.Status is "Pending" or "Running") continue;
                pending.Remove(q);
                Console.WriteLine($"=== {q.Host}: check {t.Status}{(t.Result is null ? "" : $" -- {t.Result}")}");
            }
        }
        foreach (var q in pending) Console.WriteLine($"=== {q.Host}: still running after 4 min (trigger {q.Id}); read it later with netops last-check");
        return await LastCheckAsync(server, queued.Where(q => !pending.Contains(q)).Select(q => q.Host).ToList(), ct) == 0 && pending.Count == 0 ? 0 : 1;
    }

    private static async Task<int> KnowledgeAsync(AppServer server, string? search, bool all, CancellationToken ct)
    {
        var path = $"/api/knowledge?all={(all ? "true" : "false")}{(string.IsNullOrWhiteSpace(search) ? "" : $"&search={Uri.EscapeDataString(search)}")}";
        var cards = await server.GetAsync<List<KnowledgeCard>>(path, ct);
        if (cards.Count == 0) { Console.WriteLine(string.IsNullOrWhiteSpace(search) ? "No knowledge cards banked yet." : $"No card matches '{search}'."); return 0; }
        foreach (var c in cards)
        {
            Console.WriteLine($"## card {c.CardId}: {c.Title}  [{c.Status}; applies to: {c.AppliesTo}{(c.SourceDevice is null ? "" : $"; found on {c.SourceDevice}")}, {c.CreatedUtc:yyyy-MM-dd}]");
            Console.WriteLine($"- Symptom: {c.Symptom}");
            if (c.Cause is { Length: > 0 }) Console.WriteLine($"- Cause: {c.Cause}");
            if (c.Check is { Length: > 0 }) Console.WriteLine($"- How to check: {c.Check}");
            if (c.Fix is { Length: > 0 }) Console.WriteLine($"- Fix: {c.Fix}");
            if (c.Tags is { Length: > 0 }) Console.WriteLine($"- Tags: {c.Tags}");
            Console.WriteLine();
        }
        Console.WriteLine($"{cards.Count} card(s)");
        return 0;
    }

    private static readonly JsonSerializerOptions Indented = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Every machine NetworkOps knows: the PCs and the rack (servers, storage, UPSes, backup, network).</summary>
    internal sealed record Known(IReadOnlyList<DeviceRow> Pcs, IReadOnlyList<DeviceRow> Rack, FleetSnapshot Fleet, FleetSnapshot RackSnapshot);

    private static async Task<Known> KnownAsync(AppServer server, CancellationToken ct)
    {
        var fleet = await server.GetAsync<FleetSnapshot>("/api/fleet", ct);
        var rack = await server.GetAsync<FleetSnapshot>("/api/rack", ct);
        return new Known(fleet.Devices, rack.Devices, fleet, rack);
    }

    private static async Task<int> FindingsAsync(AppServer server, string? hostsArg, CancellationToken ct)
    {
        var known = await KnownAsync(server, ct);
        IReadOnlySet<string>? only = null;
        if (hostsArg is not null)
        {
            only = HostNames.Resolve(known.Pcs, known.Rack, hostsArg, out var unknown).Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var u in unknown) Console.Error.WriteLine($"netops: NetworkOps does not know {u}");
        }
        // The PCs AND the rack: until 2026-10-02 this listed the PCs only, so a server's finding never showed here.
        var rows = known.Fleet.OpenFindings.Concat(known.RackSnapshot.OpenFindings)
            .Where(f => only is null || only.Contains(f.Device)).OrderBy(f => f.RuleKey).ThenBy(f => f.Device).ToList();
        Console.WriteLine(JsonSerializer.Serialize(new { devices = known.Pcs.Concat(known.Rack), findings = rows }, new JsonSerializerOptions { WriteIndented = true }));
        Console.Error.WriteLine($"{rows.Count} open finding(s) on {rows.Select(f => f.Device).Distinct(StringComparer.OrdinalIgnoreCase).Count()} device(s)");
        return 0;
    }

    private static async Task<int> HistoryAsync(AppServer server, IReadOnlyList<DeviceRow> hosts, CancellationToken ct)
    {
        foreach (var d in hosts)
        {
            Console.WriteLine($"=== {d.Name}");
            Console.WriteLine(JsonSerializer.Serialize(await server.GetAsync<DeviceHistory>($"/api/devices/{d.DeviceId}/history", ct), Indented));
        }
        return 0;
    }

    private static async Task<int> ReadingsAsync(AppServer server, IReadOnlyList<DeviceRow> hosts, CancellationToken ct)
    {
        foreach (var d in hosts)
        {
            Console.WriteLine($"=== {d.Name}");
            Console.WriteLine(JsonSerializer.Serialize(await server.GetAsync<List<Kor.Operations.NetworkOps.Core.Rack.DeviceReading>>($"/api/devices/{d.DeviceId}/readings", ct), Indented));
        }
        return 0;
    }

    private static async Task<int> UpdatesAsync(AppServer server, bool scan, CancellationToken ct)
    {
        if (scan)
        {
            // The Updates view's "search again": the scheduled job, run now, on every machine; wait for it (up to 10 min).
            var trigger = (await server.PostAsync<Queued>("/api/updates/scan", new { }, ct)).TriggerId;
            Console.Error.WriteLine($"netops: update search queued on APP01 (trigger {trigger}); waiting...");
            var deadline = DateTime.UtcNow.AddMinutes(10);
            TriggerState t;
            do { await Task.Delay(TimeSpan.FromSeconds(10), ct); t = await server.GetAsync<TriggerState>($"/api/triggers/{trigger}", ct); }
            while (t.Status is "Pending" or "Running" && DateTime.UtcNow < deadline);
            Console.WriteLine($"search {t.Status}{(t.Result is null ? "" : $": {t.Result}")}");
        }
        var rows = await server.GetAsync<List<UpdateRow>>("/api/updates", ct);
        foreach (var r in rows.OrderByDescending(r => r.Due).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
            Console.WriteLine($"{r.Name,-34} {(r.Target ? (r.Pending.Count == 0 ? "nothing waiting" : $"{r.Pending.Count(p => p.Security)} security, {r.Pending.Count(p => !p.Security)} other") : $"not searched: {r.Why}"),-28}" +
                              $"{(r.RebootPending ? " restart pending" : "")}{(r.ScannedUtc is { } s ? $"  (searched {s.ToLocalTime():ddd HH:mm})" : "")}");
        Console.WriteLine($"{rows.Count(r => r.Pending.Count > 0)} of {rows.Count} machines have updates waiting; {rows.Count(r => r.RebootPending)} need a restart");
        return 0;
    }

    private static async Task<int> ChangesAsync(AppServer server, string? since, CancellationToken ct)
    {
        if (HostNames.ParseSince(since, DateTime.UtcNow) is not { } from) { Console.Error.WriteLine("--since takes 12h, 3d or a date/time (2026-10-02T01:45, local)."); return 2; }
        var view = await server.GetAsync<ChangesView>($"/api/changes?since={Uri.EscapeDataString(from.ToString("o", System.Globalization.CultureInfo.InvariantCulture))}", ct);
        Console.Write(view.Brief());
        return 0;
    }

    private sealed record QueuedAction(long ActionId);

    private sealed record ManualPcAnswer(int DeviceId, string Device, string Code, DateTime ExpiresUtc, string Command);

    // The Command Center's Fix…, from a session: the same endpoint, so the catalog, the "someone is using it" refusal and
    // the audit row are the service's, not this verb's. Waits for each run to finish (or --timeout), then prints it.
    private static async Task<int> FixAsync(AppServer server, IReadOnlyList<DeviceRow> hosts, string? fixId, string? param, string? finding, string? purpose, bool confirmed, int timeout, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fixId)) { Console.Error.WriteLine("--fix <id> is required (a FixCatalog id, e.g. repair-wmi)."); return 2; }
        var queued = new List<(string Host, long Id)>();
        var failed = 0;
        foreach (var dev in hosts)
        {
            try { queued.Add((dev.Name, (await server.PostAsync<QueuedAction>($"/api/devices/{dev.DeviceId}/fixes", new FixRequest(fixId, param, finding, purpose, confirmed), ct)).ActionId)); }
            catch (AppServerException ex) { Console.WriteLine($"=== {dev.Name}: refused -- {ex.Message}"); failed++; }
        }
        Console.Error.WriteLine($"netops: {queued.Count} fix run(s) queued on APP01 ({fixId}); waiting up to {timeout} s...");
        var deadline = DateTime.UtcNow.AddSeconds(timeout);
        var pending = queued.ToList();
        while (pending.Count > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            foreach (var q in pending.ToList())
            {
                var a = await server.GetAsync<ActionRow>($"/api/actions/{q.Id}", ct);
                if (a.Status is "Requested" or "Running") continue;
                pending.Remove(q);
                if (a.Status != "Done") failed++;
                Console.WriteLine($"=== {q.Host}: action {q.Id} {a.Status}{(a.Detail is null ? "" : $" -- {a.Detail}")}");
                if (a.Output is { Length: > 0 }) Console.WriteLine(a.Output.Length > 4000 ? a.Output[..4000] + " …" : a.Output);
            }
        }
        foreach (var q in pending) Console.WriteLine($"=== {q.Host}: action {q.Id} still running after {timeout} s; it carries on on APP01 (GET /api/actions/{q.Id})");
        return failed == 0 && pending.Count == 0 ? 0 : 1;
    }

    private static string Pretty(string json)
    {
        try { return JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement, new JsonSerializerOptions { WriteIndented = true }); }
        catch (JsonException) { return json; }
    }
}
