#nullable enable
using System.ComponentModel;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Kor.Operations.NetworkOps.Transport;

// Puts the endpoint agent on a PC, upgrades it, or takes it off -- from APP01, over the same two things the
// one-shot channel uses (c$ and the Service Control Manager), so it reaches every PC the sweep already reaches and
// needs no GPO, no installer package and nobody at the PC. Upgrading is installing again.
//
//   C:\Program Files\KorOperations\Agent\        the agent (under the Webroot folder override; admin-only by Windows)
//   C:\Program Files\KorOperations\Agent\data\   its key, its work folder, its log: SYSTEM and Administrators only
//
// Every install writes a NEW key: the caller records its hash before the agent starts, so the old key is dead the
// moment the new one exists and an old copy of agent.key is worth nothing.
public static class RemoteAgentInstall
{
    public const string ServiceName = "KorNetworkOpsAgent";
    public const string DisplayName = "KOR NetworkOps Agent";
    public const string ExeName = "Kor.Operations.NetworkOps.Agent.exe";
    public const string InstallDir = @"C:\Program Files\KorOperations\Agent";

    /// <summary>
    /// The key, work folder and log. Under Program Files, where only an administrator can create anything -- NOT
    /// ProgramData, where any user can pre-create the folder before the install and own it (Codex audit 2026-09-30,
    /// finding 1). The agent re-secures the whole tree at every start (Agent/DataFolder.cs).
    /// </summary>
    public const string DataDir = InstallDir + @"\data";

    // Agent 1.0.0 kept its data in a user-writable folder. It is NOT cleaned up from here, on purpose: any user can make
    // an ancestor of that path a junction to Program Files, and this code runs as an administrator (Codex re-check,
    // 2026-09-30). It existed only on KOR-104N and was removed there; nothing else ever had it.

    private const string Description = "Runs KOR NetworkOps health checks and approved fixes on this PC for the NetworkOps service on KOR-APP01. " +
                                       "Connects out to APP01 only; listens on nothing.";

    private static string Unc(string computer, string localPath) => $@"\\{computer}\{localPath[0]}$\{localPath[3..]}";

    /// <summary>
    /// Stops any running agent, copies the files in <paramref name="packageDir"/>, locks the data folder, writes
    /// <paramref name="key"/> as a NEW file, and starts it. Returns what was done; throws with the step that failed.
    /// Call <paramref name="beforeStart"/> to record the key's hash: the agent calls in the moment it starts.
    /// </summary>
    public static async Task<string> InstallAsync(string computer, string packageDir, string key, Func<Task> beforeStart, CancellationToken ct)
    {
        var files = Directory.GetFiles(packageDir);
        if (!files.Any(f => Path.GetFileName(f).Equals(ExeName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"the agent package at {packageDir} has no {ExeName}");
        var reach = await SmbReachability.ProbeAsync(computer, ct: ct).ConfigureAwait(false);
        if (!reach.Reachable) throw new InvalidOperationException($"{computer} is not answering on port 445");

        var upgraded = await StopIfPresentAsync(computer, ct).ConfigureAwait(false);

        var bin = Directory.CreateDirectory(Unc(computer, InstallDir));
        foreach (var f in files)
            await CopyWithRetryAsync(f, Path.Combine(bin.FullName, Path.GetFileName(f)), ct).ConfigureAwait(false);

        var data = new DirectoryInfo(Unc(computer, DataDir));
        if (data.Exists && data.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException($"{DataDir} on {computer} is a link, not a folder: refusing to put a key in it");
        if (data.Exists) data.SetAccessControl(LockedFolder());
        else data.Create(LockedFolder());   // created with its permissions, never open for a moment
        var keyFile = Path.Combine(data.FullName, "agent.key");
        if (File.Exists(keyFile)) File.Delete(keyFile);   // a new file inherits the locked folder; an old one keeps its own
        await File.WriteAllTextAsync(keyFile, key, ct).ConfigureAwait(false);

        await beforeStart().ConfigureAwait(false);

        var binPath = $"\"{Path.Combine(InstallDir, ExeName)}\"";
        await Task.Run(() => ServiceControlManager.WithPooledManager(computer, m =>
        {
            using var svc = ServiceControlManager.OpenForControl(m, ServiceName) ?? ServiceControlManager.CreateAutoStart(m, ServiceName, DisplayName, binPath);
            ServiceControlManager.Reconfigure(svc, DisplayName, binPath);
            ServiceControlManager.Describe(svc, Description);
            var err = ServiceControlManager.Start(svc);
            if (err != 0 && err != ServiceControlManager.ErrorServiceAlreadyRunning) throw new Win32Exception(err, "StartService failed");
            return 0;
        }), ct).ConfigureAwait(false);
        if (!await WaitForAsync(computer, ServiceState.Running, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false))
            throw new InvalidOperationException("the agent service did not reach Running within 30 s");

        return $"{(upgraded ? "upgraded" : "installed")} and running ({files.Length} files)";
    }

    /// <summary>Stops and deletes the service and removes both folders. Safe on a PC that never had it.</summary>
    public static async Task<string> RemoveAsync(string computer, CancellationToken ct)
    {
        var reach = await SmbReachability.ProbeAsync(computer, ct: ct).ConfigureAwait(false);
        if (!reach.Reachable) throw new InvalidOperationException($"{computer} is not answering on port 445");
        var had = await StopIfPresentAsync(computer, ct).ConfigureAwait(false);
        if (had)
        {
            var err = await Task.Run(() => ServiceControlManager.WithPooledManager(computer, m =>
            {
                using var svc = ServiceControlManager.OpenForControl(m, ServiceName);
                return svc is null ? 0 : ServiceControlManager.MarkForDelete(svc);
            }), ct).ConfigureAwait(false);
            // Checked, not assumed (Codex audit 2026-09-30, finding 7): a service left registered over deleted files
            // would look removed and be broken.
            if (err != 0 && err != ServiceControlManager.ErrorServiceMarkedForDelete) throw new Win32Exception(err, "DeleteService failed");
            var gone = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (await StateAsync(computer, ct).ConfigureAwait(false) is not null)
            {
                if (DateTime.UtcNow > gone) throw new InvalidOperationException("the agent service is still registered 30 s after it was deleted");
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
        }
        if (Directory.Exists(Unc(computer, InstallDir))) await DeleteWithRetryAsync(Unc(computer, InstallDir), ct).ConfigureAwait(false);
        return had ? "service removed, files deleted" : "there was no agent service; any files deleted";
    }

    /// <summary>The service's state on the PC, or null when it is not installed.</summary>
    public static Task<ServiceState?> StateAsync(string computer, CancellationToken ct)
        => Task.Run(() => ServiceControlManager.WithPooledManager(computer, m => ServiceControlManager.QueryState(m, ServiceName)), ct);

    /// <summary>Returns whether the service existed; when it did, it is Stopped and its process gone when this returns.</summary>
    private static async Task<bool> StopIfPresentAsync(string computer, CancellationToken ct)
    {
        var state = await StateAsync(computer, ct).ConfigureAwait(false);
        if (state is null) return false;
        if (state != ServiceState.Stopped)
        {
            await Task.Run(() => ServiceControlManager.WithPooledManager(computer, m =>
            {
                using var svc = ServiceControlManager.OpenForControl(m, ServiceName);
                return svc is null ? 0 : ServiceControlManager.Stop(svc);
            }), ct).ConfigureAwait(false);
            if (!await WaitForAsync(computer, ServiceState.Stopped, TimeSpan.FromSeconds(45), ct).ConfigureAwait(false))
                throw new InvalidOperationException("the running agent did not stop within 45 s");
        }
        return true;
    }

    private static async Task<bool> WaitForAsync(string computer, ServiceState want, TimeSpan limit, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < deadline)
        {
            if (await StateAsync(computer, ct).ConfigureAwait(false) == want) return true;
            await Task.Delay(500, ct).ConfigureAwait(false);
        }
        return false;
    }

    /// <summary>SYSTEM and Administrators, full control, nothing inherited: a user at the PC can neither read the key nor swap a script.</summary>
    public static DirectorySecurity LockedFolder()
    {
        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var who in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(who, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        return acl;
    }

    // A stopped service's process can hold its exe for a moment after the SCM says Stopped.
    private static async Task CopyWithRetryAsync(string from, string to, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { File.Copy(from, to, overwrite: true); return; }
            catch (IOException) when (attempt < 10) { await Task.Delay(1000, ct).ConfigureAwait(false); }
        }
    }

    private static async Task DeleteWithRetryAsync(string dir, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { Directory.Delete(dir, recursive: true); return; }
            catch (IOException) when (attempt < 10) { await Task.Delay(1000, ct).ConfigureAwait(false); }
        }
    }
}
