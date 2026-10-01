#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics;
using Kor.Operations.NetworkOps.Service.Store;
using Kor.Operations.NetworkOps.Transport;
using Microsoft.Extensions.Options;

namespace Kor.Operations.NetworkOps.Service.Agents;

// Install, upgrade or remove a PC's agent: requested like a fix (NetworkOps.Actions, kinds install-agent and
// remove-agent), so it is queued, audited and shown with everything else done to that PC. The agent files travel
// with the service (the "agent" folder beside the service's exe), so deploying the service is what makes a new
// agent version available, and installing again is the upgrade.
//
// One change per PC at a time (Codex audit 2026-09-30, finding 4): two installs racing would each write a key and
// save a hash, and the PC could end up running with the one the record does not hold.
internal sealed class AgentInstaller(NetworkOpsStore store, AgentHub hub, IOptions<NetworkOpsOptions> options)
{
    public const string InstallKind = "install-agent";
    public const string RemoveKind = "remove-agent";

    /// <summary>A freshly started agent calls in within a second or two; this is generous.</summary>
    public static readonly TimeSpan FirstCallWait = TimeSpan.FromSeconds(60);

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PerPc = new(StringComparer.OrdinalIgnoreCase);

    public static string PackageDir => Path.Combine(AppContext.BaseDirectory, "agent");

    public static string PackageVersion
        => FileVersionInfo.GetVersionInfo(Path.Combine(PackageDir, RemoteAgentInstall.ExeName)).ProductVersion?.Split('+')[0] ?? "unknown";

    /// <summary>The shipped version, or null when the package is missing or its version does not parse (a test host, a broken deploy).</summary>
    public static string? PackageVersionOrNull()
        => File.Exists(Path.Combine(PackageDir, RemoteAgentInstall.ExeName)) && PackageVersion is var v && Version.TryParse(v, out _) ? v : null;

    public static bool IsAgentKind(string kind) => kind is InstallKind or RemoveKind;

    /// <param name="fromRollout">A rollout picked this PC earlier: re-check, under this PC's lock, that its agent was not
    /// removed meanwhile (removal is a decision only that PC's own window undoes).</param>
    /// <returns>(ok, what happened) for the action's Detail.</returns>
    public async Task<(bool Ok, string Detail)> RunAsync(string kind, int deviceId, string device, string requestedBy, CancellationToken ct, bool fromRollout = false)
    {
        var gate = PerPc.GetOrAdd(device, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (kind == RemoveKind) return await RemoveAsync(deviceId, device, ct).ConfigureAwait(false);
            if (fromRollout && await store.AgentCredentialAsync(device, ct).ConfigureAwait(false) is { Removed: true })
                return (true, "skipped: its agent was removed since the rollout picked it");
            return await InstallAsync(deviceId, device, requestedBy, ct).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private async Task<(bool, string)> RemoveAsync(int deviceId, string device, CancellationToken ct)
    {
        // Refused from now on -- the record AND any poll held open right now -- even if the remote delete below fails.
        await store.RemoveAgentAsync(deviceId, ct).ConfigureAwait(false);
        hub.Revoke(device, currentKeyHash: null);
        return (true, await RemoteAgentInstall.RemoveAsync(device, ct).ConfigureAwait(false));
    }

    private async Task<(bool, string)> InstallAsync(int deviceId, string device, string requestedBy, CancellationToken ct)
    {
        // Removing is always allowed (it is how you back out); installing is not while agents are switched off.
        if (!options.Value.AgentsEnabled)
            return (false, "agents are switched off on APP01 (AgentsEnabled = false): nothing was installed");
        var version = PackageVersionOrNull() ?? throw new InvalidOperationException($"the agent package at {PackageDir} is missing or has no readable version");
        var key = AgentApi.NewKey();
        var hash = AgentApi.Hash(key);
        var hashHex = Convert.ToHexString(hash);
        DateTime revokedAt = default;
        await store.MarkAgentInstallPendingAsync(deviceId, ct).ConfigureAwait(false);   // not confirmed until it calls in
        var done = await RemoteAgentInstall.InstallAsync(device, PackageDir, key, async () =>
        {
            await store.SaveAgentAsync(deviceId, hash, version, requestedBy, ct).ConfigureAwait(false);
            hub.Revoke(device, hashHex);   // from here this process accepts only the new key; polls held with the old one are cut
            revokedAt = DateTime.UtcNow;
        }, ct).ConfigureAwait(false);

        // Installed is not the goal; an agent that APP01 hears from WITH THE NEW KEY is (Codex audit, finding 5). An old
        // connection closing moves no clock that this looks at.
        var deadline = DateTime.UtcNow + FirstCallWait;
        while (DateTime.UtcNow < deadline)
        {
            if (hub.Status(device) is { Connected: true } s && s.KeyHash == hashHex && s.LastPollUtc >= revokedAt)
            {
                await store.TouchAgentAsync(deviceId, hash, s.Version, s.Address, DateTime.UtcNow, ct).ConfigureAwait(false);   // confirmed: the record says so
                return (true, $"agent {version} {done}; it called in with its new key from {s.Address}");
            }
            await Task.Delay(1000, ct).ConfigureAwait(false);
        }
        return (false, $"agent {version} {done}, but it has not called in with its new key after {FirstCallWait.TotalSeconds:0} s: " +
                       @"its log is C:\Program Files\KorOperations\Agent\data\agent.log on the PC");
    }
}
