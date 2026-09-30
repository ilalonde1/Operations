#nullable enable
using System.Text;
using Renci.SshNet;

namespace Kor.Operations.NetworkOps.Transport;

// SSH to an ESXi host as root, by KEY (no host password is held anywhere), with the host key PINNED:
// a machine that is not the host we know is refused before authentication. The commands themselves
// live in Core (EsxiCommands, HostScript); this only moves bytes.
public sealed class EsxiShell : IDisposable
{
    private readonly SshClient _ssh;

    private EsxiShell(SshClient ssh) => _ssh = ssh;

    /// <param name="pinnedSha256">Accepted host-key fingerprints, "SHA256:..." as ssh-keygen -lf prints them (padding optional).</param>
    public static EsxiShell Connect(string host, string keyPath, IReadOnlyCollection<string> pinnedSha256, TimeSpan timeout)
    {
        if (!File.Exists(keyPath)) throw new InvalidOperationException($"ESXi key not found at '{keyPath}'");
        var pins = pinnedSha256.Select(Normalise).ToHashSet(StringComparer.Ordinal);
        if (pins.Count == 0) throw new InvalidOperationException($"no host key is pinned for {host}: refusing to connect to whatever answers");

        var key = new PrivateKeyFile(keyPath);
        var ssh = new SshClient(new ConnectionInfo(host, "root", new PrivateKeyAuthenticationMethod("root", key)) { Timeout = timeout });
        string? seen = null;
        ssh.HostKeyReceived += (_, e) => { seen = Normalise(e.FingerPrintSHA256); e.CanTrust = pins.Contains(seen); };
        try { ssh.Connect(); }
        catch (Renci.SshNet.Common.SshConnectionException) when (seen is not null && !pins.Contains(seen))
        {
            ssh.Dispose();
            throw new InvalidOperationException($"{host} presented host key SHA256:{seen}, which is not pinned: refused");
        }
        return new EsxiShell(ssh);
    }

    public async Task<(int Exit, string Output, string Error)> RunAsync(string command, TimeSpan timeout, CancellationToken ct)
    {
        using var cmd = _ssh.CreateCommand(command);
        cmd.CommandTimeout = timeout;
        await cmd.ExecuteAsync(ct).ConfigureAwait(false);
        return (cmd.ExitStatus ?? -1, cmd.Result, cmd.Error);
    }

    /// <summary>Runs <paramref name="command"/> with <paramref name="stdin"/> as its input (how secrets reach the host: never argv).</summary>
    public async Task<(int Exit, string Output, string Error)> RunWithInputAsync(string command, string stdin, TimeSpan timeout, CancellationToken ct)
    {
        using var cmd = _ssh.CreateCommand(command);
        cmd.CommandTimeout = timeout;
        var run = cmd.ExecuteAsync(ct);
        using (var input = cmd.CreateInputStream())
            await input.WriteAsync(Encoding.UTF8.GetBytes(stdin), ct).ConfigureAwait(false);
        await run.ConfigureAwait(false);
        return (cmd.ExitStatus ?? -1, cmd.Result, cmd.Error);
    }

    public void Dispose()
    {
        if (_ssh.IsConnected) _ssh.Disconnect();
        _ssh.Dispose();
    }

    public static string Normalise(string s)
    {
        var v = s.Trim();
        if (v.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase)) v = v[7..];
        return v.TrimEnd('=');
    }
}
