using System.Text.Json;

namespace Kor.Operations.Framework.Enforce;

public static partial class CheckCache
{
    /// <summary>Same 30 s freshness and 60 s outage backoff as checks, keyed by session. Checks use the shared snapshot;
    /// only role/origin/approval are persisted per session. StartFolder survives refreshes and outages. A stale worker role can allow work
    /// during an outage; the error is logged. No cache/DB still enforces the Framework default. This is not an authority
    /// guarantee during an outage. Concurrent expiry can duplicate reads, as with the original check cache.</summary>
    public sealed record RoleSnapshot(DateTimeOffset ReadAtUtc, List<LiveCheck>? Checks, SessionRoles.Role? Role,
                                      string? StartFolder, DateTimeOffset? DownUntilUtc = null,
                                      CoordinatorApproval.Scan? ApprovalScan = null, DateTimeOffset? LoggedApproval = null);

    internal static string RolePath(string dir, string session) => Path.Combine(dir, "kor-role-" + session + ".json");
    public static RoleSnapshot? LoadRole(string dir, string session)
    {
        if (!SessionRoles.ValidSession(session)) return null;
        try { return JsonSerializer.Deserialize<RoleSnapshot>(File.ReadAllText(RolePath(dir, session))); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    internal static void SaveRole(string dir, string session, RoleSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var path = RolePath(dir, session);
            var tmp = path + "." + Environment.ProcessId + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot with { Checks = null }));
            File.Move(tmp, path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* same convenience-cache policy */ }
    }

    public static RoleSnapshot GetSession(string dir, string session, string? startFolder,
        Func<(List<LiveCheck> Checks, SessionRoles.Role? Role)> read, DateTimeOffset now, Action<Exception> log, out string source,
        Func<SessionRoles.Role?>? readRole = null)
    {
        if (!SessionRoles.ValidSession(session)) throw new ArgumentException("Invalid session cache key");
        var cached = LoadRole(dir, session);
        if (cached is null) PruneRoles(dir, session, now);
        var shared = Load(dir);
        var checks = shared is { ReadAtUtc: var at } && at != DateTimeOffset.MinValue ? shared.Checks : cached?.Checks;
        var sharedUsable = shared is not null && (shared.DownUntilUtc > now || now >= shared.ReadAtUtc && now - shared.ReadAtUtc < FreshFor);
        if (cached is not null && (cached.DownUntilUtc > now || now >= cached.ReadAtUtc && now - cached.ReadAtUtc < FreshFor))
        {
            if (sharedUsable)
            {
                source = cached.DownUntilUtc > now ? "shared checks + session role (database backoff)" : "shared checks + session role";
                return cached with { Checks = checks };
            }
        }
        if (shared?.DownUntilUtc > now)
        {
            var down = (cached ?? new RoleSnapshot(DateTimeOffset.MinValue, null, null, startFolder)) with { DownUntilUtc = shared.DownUntilUtc };
            SaveRole(dir, session, down);
            source = "shared checks + session role (database backoff)";
            return down with { Checks = checks };
        }
        try
        {
            SessionRoles.Role? role;
            if (sharedUsable && readRole is not null) role = readRole();
            else
            {
                (checks, role) = read();
                Save(dir, new Snapshot(now, checks));
            }
            var fresh = new RoleSnapshot(now, checks, role, cached?.StartFolder ?? startFolder,
                ApprovalScan: cached?.ApprovalScan, LoggedApproval: cached?.LoggedApproval);
            SaveRole(dir, session, fresh);
            source = sharedUsable && readRole is not null ? "shared checks + database role" : "database (checks and role)";
            return fresh;
        }
        catch (Exception e)
        {
            log(e);
            var stale = (cached ?? new RoleSnapshot(DateTimeOffset.MinValue, null, null, startFolder)) with { DownUntilUtc = now + DownBackoff };
            Save(dir, shared is null ? new Snapshot(DateTimeOffset.MinValue, new(), now + DownBackoff) : shared with { DownUntilUtc = now + DownBackoff });
            SaveRole(dir, session, stale);
            source = "session cache (database unavailable)";
            return stale with { Checks = checks };
        }
    }

    /// <summary>role set expires the default/local cache after its transaction commits. Other machines/custom caches
    /// see it within 30 s. Caller's rolled-back transactions never alter a production cache.</summary>
    public static void InvalidateRole(string dir, string session)
    {
        if (LoadRole(dir, session) is { } cached)
            SaveRole(dir, session, cached with { ReadAtUtc = DateTimeOffset.MinValue, DownUntilUtc = null });
    }

    public static void MarkLiftLogged(string dir, string session, DateTimeOffset approval)
    {
        if (LoadRole(dir, session) is { } cached) SaveRole(dir, session, cached with { LoggedApproval = approval });
    }

    private static void PruneRoles(string dir, string current, DateTimeOffset now)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            var files = new DirectoryInfo(dir).EnumerateFiles("kor-role-*.json", SearchOption.TopDirectoryOnly)
                .Where(f => f.Name != "kor-role-" + current + ".json")
                .OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            for (var i = 0; i < files.Count; i++)
                if (i >= 127 || now.UtcDateTime - files[i].LastWriteTimeUtc > TimeSpan.FromDays(30)) files[i].Delete();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* bounded best effort; do not alter enforcement */ }
    }
}
