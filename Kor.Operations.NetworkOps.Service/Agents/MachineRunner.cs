#nullable enable
using Kor.Operations.NetworkOps.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Operations.NetworkOps.Service.Agents;

// The one way the service runs a script on a PC. Through the PC's agent when it has one and it is connected;
// otherwise -- or if the agent does not take the job, or agents are switched off (AgentsEnabled, the kill
// switch) -- through the one-shot service over the network, exactly as before the agent existed. Same script,
// same result shape, so the sweep and the fix runner cannot tell, and a PC without an agent loses nothing.
internal sealed class MachineRunner(AgentHub hub, IOptions<NetworkOpsOptions> options, ILogger<MachineRunner> log)
{
    /// <summary>
    /// Jobs running on one PC at once, by either route (Codex audit 2026-09-30, finding 10): a health check and a couple
    /// of fixes fit; a pile of repeated requests waits its turn instead of starting a dozen SYSTEM PowerShells. The agent
    /// has its own, larger, hard limit behind this one.
    /// </summary>
    public const int PerPcLimit = 3;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> Slots = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="wantsIdle">Ask the agent for the console user's idle time (the health probe reports it).</param>
    public async Task<OnTargetRun> RunAsync(string host, string body, TimeSpan timeout, bool wantsIdle, CancellationToken ct)
    {
        var slot = Slots.GetOrAdd(host, _ => new SemaphoreSlim(PerPcLimit, PerPcLimit));
        if (!await slot.WaitAsync(timeout, ct).ConfigureAwait(false))
            return OnTargetRun.Failed(host, OnTargetStatus.Timeout, $"{PerPcLimit} jobs were already running on {host} for the whole {timeout.TotalSeconds:0} s");
        try
        {
            if (options.Value.AgentsEnabled && hub.IsConnected(host))
            {
                if (await hub.RunAsync(host, body, timeout, wantsIdle, ct).ConfigureAwait(false) is { } run) return run;
                log.LogWarning("Agent on {Host} did not take a job within {Seconds:0} s: using the network route", host, AgentHub.PickupWait.TotalSeconds);
            }
            return await new OnTargetChannel(timeout).RunAsync(host, body, ct).ConfigureAwait(false);
        }
        finally { slot.Release(); }
    }

    /// <summary>Whether this check of <paramref name="host"/> would go through its agent right now.</summary>
    public bool ViaAgent(string host) => options.Value.AgentsEnabled && hub.IsConnected(host);
}
