// Kor.Operations.NetworkOps.Agent -- the NetworkOps endpoint agent (docs/KOR-NetworkOps-Design-2026-09-28.md 4.2).
//
// It is a transport, not a second brain. It holds one HTTPS request open to the NetworkOps service on APP01;
// when the service has work for this PC -- the health probe, a fix -- the answer to that request IS the
// work: a PowerShell script, exactly the one the service would otherwise have run over the network through a
// one-shot service. The agent runs it here as SYSTEM and posts the result back. Every rule, every finding and
// every decision stays on APP01, so an agent can never disagree with the service about anything.
//
// What it adds over the network route: nothing opens on the PC (the connection is outbound), a laptop that
// was off at sweep time is checked the moment it is back, and it can read how long the person at the
// keyboard has been idle, which only a process in their session can see.
//
//   Kor.Operations.NetworkOps.Agent.exe                          as the Windows service (installed by APP01)
//   Kor.Operations.NetworkOps.Agent.exe --console [overrides]    in a console, for tests
//   Kor.Operations.NetworkOps.Agent.exe --idle                   exit with this session's idle seconds (the service
//                                                                 starts it in the signed-in user's session)
//   Kor.Operations.NetworkOps.Agent.exe --enrol CODE             a PC outside the domain installs itself (Enrol.cs)
using System;
using System.Linq;
using System.ServiceProcess;
using System.Threading;

namespace Kor.Operations.NetworkOps.Agent;

internal static class Program
{
    public const string ServiceName = "KorNetworkOpsAgent";

    public static int Main(string[] args)
    {
        // Started by the agent in the console user's session: the exit code IS the answer (ConsoleIdle).
        if (args.Length > 0 && args[0] == "--idle") return ConsoleIdle.ForThisSession();

        // A PC outside the domain installs itself with a one-time code (Enrol.cs).
        if (args.Length > 1 && args[0] == "--enrol") return Enrol.Run(AgentSettings.Load([]), args[1]);

        var settings = AgentSettings.Load(args);
        // Before anything can start a child: everything the agent ever starts is contained, and dies with it.
        ProcessTree.ContainSelf();
        if (!args.Contains("--console"))
        {
            ServiceBase.Run(new AgentService(settings));
            return 0;
        }

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        var log = new AgentLog(settings.DataDir, echo: true);
        try { new AgentLoop(settings, log).RunAsync(stop.Token).GetAwaiter().GetResult(); }
        catch (Exception ex) { log.Error("agent stopped: " + ex); return 1; }
        return 0;
    }
}

internal sealed class AgentService : ServiceBase
{
    private readonly AgentSettings _settings;
    private readonly CancellationTokenSource _stop = new();
    private System.Threading.Tasks.Task? _loop;
    private AgentLog? _log;

    public AgentService(AgentSettings settings)
    {
        _settings = settings;
        ServiceName = Program.ServiceName;
        CanStop = true;
        CanShutdown = true;
    }

    protected override void OnStart(string[] args)
    {
        DataFolder.Secure(_settings.DataDir);
        _log = new AgentLog(_settings.DataDir, echo: false);
        var log = _log;
        _loop = System.Threading.Tasks.Task.Run(async () =>
        {
            try { await new AgentLoop(_settings, log).RunAsync(_stop.Token).ConfigureAwait(false); }
            catch (Exception ex) { log.Error("agent loop ended: " + ex); }
        });
    }

    protected override void OnStop() => Stop(TimeSpan.FromSeconds(20));

    protected override void OnShutdown() => Stop(TimeSpan.FromSeconds(5));

    private void Stop(TimeSpan wait)
    {
        _stop.Cancel();
        try { _loop?.Wait(wait); } catch (AggregateException) { /* the loop logs its own end */ }
        _log?.Info("agent stopped");
    }
}
