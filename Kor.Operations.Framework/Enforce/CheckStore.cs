using System.Data;
using System.Text.Json;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Records;

namespace Kor.Operations.Framework.Enforce;

/// <summary>A brain.[Check] row with its retirement (kor checks lists retired ones only when asked).</summary>
public sealed record StoredCheck(LiveCheck Check, DateTimeOffset CreatedAtUtc, string CreatedBy);

/// <summary>
/// K7: brain.[Check] (KorStandards 172) - read through brain.vw_LiveCheck, written by kor check-add / check-retire as
/// korbrain_app (brain_rw: SELECT, INSERT, UPDATE; TR_Check_AppendOnly allows one update, the retirement), and the hits
/// read from oversight.GuardRefusal as the Board login. A check's target must be a LIVE issue or lesson
/// (CK_Check_Target; kor says so first).
/// </summary>
public static class CheckStore
{
    public const string Guard = "kor-enforce";

    public static string RuleName(int checkId) => $"brain.Check #{checkId}";

    private const string LiveColumns = "Id, MemoryId, MemoryKind, MemoryName, Module, Tool, Field, Pattern, MustRefuse, MustAllow, Says, CreatedAtUtc, CreatedBy";

    /// <summary>Every live check (all modules), oldest first.</summary>
    public static IReadOnlyList<StoredCheck> Live(BrainDb db)
    {
        using var c = db.Command($"SELECT {LiveColumns} FROM brain.vw_LiveCheck ORDER BY Id", 5);
        using var r = c.ExecuteReader();
        var rows = new List<StoredCheck>();
        while (r.Read())
            rows.Add(new StoredCheck(new LiveCheck(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5),
                                                   r.GetString(6), r.GetString(7), LiveCheck.ParseExamples(r.GetString(8)),
                                                   LiveCheck.ParseExamples(r.GetString(9)), r.GetString(10)),
                                     r.GetDateTimeOffset(11), r.GetString(12)));
        return rows;
    }

    /// <summary>The checks that reach a module: its own and the Office ones (Office reaches every module).</summary>
    public static IEnumerable<LiveCheck> For(IEnumerable<LiveCheck> checks, string module) =>
        checks.Where(c => c.Module.Equals(module, StringComparison.OrdinalIgnoreCase) || c.Module.Equals("Office", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Adds a check after every refusal kor can say first: the target is a live issue or lesson, the tool and field are
    /// known, the texts fit their columns and hold no credential value, and the rule passes its known answers. Returns its Id.
    /// </summary>
    public static int Add(BrainDb db, int memoryId, string module, string tool, string pattern, IReadOnlyList<string> mustRefuse,
                          IReadOnlyList<string> mustAllow, string says, string by)
    {
        if (!Rules.Tools.Contains(tool)) throw new RecordRefusedException($"--tool is Bash, PowerShell, Write or Edit, not '{tool}'");
        if (string.IsNullOrWhiteSpace(module) || module.Length > 60) throw new RecordRefusedException("--module is a lane (or Office), at most 60 characters");
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Length > 400) throw new RecordRefusedException("--pattern is 1 to 400 characters");
        if (string.IsNullOrWhiteSpace(says) || says.Length > 400) throw new RecordRefusedException("--says is 1 to 400 characters");
        if (mustRefuse.Any(string.IsNullOrWhiteSpace) || mustAllow.Any(string.IsNullOrWhiteSpace))
            throw new RecordRefusedException("an example is empty");
        SecretGuard.Refuse(pattern, "pattern");
        SecretGuard.Refuse(says, "says");
        foreach (var e in mustRefuse.Concat(mustAllow)) SecretGuard.Refuse(e, "example");
        var m = RecordStore.Get(db, memoryId) ?? throw new RecordRefusedException($"no brain.Memory #{memoryId}");
        if (m.Kind is not ("issue" or "lesson"))
            throw new RecordRefusedException($"#{memoryId} is a {m.Kind}: a check enforces an issue or a lesson (record one: kor record --kind lesson, or kor issue)");
        if (m.RetiredAtUtc is not null || m.Status is not ("current" or "stale"))
            throw new RecordRefusedException($"#{memoryId} is not live (status {m.Status}{(m.RetiredAtUtc is null ? "" : ", retired")}): a check enforces a live issue or lesson");
        var fails = Rules.KnownAnswerFailures(pattern, tool, mustRefuse, mustAllow);
        if (fails.Count > 0) throw new RecordRefusedException($"the rule fails its known answers, nothing written: {string.Join("; ", fails)}");
        using var c = db.Command(
            "DECLARE @id TABLE (Id int); " +
            "INSERT brain.[Check] (MemoryId, Module, Tool, Field, Pattern, MustRefuse, MustAllow, Says, CreatedBy) OUTPUT INSERTED.Id INTO @id " +
            "VALUES (@m, @mod, @t, @f, @p, @r, @a, @s, @by); SELECT Id FROM @id;");
        c.Parameters.Add("@m", SqlDbType.Int).Value = memoryId;
        c.Parameters.Add("@mod", SqlDbType.NVarChar, 60).Value = module;
        c.Parameters.Add("@t", SqlDbType.NVarChar, 10).Value = tool;
        c.Parameters.Add("@f", SqlDbType.NVarChar, 10).Value = Rules.FieldOf(tool);
        c.Parameters.Add("@p", SqlDbType.NVarChar, 400).Value = pattern;
        c.Parameters.Add("@r", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(mustRefuse);
        c.Parameters.Add("@a", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(mustAllow);
        c.Parameters.Add("@s", SqlDbType.NVarChar, 400).Value = says;
        c.Parameters.Add("@by", SqlDbType.NVarChar, 100).Value = BrainStore.Clip(by, 100) ?? "unknown";
        return Convert.ToInt32(c.ExecuteScalar());
    }

    /// <summary>Retires a check (once; TR_Check_AppendOnly refuses any other change).</summary>
    public static void Retire(BrainDb db, int id, string by, string because)
    {
        if (string.IsNullOrWhiteSpace(because) || because.Length > 400) throw new RecordRefusedException("--because is 1 to 400 characters");
        SecretGuard.Refuse(because, "reason (--because)");
        using var c = db.Command("UPDATE brain.[Check] SET RetiredAtUtc = SYSDATETIMEOFFSET(), RetiredBy = @by, RetiredBecause = @why WHERE Id = @id AND RetiredAtUtc IS NULL");
        c.Parameters.Add("@by", SqlDbType.NVarChar, 100).Value = BrainStore.Clip(by, 100) ?? "unknown";
        c.Parameters.Add("@why", SqlDbType.NVarChar, 400).Value = because.Trim();
        c.Parameters.Add("@id", SqlDbType.Int).Value = id;
        if (c.ExecuteNonQuery() != 1) throw new RecordRefusedException($"brain.Check #{id} does not exist or is already retired");
    }

    /// <summary>One oversight.GuardRefusal row as the Board login: Guard kor-enforce, Rule brain.Check #id, Matched redacted
    /// through SecretGuard (CK_GuardRefusal_NoSecret refuses an unredacted marker). Returns its Id.</summary>
    public static int LogRefusal(BrainDb board, int checkId, string? session, string? repo, string matched)
    {
        using var c = board.Command(
            "DECLARE @id TABLE (Id int); INSERT oversight.GuardRefusal (Guard, [Rule], [Session], Repo, Matched) OUTPUT INSERTED.Id INTO @id " +
            "VALUES (@g, @r, @s, @repo, @m); SELECT Id FROM @id;", 3);
        c.Parameters.Add("@g", SqlDbType.NVarChar, 60).Value = Guard;
        c.Parameters.Add("@r", SqlDbType.NVarChar, 200).Value = RuleName(checkId);
        c.Parameters.Add("@s", SqlDbType.NVarChar, 80).Value = (object?)BrainStore.Clip(session, 80) ?? DBNull.Value;
        c.Parameters.Add("@repo", SqlDbType.NVarChar, 80).Value = (object?)BrainStore.Clip(repo, 80) ?? DBNull.Value;
        c.Parameters.Add("@m", SqlDbType.NVarChar, 400).Value = SecretGuard.ForGuardLog(matched);
        return Convert.ToInt32(c.ExecuteScalar());
    }

    /// <summary>Refusals per check (Guard kor-enforce), read as the Board login.</summary>
    public static IReadOnlyDictionary<int, int> Hits(BrainDb board)
    {
        using var c = board.Command(
            "SELECT [Rule], COUNT(*) FROM oversight.GuardRefusal WHERE Guard = @g AND [Rule] LIKE N'brain.Check #%' GROUP BY [Rule]", 10);
        c.Parameters.Add("@g", SqlDbType.NVarChar, 60).Value = Guard;
        using var r = c.ExecuteReader();
        var hits = new Dictionary<int, int>();
        while (r.Read())
            if (int.TryParse(r.GetString(0)["brain.Check #".Length..], out var id)) hits[id] = r.GetInt32(1);
        return hits;
    }
}

/// <summary>
/// The live checks as last read, at %LOCALAPPDATA%\KOR\kor-checks-cache.json: used for 5 minutes, and for as long as the
/// database cannot be read. After a failed read the database is not tried again for 60 s (a hook must not wait 3 s on
/// every tool call while the server is down). No database and no cache: nothing is enforced (fails open, said in the
/// hook error log).
/// </summary>
public static class CheckCache
{
    public static readonly string DefaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KOR");

    public static readonly TimeSpan Fresh = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan DownBackoff = TimeSpan.FromSeconds(60);

    public sealed record Snapshot(DateTimeOffset ReadAtUtc, List<LiveCheck> Checks, DateTimeOffset? DownUntilUtc = null);

    public static string PathIn(string dir) => System.IO.Path.Combine(dir, "kor-checks-cache.json");

    public static Snapshot? Load(string dir)
    {
        try
        {
            var p = PathIn(dir);
            return File.Exists(p) ? JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(p)) : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public static void Save(string dir, Snapshot s)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var tmp = PathIn(dir) + "." + Environment.ProcessId + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(s));
            File.Move(tmp, PathIn(dir), overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the cache is a convenience */ }
    }

    /// <summary>
    /// The checks to enforce: the cache when fresh (or while the DB is held down), else <paramref name="read"/>, saved;
    /// when the read fails, the cache at any age; null = no DB and no cache. <paramref name="source"/> says which.
    /// </summary>
    public static IReadOnlyList<LiveCheck>? Get(string dir, Func<IReadOnlyList<LiveCheck>> read, DateTimeOffset now, Action<Exception> log,
                                                out string source)
    {
        var cached = Load(dir);
        if (cached is not null && (now - cached.ReadAtUtc < Fresh || cached.DownUntilUtc > now))
        {
            // the outage marker alone (never read): still no DB and no cache
            if (cached.ReadAtUtc == DateTimeOffset.MinValue) { source = "none"; return null; }
            source = "cache";
            return cached.Checks;
        }
        try
        {
            var live = read().ToList();
            Save(dir, new Snapshot(now, live));
            source = "db";
            return live;
        }
        catch (Exception e)
        {
            log(e);
            if (cached is null)
            {
                // remember the outage (no checks known): the next calls in the minute do not wait on the server again
                Save(dir, new Snapshot(DateTimeOffset.MinValue, new List<LiveCheck>(), now + DownBackoff));
                source = "none";
                return null;
            }
            Save(dir, cached with { DownUntilUtc = now + DownBackoff });
            source = "stale cache (db unreadable)";
            return cached.Checks;
        }
    }
}
