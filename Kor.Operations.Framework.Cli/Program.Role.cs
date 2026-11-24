using System.Text.Json;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Enforce;

namespace Kor.Operations.Framework.Cli;

public static partial class Program
{
    private static int RoleVerb(Options o, TextWriter output, BrainDb? db)
    {
        var verb = o.Positional.ElementAtOrDefault(1);
        if (verb == "replay")
            return RoleReplay(o.Positional.ElementAtOrDefault(2) ?? throw new ArgumentException("role replay needs a transcript.jsonl"), output, db);
        var session = o.Get("--session") ?? throw new ArgumentException("role set/show needs --session ID");
        if (!SessionRoles.ValidSession(session)) throw new ArgumentException("Invalid --session ID");
        var cache = o.Get("--cache-dir") ?? CheckCache.DefaultDir;
        if (verb == "set")
        {
            var role = o.Positional.ElementAtOrDefault(2) ?? throw new ArgumentException("role set takes worker or coordinator");
            var id = BrainDb.Use(db, d => SessionRoles.Set(d, session, role, o.Get("--because")));
            if (db is null) CheckCache.InvalidateRole(cache, session);
            output.WriteLine($"{session}: {role} (brain.Event #{id}, role:{role})");
            return 0;
        }
        if (verb != "show") throw new ArgumentException("role takes set, show or replay");
        var stored = db is not null ? SessionRoles.Read(db, session) : BrainDb.Read3s(d => SessionRoles.Read(d, session));
        var start = CheckCache.LoadRole(cache, session)?.StartFolder
            ?? RoleOrigin(RoleTranscript(session, o.Get("--projects-dir"))) ?? o.Get("--cwd");
        output.WriteLine($"{session}: {stored?.Name ?? SessionRoles.DefaultRole(start) ?? "none"}; source: {stored?.Source ?? (start is null ? "origin unknown; no role event" : "session start " + start)}");
        return 0;
    }

    private static bool CoordinatorRoleRefusal(Options o, string raw, string? role, string? start, string? cwd, string? session,
        string? transcript, TextWriter output, bool hook, BrainDb? db, BrainDb? board, Action<Exception> log)
    {
        var decision = CoordinatorRoleGuard.Evaluate(raw, role, start);
        if (!decision.Refuse) return false;
        transcript ??= RoleTranscript(session, o.Get("--projects-dir"));
        var lift = CoordinatorApproval.Find(session, o.Get("--prompts-dir"), transcript, DateTimeOffset.UtcNow);
        if (lift is not null)
        {
            try
            {
                var ev = new BrainEvent("Framework", "gate", null, null, "role:lift", "lifted",
                    JsonSerializer.Serialize(new { session, approvedAtUtc = lift, expiresAtUtc = lift.Value.AddHours(12), decision.Rule, decision.Target }),
                    "kor enforce coordinator-role", session);
                if (db is not null) BrainStore.Insert(ev, db);
                else BrainDb.Use3s(KorStandardsConnection.Login.Brain, d => BrainStore.Insert(ev, d));
                if (!hook) output.WriteLine($"coordinator-role lift logged (expires {lift.Value.AddHours(12):O})");
                return false;
            }
            catch (Exception e) { log(e); } // an unlogged lift is not applied
        }
        var reason = CoordinatorRoleGuard.Says(decision);
        try
        {
            if (board is not null) Stall.StallStore.LogGuardRefusal(board, CoordinatorRoleGuard.GuardName, decision.Rule, session, null, decision.Target ?? reason);
            else BrainDb.Use3s(KorStandardsConnection.Login.Board, b => Stall.StallStore.LogGuardRefusal(b, CoordinatorRoleGuard.GuardName, decision.Rule, session, null, decision.Target ?? reason));
        }
        catch (Exception e) { log(e); } // the decision stands if refusal logging is unavailable
        output.WriteLine(hook
            ? JsonSerializer.Serialize(new { hookSpecificOutput = new { hookEventName = "PreToolUse", permissionDecision = "deny", permissionDecisionReason = reason } })
            : "deny: " + reason);
        return true;
    }

    internal static string? RoleOrigin(string? transcript)
    {
        if (transcript is null) return null;
        try
        {
            foreach (var line in File.ReadLines(transcript))
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var r = doc.RootElement;
                    if (r.ValueKind == JsonValueKind.Object && !(r.TryGetProperty("isSidechain", out var side) && side.ValueKind == JsonValueKind.True)
                        && Str(r, "cwd") is { } cwd) return cwd;
                }
                catch (JsonException) { /* a partial transcript line does not change the origin */ }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        return null;
    }

    private static string? RoleTranscript(string? session, string? projects)
    {
        if (!SessionRoles.ValidSession(session)) return null;
        projects ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(projects))
            {
                var path = Path.Combine(dir, session + ".jsonl");
                if (File.Exists(path)) return path;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        return null;
    }

    /// <summary>Every main-thread tool_use once, at the original line number. Nothing is executed or logged. Role events
    /// are read once, then applied at their historical timestamp. Typed approvals are reconstructed from transcript prompts
    /// (the recorder only retains the latest 20 today); tool/file text never lifts. The replay cannot reconstruct historical
    /// cache staleness or shell side effects. Unknown/missing timestamps get the default role, never today's role.</summary>
    private static int RoleReplay(string path, TextWriter output, BrainDb? db)
    {
        string? start = null, session = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        IReadOnlyList<SessionRoles.Role> history = Array.Empty<SessionRoles.Role>();
        DateTimeOffset? approval = null;
        var lineNo = 0;
        var calls = 0;
        var refused = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNo++;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); } catch (JsonException) { continue; }
            using (doc)
            {
                var r = doc.RootElement;
                if (r.ValueKind != JsonValueKind.Object || r.TryGetProperty("isSidechain", out var side) && side.ValueKind == JsonValueKind.True
                    || Str(r, "agentId") is { Length: > 0 }) continue;
                start ??= Str(r, "cwd");
                if (session is null && Str(r, "sessionId") is { } id)
                {
                    session = id;
                    history = db is not null ? SessionRoles.History(db, session) : BrainDb.Read3s(d => SessionRoles.History(d, session));
                }
                var hasTime = DateTimeOffset.TryParse(Str(r, "timestamp"), out var now);
                if (session is not null && hasTime && CoordinatorApproval.TypedApproval(r, session, now, out var at, out _)) approval = at;
                if (Str(r, "type") != "assistant" || !r.TryGetProperty("message", out var msg)
                    || !msg.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
                foreach (var block in content.EnumerateArray())
                {
                    if (Str(block, "type") != "tool_use" || Str(block, "name") is not { } tool || !block.TryGetProperty("input", out var input)) continue;
                    if (Str(block, "id") is { } callId && !seen.Add(callId)) continue;
                    var role = hasTime ? history.LastOrDefault(h => h.AtUtc <= now)?.Name : null;
                    var raw = JsonSerializer.Serialize(new { tool_name = tool, tool_input = input, cwd = Str(r, "cwd") ?? start, session_id = session });
                    var d = CoordinatorRoleGuard.Evaluate(raw, role, start);
                    if (d.Refuse && hasTime && approval <= now && now - approval < TimeSpan.FromHours(12)) d = d with { Refuse = false, Rule = "typed approval (historical lift)" };
                    calls++;
                    if (d.Refuse) refused++;
                    output.WriteLine($"L{lineNo} {(d.Refuse ? "REFUSE" : "ALLOW")} {tool} | {d.Rule} | {d.Target ?? "-"}");
                }
            }
        }
        output.WriteLine($"role replay: {calls} main-thread calls; {refused} REFUSE; {calls - refused} ALLOW; {history.Count} stored role events; start={start ?? "unknown"}");
        return 0;
    }
}
