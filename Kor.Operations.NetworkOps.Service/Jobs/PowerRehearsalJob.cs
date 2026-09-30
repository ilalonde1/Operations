#nullable enable
using Kor.Operations.NetworkOps.Service.Power;
using Microsoft.Extensions.Options;

namespace Kor.Operations.NetworkOps.Service.Jobs;

// Daily: the whole shutdown chain as a DRY RUN against the live rack. It is the check that the chain
// still matches the rack -- a VM added, renamed or moved; a host key changed; a Synology password
// rotated; the final script no longer landing -- found at 06:45 with the lights on, not at the moment
// the batteries start draining. Any problem THROWS, so the dispatcher records a failed run and mails it.
// Also runnable on demand from the Command Center ("rehearse now").
internal sealed class PowerRehearsalJob(PowerChainRunner chain, IOptions<NetworkOpsOptions> options) : INetworkOpsJob
{
    public const string JobName = "PowerRehearsal";
    public string Name => JobName;

    public async Task<string> RunAsync(CancellationToken ct)
    {
        var o = options.Value;
        if (o.PowerChain.Hosts.Count == 0 || string.IsNullOrWhiteSpace(o.EsxiKeyPath))
            return "not configured (PowerChain.Hosts or EsxiKeyPath empty)";
        var outcome = await chain.RunAsync(dryRun: true, "daily rehearsal", ct).ConfigureAwait(false);
        if (!outcome.Ok) throw new InvalidOperationException("the shutdown chain would NOT run cleanly: " + outcome.Summary);
        return outcome.Summary + (o.PowerChainArmed ? " (chain ARMED)" : " (chain not armed: a real outage runs it as a dry run)");
    }
}
