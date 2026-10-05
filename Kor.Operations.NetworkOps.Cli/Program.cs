#nullable enable
// netops -- the hands-on half of KOR NetworkOps (docs/KOR-NetworkOps-Design-2026-09-28.md, Phase 1).
//
//   netops census   [--hosts A,B | all]                      which channel answers on which machine
//   netops run      --script x.ps1 --hosts A,B [--timeout 90]  THROUGH APP01 (SessionVerbs.cs): what a Claude session uses
//   netops check | last-check --hosts A,B | knowledge [--search w]   also through APP01
//   netops run --direct --script probe.ps1 [--hosts A,B | all] [--timeout 600] [--out dir] [--repeat n]   this PC -> the machine
//   netops hardware [--hosts A,B | all]                      CPU, board, DIMM slots, GPU, disks
//   netops health   [--hosts A,B | all] [--out dir]          the health probe + rules: findings per machine
//   netops watchdog [--dry-run | --test] ...                 the dead-man switch (WatchdogVerb.cs), run on KOR-FS01
//
// Every verb does its reading ON the target (one service call + one small file over the VPN),
// never a chatty remote walk: Ian is on the VPN almost all the time, and a remote registry /s
// took 5+ minutes per machine where the same read on the machine takes under a second.
// "all" = enabled, non-server computer accounts in Active Directory.
//
// Read-only except `run`, which does whatever its script does. Exit codes: 0 ok, 1 some hosts
// failed, 2 bad arguments.
using System.Text.Json;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Probes;
using Kor.Operations.NetworkOps.Core.Smbios;
using Kor.Operations.NetworkOps.Transport;

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine("netops watchdog [--dry-run|--test] [--to addr] [--silent-min 15] [--remind-min 60]");
    Console.WriteLine("Through NetworkOps on APP01 (what a Claude session uses; signs in like the app):");
    Console.WriteLine("  netops run --script x.ps1 --hosts A,B [--timeout 90] [--purpose \"why\"] [--run <prompt run>] [--out dir]");
    Console.WriteLine("  netops last-check --hosts A,B | netops check --hosts A,B | netops knowledge [--search words] [--all]");
    Console.WriteLine("From this PC straight to the machine (on the LAN):");
    Console.WriteLine("  netops census|hardware|health [--hosts A,B|all] [--timeout s] [--out dir] [--parallel n]");
    Console.WriteLine("  netops run --direct --script f.ps1 [--hosts A,B|all] [--timeout s] [--repeat n]");
    return 2;
}

var verb = args[0].ToLowerInvariant();

// The dead-man watcher touches no workstation and takes its own options.
if (verb == "watchdog")
    return await Kor.Operations.NetworkOps.Cli.WatchdogVerb.RunAsync(args);

// A stdio MCP server Claude Code launches, proxied to the real /mcp on APP01 (signed in as the person, cert pinned):
// the NetworkOps tools with nothing to set up. Long-running; speaks MCP on stdio, so no other output here.
if (verb == "mcp")
    return await Kor.Operations.NetworkOps.Cli.McpBridge.RunAsync();

// What a Claude session uses: everything through NetworkOps on APP01, never from this PC to the machine (SessionVerbs.cs).
if (Kor.Operations.NetworkOps.Cli.SessionVerbs.Handles(verb, args))
    return await Kor.Operations.NetworkOps.Cli.SessionVerbs.RunAsync(verb, args);

string? hostsArg = null, script = null, outDir = null;
var timeout = 600;
var parallel = 16;
var repeat = 1;
for (var i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--hosts" when i + 1 < args.Length: hostsArg = args[++i]; break;
        case "--script" when i + 1 < args.Length: script = args[++i]; break;
        case "--out" when i + 1 < args.Length: outDir = args[++i]; break;
        case "--timeout" when i + 1 < args.Length && int.TryParse(args[i + 1], out var t): timeout = t; i++; break;
        case "--parallel" when i + 1 < args.Length && int.TryParse(args[i + 1], out var p): parallel = p; i++; break;
        case "--repeat" when i + 1 < args.Length && int.TryParse(args[i + 1], out var n) && n >= 1: repeat = n; i++; break;
        case "--direct": break;   // `run --direct`: this PC straight to the machine (the route below)
        default: Console.Error.WriteLine($"Unknown argument: {args[i]}"); return 2;
    }
}

var hosts = hostsArg is null || hostsArg.Equals("all", StringComparison.OrdinalIgnoreCase)
    ? ActiveDirectoryFleet.Workstations()
    : hostsArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
if (hosts.Count == 0) { Console.Error.WriteLine("No hosts."); return 2; }

var options = new ParallelOptions { MaxDegreeOfParallelism = parallel };

switch (verb)
{
    case "census":
    {
        var rows = new System.Collections.Concurrent.ConcurrentBag<CensusRow>();
        await Parallel.ForEachAsync(hosts, options, async (h, ct) => rows.Add(await ChannelCensus.ProbeAsync(h, ct)));
        Console.WriteLine($"{"Host",-16} {"Answered on",-15} {"c$",-3} {"wr",-3} {"SCM",-4} {"RemoteReg",-10} {"WinRM",-6} {"RDP",-4} Resolved");
        foreach (var r in rows.OrderBy(r => r.Host, StringComparer.OrdinalIgnoreCase))
            Console.WriteLine($"{r.Host,-16} {r.AnsweredOn ?? "-",-15} {Yn(r.AdminShare),-3} {Yn(r.AdminWrite),-3} {Yn(r.ServiceControl),-4} {r.RemoteRegistry ?? "-",-10} {Yn(r.WinRm),-6} {Yn(r.Rdp),-4} {string.Join(",", r.Resolved)}");
        var reached = rows.Count(r => r.Reachable);
        Console.WriteLine();
        Console.WriteLine($"reachable {reached} of {rows.Count} | run-on-target ready (c$ write + SCM) {rows.Count(r => r.AdminWrite && r.ServiceControl)} of {rows.Count} | WinRM open {rows.Count(r => r.WinRm)} of {rows.Count} | dual-homed {rows.Count(r => r.Resolved.Count > 1)} of {rows.Count}");
        WriteJson(outDir, "census", rows.OrderBy(r => r.Host));
        return reached == rows.Count ? 0 : 1;
    }

    case "run":
    {
        if (script is null || !File.Exists(script)) { Console.Error.WriteLine("--script <file.ps1> is required and must exist."); return 2; }
        var body = await File.ReadAllTextAsync(script);
        // --repeat runs the script again in the same process: the later passes show what "check now"
        // costs once the SCM connection to each machine is already open.
        var runs = new List<OnTargetRun>();
        for (var pass = 0; pass < repeat; pass++) runs.AddRange(await RunEverywhere(hosts, body, timeout, options));
        foreach (var r in runs)
            Console.WriteLine($"=== {r.Computer} [{r.Status}] {r.TotalMs} ms ({r.Stages}) {r.Error}{Environment.NewLine}{(r.OutputJson is null ? "" : Pretty(r.OutputJson))}");
        Summarise(runs);
        WriteJson(outDir, "run", runs);
        return runs.All(r => r.Status == OnTargetStatus.Ok) ? 0 : 1;
    }

    case "hardware":
    {
        var runs = await RunEverywhere(hosts, ProbeLibrary.Get(ProbeLibrary.Hardware), timeout, options);
        var profiles = new List<object>();
        foreach (var r in runs)
        {
            if (r.Status != OnTargetStatus.Ok) { Console.WriteLine($"{r.Computer,-16} [{r.Status}] {r.Error}"); continue; }
            var o = JsonDocument.Parse(r.OutputJson!).RootElement[0];
            SmbiosInfo? s = null;
            if (o.TryGetProperty("SmbiosBase64", out var b) && b.GetString() is { } b64) s = SmbiosParser.Parse(Convert.FromBase64String(b64));
            var gpus = o.TryGetProperty("Gpus", out var g) && g.ValueKind == JsonValueKind.Array ? string.Join("; ", g.EnumerateArray().Select(x => x.GetProperty("Name").GetString())) : "";
            var m = s?.Memory;
            Console.WriteLine($"{r.Computer,-16} {o.GetProperty("Cpu").GetString()} | {s?.Board?.Manufacturer} {s?.Board?.Product} | {s?.Chassis?.Type}");
            Console.WriteLine($"{"",-16} RAM {m?.InstalledGB} GB in {m?.SlotsPopulated} of {m?.Slots} slots ({m?.SlotsFree} free, max {m?.MaxCapacityGB} GB) | GPU {gpus}");
            Console.WriteLine($"{"",-16} {o.GetProperty("Os").GetString()} | C: {o.GetProperty("SystemDriveFreeGB")} of {o.GetProperty("SystemDriveGB")} GB free | SecureBoot {o.GetProperty("SecureBoot")}");
            profiles.Add(new { r.Computer, Smbios = s, Raw = JsonDocument.Parse(r.OutputJson!).RootElement[0] });
        }
        Summarise(runs);
        WriteJson(outDir, "hardware", profiles);
        return runs.All(r => r.Status == OnTargetStatus.Ok) ? 0 : 1;
    }

    case "health":
    {
        var runs = await RunEverywhere(hosts, ProbeLibrary.Get(ProbeLibrary.Health), timeout, options);
        var report = new List<object>();
        var byRule = new Dictionary<string, List<string>>();
        foreach (var r in runs)
        {
            if (r.Status != OnTargetStatus.Ok) { Console.WriteLine($"{r.Computer,-16} [{r.Status}] {r.Error}"); continue; }
            HealthSnapshot snap;
            try { snap = HealthSnapshot.Parse(r.OutputJson!); }
            catch (JsonException ex) { Console.WriteLine($"{r.Computer,-16} [Unreadable] {ex.Message}"); continue; }   // one bad machine never sinks the fleet report
            var findings = HealthRules.Evaluate(snap).OrderByDescending(x => x.Severity).ToList();
            Console.WriteLine(findings.Count == 0 ? $"{r.Computer,-16} healthy" : $"{r.Computer,-16} {findings.Count} finding(s)");
            foreach (var x in findings)
            {
                Console.WriteLine($"{"",-16}   {x.Severity,-8} {x.Title} -- {x.Evidence}");
                var family = x.RuleKey.Split(':')[0];
                (byRule.TryGetValue(family, out var list) ? list : byRule[family] = new List<string>()).Add(r.Computer);
            }
            report.Add(new { r.Computer, Findings = findings, Snapshot = snap });
        }
        var ok = runs.Count(r => r.Status == OnTargetStatus.Ok);
        Console.WriteLine();
        Console.WriteLine($"Across the {ok} machines that answered:");
        foreach (var (rule, machines) in byRule.OrderByDescending(kv => kv.Value.Distinct().Count()))
            Console.WriteLine($"  {rule,-28} {machines.Distinct().Count(),3} of {ok}  {string.Join(", ", machines.Distinct())}");
        Summarise(runs);
        WriteJson(outDir, "health", report);
        return runs.All(r => r.Status == OnTargetStatus.Ok) ? 0 : 1;
    }

    default:
        Console.Error.WriteLine($"Unknown verb '{verb}'. Use census, run, hardware or health.");
        return 2;
}

static async Task<List<OnTargetRun>> RunEverywhere(IReadOnlyList<string> hosts, string body, int timeoutSeconds, ParallelOptions options)
{
    var channel = new OnTargetChannel(TimeSpan.FromSeconds(timeoutSeconds));
    var bag = new System.Collections.Concurrent.ConcurrentBag<OnTargetRun>();
    await Parallel.ForEachAsync(hosts, options, async (h, ct) => bag.Add(await channel.RunAsync(h, body, ct)));
    await OnTargetChannel.DrainAsync();   // the one-shot services are deleted as the SCM lets go; never exit with any left behind
    return bag.OrderBy(r => r.Computer, StringComparer.OrdinalIgnoreCase).ToList();
}

static void Summarise(IReadOnlyCollection<OnTargetRun> runs)
{
    Console.WriteLine();
    Console.WriteLine(string.Join(" | ", runs.GroupBy(r => r.Status).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()} of {runs.Count}")));
}

static string Yn(bool b) => b ? "yes" : "no";

static string Pretty(string json) => JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement, new JsonSerializerOptions { WriteIndented = true });

static void WriteJson(string? dir, string name, object data)
{
    if (dir is null) return;
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, $"netops-{name}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
    File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"wrote {path}");
}
