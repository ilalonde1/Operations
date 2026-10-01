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
    /// <param name="wantsIdle">Ask the agent for the console user's idle time (the health probe reports it).</param>
    public async Task<OnTargetRun> RunAsync(string host, string body, TimeSpan timeout, bool wantsIdle, CancellationToken ct)
    {
        if (options.Value.AgentsEnabled && hub.IsConnected(host))
        {
            if (await hub.RunAsync(host, body, timeout, wantsIdle, ct).ConfigureAwait(false) is { } run) return run;
            log.LogWarning("Agent on {Host} did not take a job within {Seconds:0} s: using the network route", host, AgentHub.PickupWait.TotalSeconds);
        }
        return await new OnTargetChannel(timeout).RunAsync(host, body, ct).ConfigureAwait(false);
    }

    /// <summary>Whether this check of <paramref name="host"/> would go through its agent right now.</summary>
    public bool ViaAgent(string host) => options.Value.AgentsEnabled && hub.IsConnected(host);
}
