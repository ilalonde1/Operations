#nullable enable
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Kor.Operations.NetworkOps.Transport;

// Reach a WORKGROUP box (BK01, the boardroom PC) with a LOCAL admin credential, the same way the push reaches a domain
// PC as the service account. The SMB + SCM push authenticates as the PROCESS token -- the domain service account --
// because OpenSCManager(\\host, null, ...) and the c$ file copies use the caller's identity. A workgroup machine has
// never heard of that domain account, so it answers "Access denied" (System error 5). There is otherwise nothing special
// about a workgroup box: its agent, once installed, calls home over HTTPS exactly like a domain PC's.
//
// The fix is the `net use` trick: before the c$ writes and the svcctl calls, open an SMB session to \\host\IPC$ UNDER
// the local credential. Windows keys sessions by server, so every following connection to that SAME server string (its
// c$ share, and the named pipe the Service Control Manager rides) reuses it. So the whole push -- install, agentless
// fix, agentless health probe -- then runs as the local admin, over the robust channel, with no MeshCentral involved.
//
// The credential is registered from APP01's environment at startup (its password never lives in the repo or in config,
// only in a named machine variable). A session is per logon (the service process): opened once (the handshake is the
// cost), kept, re-opened if it drops. Nothing here logs the password.
public static class LocalAuth
{
    private sealed record Cred(string User, string Password);

    // Every string the push may address a host by (its IP, and each name alias) -> the credential to use for it.
    private static readonly ConcurrentDictionary<string, Cred> Registered = new(StringComparer.OrdinalIgnoreCase);
    // Host strings that currently have an IPC$ session open under their credential.
    private static readonly ConcurrentDictionary<string, bool> Open = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    /// <summary>Register a local admin credential for a host under its address AND every alias it is reached by, so an
    /// IP-targeted install/probe and a name-targeted fleet probe both find it. Called once at startup from APP01's env.</summary>
    public static void Register(string host, string user, string password, IEnumerable<string>? aliases = null)
    {
        var cred = new Cred(user, password);
        Registered[host] = cred;
        foreach (var a in aliases ?? [])
            if (!string.IsNullOrWhiteSpace(a)) Registered[a] = cred;
    }

    /// <summary>Whether a local credential is registered for this host string (the ordinary domain path has none).</summary>
    public static bool Has(string host) => Registered.ContainsKey(host);

    /// <summary>If a local credential is registered for this host, make sure an SMB session to \\host\IPC$ is open under
    /// it, so the c$ writes and the SCM calls that follow authenticate as that account. Idempotent, and a no-op for a host
    /// with no registered credential. Throws <see cref="Win32Exception"/> when the session cannot be opened.</summary>
    public static void Ensure(string host)
    {
        if (!Registered.TryGetValue(host, out var cred)) return;   // a domain host: nothing to do, normal path
        if (Open.ContainsKey(host)) return;
        lock (Gate)
        {
            if (Open.ContainsKey(host)) return;
            Connect(host, cred);
            Open[host] = true;
        }
    }

    /// <summary>Forget a host's session after it may have dropped (a stale SCM handle), so the next Ensure re-opens it.</summary>
    public static void Drop(string host)
    {
        if (Open.TryRemove(host, out _)) TryCancel(Ipc(host));
    }

    private static string Ipc(string host) => $@"\\{host}\IPC$";

    private static void Connect(string host, Cred cred)
    {
        var nr = new NETRESOURCE { dwType = ResourceTypeAny, lpRemoteName = Ipc(host) };
        var rc = WNetAddConnection2(ref nr, cred.Password, cred.User, 0 /* no CONNECT_UPDATE_PROFILE: in-memory, never persisted */);
        // A session to this server already exists (ours from before, or under other credentials): drop it and open ours.
        if (rc is ErrorAlreadyAssigned or ErrorSessionCredentialConflict)
        {
            TryCancel(Ipc(host));
            rc = WNetAddConnection2(ref nr, cred.Password, cred.User, 0);
        }
        if (rc != NoError)
            throw new Win32Exception((int)rc, $"opening an authenticated session to {Ipc(host)} as {cred.User} failed");
    }

    private static void TryCancel(string remote)
    {
        try { WNetCancelConnection2(remote, 0, force: true); } catch { /* best effort */ }
    }

    // ---- Win32 (mpr.dll). WNetAddConnection2 returns the Win32 error directly, so SetLastError is not used.
    private const uint NoError = 0, ErrorAlreadyAssigned = 85, ErrorSessionCredentialConflict = 1219, ResourceTypeAny = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NETRESOURCE
    {
        public uint dwScope, dwType, dwDisplayType, dwUsage;
        public string? lpLocalName, lpRemoteName, lpComment, lpProvider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern uint WNetAddConnection2(ref NETRESOURCE netResource, string? password, string? username, uint flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern uint WNetCancelConnection2(string name, uint flags, bool force);
}
