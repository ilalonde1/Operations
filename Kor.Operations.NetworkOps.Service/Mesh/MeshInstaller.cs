#nullable enable
using Kor.Operations.NetworkOps.Service.Agents;
using Kor.Operations.NetworkOps.Transport;
using Microsoft.Extensions.Options;

namespace Kor.Operations.NetworkOps.Service.Mesh;

// Install remote control on a PC or a rack server: an action like any fix (NetworkOps.Actions, kind install-mesh),
// queued, audited, and run on the machine through its NetworkOps agent or the network route (MachineRunner). Done only
// when MeshCentral itself lists the device as connected -- installed is not the goal; reachable is.
internal sealed class MeshInstaller(MachineRunner runner, MeshSweepJob sweep, MeshState state, IOptions<NetworkOpsOptions> options)
{
    public const string InstallKind = "install-mesh";
    public static readonly TimeSpan ConfirmWait = TimeSpan.FromSeconds(90);

    /// <summary>The script, with this deployment's server, group and certificate pin put in.</summary>
    public static string Script(NetworkOpsOptions o, bool server)
    {
        using var s = typeof(MeshInstaller).Assembly.GetManifestResourceStream("Kor.Operations.NetworkOps.Service.Mesh.install-mesh.ps1")
                      ?? throw new InvalidOperationException("install-mesh.ps1 is not embedded");
        var template = new StreamReader(s).ReadToEnd();
        var group = server ? o.MeshServerGroup : o.MeshPcGroup;
        if (group.Length == 0) throw new InvalidOperationException($"no Mesh device group configured for {(server ? "servers" : "PCs")}");
        var meshId = Uri.EscapeDataString(group.StartsWith("mesh//", StringComparison.Ordinal) ? group["mesh//".Length..] : group);
        return template.Replace("{{URL}}", o.MeshUrl.TrimEnd('/')).Replace("{{MESHID}}", meshId)
                       .Replace("{{CERTSHA256}}", o.MeshCertSha256.Replace(":", "").ToUpperInvariant());
    }

    /// <param name="host">What to run on: the PC's name, or a rack server's Address.</param>
    public async Task<(bool Ok, string Detail)> RunAsync(int deviceId, string host, bool server, CancellationToken ct)
    {
        var o = options.Value;
        if (!o.MeshEnabled) return (false, "remote control is not configured on APP01 (MeshUrl / MeshCertSha256 / MeshUser / KOR_NETWORKOPS_MESHPASSWORD)");
        var run = await runner.RunAsync(host, Script(o, server), TimeSpan.FromMinutes(5), wantsIdle: false, ct).ConfigureAwait(false);
        if (run.Status != OnTargetStatus.Ok) return (false, $"{run.Status}: {run.Error}");
        var said = Sweep.ActionRunner.ResultLine(run.OutputJson) ?? "ran";

        var deadline = DateTime.UtcNow + ConfirmWait;
        while (DateTime.UtcNow < deadline)
        {
            await sweep.RunAsync(ct).ConfigureAwait(false);
            if (state.For(deviceId) is { Node.AgentConnected: true } link)
                return (true, $"{said}; MeshCentral lists it as {link.Node.Name} in {link.Group}, connected");
            await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        }
        return (false, $"{said}; but MeshCentral has not listed it as connected after {ConfirmWait.TotalSeconds:0} s");
    }
}
