#nullable enable
using System.Text.Json;

namespace Kor.Operations.NetworkOps.Core.Power;

// What NetworkOps says to an ESXi host over SSH, as text: kept here (pure) so every command and every
// parse is tested against captured output, and the transport only moves bytes.
public static class EsxiCommands
{
    /// <summary>One line per registered VM: id|name|power state. vim-cmd only, as root on the host.</summary>
    public const string Inventory =
        "for id in $(vim-cmd vmsvc/getallvms | awk 'NR>1 && $1 ~ /^[0-9]+$/ {print $1}'); do " +
        "n=$(vim-cmd vmsvc/get.summary $id | grep -m1 ' name = ' | cut -d'\"' -f2); " +
        "p=$(vim-cmd vmsvc/power.getstate $id | tail -1); echo \"$id|$n|$p\"; done";

    public static string GuestShutdown(int vmId) => $"vim-cmd vmsvc/power.shutdown {vmId}";
    public static string PowerState(int vmId) => $"vim-cmd vmsvc/power.getstate {vmId} | tail -1";
    public static string PowerOff(int vmId) => $"vim-cmd vmsvc/power.off {vmId}";

    public static IReadOnlyList<VmOnHost> ParseInventory(string host, string output)
    {
        var list = new List<VmOnHost>();
        foreach (var raw in output.Split('\n'))
        {
            var parts = raw.Trim().Split('|');
            if (parts.Length != 3 || !int.TryParse(parts[0], out var id) || parts[1].Length == 0) continue;
            list.Add(new VmOnHost(host, id, parts[1], IsOn(parts[2])));
        }
        return list;
    }

    public static bool IsOn(string powerState) => powerState.Trim().Equals("Powered on", StringComparison.OrdinalIgnoreCase);
}

/// <summary>The chain's last step, as it is put on a host: the embedded script, its JSON config, and the
/// two commands that land them (the config through stdin, so no password is ever on a command line).</summary>
public static class HostScript
{
    public const string ScriptPath = "/tmp/kor-host-final.py";
    public const string ConfigPath = "/tmp/kor-chain.json";
    public const string LogPath = "/tmp/kor-chain.log";
    public const string DoneMarker = "KOR-CHAIN-DONE";

    public static string Text
    {
        get
        {
            using var s = typeof(HostScript).Assembly.GetManifestResourceStream("Power.host-final.py")
                ?? throw new InvalidOperationException("the embedded host script is missing");
            using var r = new StreamReader(s);
            return r.ReadToEnd();
        }
    }

    /// <summary>Writes stdin to <paramref name="path"/>, created 0600 (root only) -- ESXi has no base64 and no umask command.</summary>
    public static string WriteStdinTo(string path)
        => $"python -c \"import os,sys;fd=os.open('{path}',os.O_WRONLY|os.O_CREAT|os.O_TRUNC,0o600);os.write(fd,sys.stdin.buffer.read());os.close(fd)\"";

    /// <summary>Starts the script; it detaches itself (double fork), so this returns at once and the script outlives the session.</summary>
    public const string Launch = "python " + ScriptPath + " " + ConfigPath;

    public const string ReadLog = "cat " + LogPath + " 2>/dev/null";

    /// <summary>Clears the previous run's log so a read-back shows this run only.</summary>
    public const string ClearLog = ": > " + LogPath;

    public static string Config(bool dryRun, string? vm, int timeoutSeconds, bool powerOffHost,
        IReadOnlyList<(StorageTarget Target, string Password)> storage)
        => JsonSerializer.Serialize(new
        {
            dryRun,
            vm,
            timeoutSeconds,
            powerOffHost,
            storage = storage.Select(s => new
            {
                name = s.Target.Name,
                address = s.Target.Address,
                account = s.Target.Account,
                password = s.Password,
                dualController = s.Target.DualController,
            }),
        });
}
