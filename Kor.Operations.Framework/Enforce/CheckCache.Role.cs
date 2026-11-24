using System.Text.Json;

namespace Kor.Operations.Framework.Enforce;

public static partial class CheckCache
{
    /// <summary>Same 30 s freshness and 60 s outage backoff as checks, keyed by session. Checks and role are read in one
    /// command, saved together atomically. StartFolder survives refreshes and outages. A stale worker role can allow work
    /// during an outage; the error is logged. No cache/DB still enforces the Framework default. This is not an authority
    /// guarantee during an outage. Concurrent expiry can duplicate reads, as with the original check cache.</summary>
    public sealed record RoleSnapshot(DateTimeOffset ReadAtUtc, List<LiveCheck>? Checks, SessionRoles.Role? Role,
                                      string? StartFolder, DateTimeOffset? DownUntilUtc = null);

    internal static string RolePath(string dir, string session) => Path.Combine(dir, "kor-role-" + session + ".json");
    public static RoleSnapshot? LoadRole(string dir, string session)
    {
        if (!SessionRoles.ValidSession(session)) return null;
        try { return JsonSerializer.Deserialize<RoleSnapshot>(File.ReadAllText(RolePath(dir, session))); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static void SaveRole(string dir, string session, RoleSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var path = RolePath(dir, session);
            var tmp = path + "." + Environment.ProcessId + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot));
            File.Move(tmp, path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* same convenience-cache policy */ }
    }

    public static RoleSnapshot GetSession(string dir, string session, string? startFolder,
        Func<(List<LiveCheck> Checks, SessionRoles.Role? Role)> read, DateTimeOffset now, Action<Exception> log, out string source)
    {
        if (!SessionRoles.ValidSession(session)) throw new ArgumentException("Invalid session cache key");
        var cached = LoadRole(dir, session);
        if (cached is not null && (cached.DownUntilUtc > now || now >= cached.ReadAtUtc && now - cached.ReadAtUtc < FreshFor))
        {
            source = cached.DownUntilUtc > now ? "session cache (database backoff)" : "session cache";
            return cached;
        }
        try
        {
            var (checks, role) = read();
            var fresh = new RoleSnapshot(now, checks, role, cached?.StartFolder ?? startFolder);
            SaveRole(dir, session, fresh);
            source = "database (checks and role)";
            return fresh;
        }
        catch (Exception e)
        {
            log(e);
            var stale = (cached ?? new RoleSnapshot(DateTimeOffset.MinValue, null, null, startFolder)) with { DownUntilUtc = now + DownBackoff };
            SaveRole(dir, session, stale);
            source = "session cache (database unavailable)";
            return stale;
        }
    }

    /// <summary>role set expires the default/local cache after its transaction commits. Other machines/custom caches
    /// see it within 30 s. Caller's rolled-back transactions never alter a production cache.</summary>
    public static void InvalidateRole(string dir, string session)
    {
        if (LoadRole(dir, session) is { } cached)
            SaveRole(dir, session, cached with { ReadAtUtc = DateTimeOffset.MinValue, DownUntilUtc = null });
    }
}
