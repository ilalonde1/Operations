#nullable enable
using System.Diagnostics;
using Kor.Operations.NetworkOps.Service.Store;
using Kor.Operations.NetworkOps.Transport;

namespace Kor.Operations.NetworkOps.Service.Agents;

// Install, upgrade or remove a PC's agent: requested like a fix (NetworkOps.Actions, kinds install-agent and
// remove-agent), so it is queued, audited and shown with everything else done to that PC. The agent files travel
// with the service (the "agent" folder beside the service's exe), so deploying the service is what makes a new
// agent version available, and installing again is the upgrade.
internal sealed class AgentInstaller(NetworkOpsStore store, AgentHub hub, Microsoft.Extensions.Options.IOptions<NetworkOpsOptions> options)
{
    public const string InstallKind = "install-agent";
    public const string RemoveKind = "remove-agent";

    /// <summary>A freshly started agent calls in within a second or two; this is generous.</summary>
    public static readonly TimeSpan FirstCallWait = TimeSpan.FromSeconds(60);

    public static string PackageDir => Path.Combine(AppContext.BaseDirectory, "agent");

    public static string PackageVersion
        => FileVersionInfo.GetVersionInfo(Path.Combine(PackageDir, RemoteAgentInstall.ExeName)).ProductVersion?.Split('+')[0] ?? "unknown";

    /// <summary>The shipped version, or null when the package is missing (a test host, a broken deploy).</summary>
    public static string? PackageVersionOrNull() => File.Exists(Path.Combine(PackageDir, RemoteAgentInstall.ExeName)) ? PackageVersion : null;

    public static bool IsAgentKind(string kind) => kind is InstallKind or RemoveKind;

    /// <returns>(ok, what happened) for the action's Detail.</returns>
    public async Task<(bool Ok, string Detail)> RunAsync(string kind, int deviceId, string device, string requestedBy, CancellationToken ct)
    {
        if (kind == RemoveKind)
        {
            await store.RemoveAgentAsync(deviceId, ct).ConfigureAwait(false);   // refused from now on, even if the delete below fails
            return (true, await RemoteAgentInstall.RemoveAsync(device, ct).ConfigureAwait(false));
        }

        // Removing is always allowed (it is how you back out); installing is not while agents are switched off.
        if (!options.Value.AgentsEnabled)
            return (false, "agents are switched off on APP01 (AgentsEnabled = false): nothing was installed");
        var version = PackageVersion;
        var key = AgentApi.NewKey();
        var before = hub.Status(device)?.LastPollUtc;
        var done = await RemoteAgentInstall.InstallAsync(device, PackageDir, key,
            () => store.SaveAgentAsync(deviceId, AgentApi.Hash(key), version, requestedBy, ct), ct).ConfigureAwait(false);

        // Installed is not the goal; an agent that APP01 hears from is.
        var deadline = DateTime.UtcNow + FirstCallWait;
        while (DateTime.UtcNow < deadline)
        {
            if (hub.Status(device) is { } s && s.LastPollUtc != before && s.Connected)
                return (true, $"agent {version} {done}; it called in from {s.Address}");
            await Task.Delay(1000, ct).ConfigureAwait(false);
        }
        return (false, $"agent {version} {done}, but it has not called in after {FirstCallWait.TotalSeconds:0} s: " +
                       @"its log is C:\ProgramData\KorOperations\Agent\agent.log on the PC");
    }
}
