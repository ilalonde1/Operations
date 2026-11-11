using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Kor.Operations.Framework.Recall;

namespace Kor.Operations.Framework.Cli;

/// <summary>
/// Board row 1893. <c>kor recall --module M "task" [--budget tokens]</c>: the pilot, one module's whole sections (<see cref="MemoryRecall"/>).
/// <c>kor recall "text" [--top N] [--as-of ISO-UTC] [--proposal] [--explain]</c>: the brain-wide ranking (<see cref="BrainRecall"/>) - every
/// lane's live records and every owner's open board rows - with score and shared words, read fresh from the database.
/// <c>kor recall --hook</c>: the moment-of-action hook. On UserPromptSubmit (the prompt) and PreToolUse (Bash, PowerShell, Write, Agent,
/// SendMessage: <see cref="BrainRecall.MomentText"/>) it adds the top <c>--top</c> (default 3) results over the floor as additionalContext,
/// each once per session; nothing over the floor, nothing added. With <c>--push-back</c> on Stop it runs <see cref="PushBack"/> on the reply
/// and blocks once per key. The hook reads the LOCAL index file (<see cref="IndexPath"/>), never the database on the call: a file older than
/// <see cref="IndexFreshFor"/> is still used, and a detached <c>kor recall --rebuild</c> is started to refresh it (so a record or board row is
/// seen by the hook up to about ten minutes after it is written; with no file at all the call builds one and waits).
/// <c>kor recall --replay transcript.jsonl [--last N]</c>: a dry run of both over a real transcript on the same index - bytes added per call,
/// time per call, and every push-back it would have made (read them for false alarms). Hook mode fails OPEN: an error adds nothing, logged.
/// The once-per-session ledger is a file per session under %TEMP%\kor-recall (lines "deliver REF" / "refuse KEY").
/// </summary>
public static partial class Program
{
    private const int HookTop = 3;
    private const int HookLeadChars = 240;
    private const string StandingKey = "standing";   // the job ledger's key once Ian's standing asks were shown (row 2042)
    internal static readonly TimeSpan IndexFreshFor = TimeSpan.FromMinutes(10);

    /// <summary>Quotes and apostrophes as themselves, not 6-byte escapes: every byte here is re-sent with the context.</summary>
    private static readonly JsonSerializerOptions RecallJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The index file; KOR_RECALL_INDEX overrides it (tests of the lock and the fallback use their own temporary file).</summary>
    internal static string IndexPath => Environment.GetEnvironmentVariable("KOR_RECALL_INDEX") is { Length: > 0 } p ? p : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KOR", "kor-recall", "index.txt");

    private static int RecallVerb(Options o, Func<string?> stdin, TextWriter output, bool hook, BrainDb? db, BrainDb? board)
    {
        if (o.Get("--module") is not null) return ModuleRecallVerb(o, output, db);
        if (o.Positional.ElementAtOrDefault(1) == "--rebuild" || o.Get("--rebuild") is not null) return RecallRebuild(output, db, board);
        if (o.Get("--replay") is { } replay) return RecallReplay(replay, o.Int("--last"), o.Int("--floor") ?? BrainRecall.MinScore, output, db, board);
        if (o.Get("--job") is { } job) return RecallJob(job, output, db, board);
        if (hook) return RecallHook(o, stdin, output, db, board);
        var text = o.Positional.ElementAtOrDefault(1);
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("recall needs the text: kor recall \"<what you are about to do>\" [--top N], or --module M for the pilot");
        var top = o.Get("--top") is null ? 10 : o.Int("--top") ?? throw new ArgumentException($"--top takes a whole number, not '{o.Get("--top")}'");
        DateTime? asOf = o.Get("--as-of") is { } a ? DateTime.Parse(a, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal) : null;
        if (o.Get("--proposal") is not null) text = PushBack.ProposalText(text);
        var clock = Stopwatch.StartNew();
        var ix = WithStores((b, w) => BrainRecall.Build(b, w, asOf), db, board, 15);
        var built = clock.ElapsedMilliseconds;
        var ranked = BrainRecall.Rank(text, ix);
        output.WriteLine($"KOR RECALL - every lane's live records ({ix.Docs.Count(d => !d.Board)}, copies kept once) + every owner's open board rows ({ix.Docs.Count(d => d.Board)}); " +
                         $"BM25 on shared words; {(asOf is null ? "now" : "as of " + asOf.Value.ToString("O", CultureInfo.InvariantCulture))}.");
        output.WriteLine($"WORDS: {string.Join(" ", BrainRecall.QueryWords(text).Take(40))}");
        output.WriteLine($"RESULTS: {ranked.Count} matched; top {Math.Min(top, ranked.Count)} below. The hook shows only score >= {BrainRecall.MinScore} on >= {BrainRecall.MinTermsHit} words.");
        foreach (var r in ranked.Take(top))
            output.WriteLine($"{r.Score.ToString("0.0", CultureInfo.InvariantCulture)} [{string.Join(",", r.Hit.Take(10))}] {BrainRecall.Line(r, 600)[2..]}");
        if (o.Get("--explain") is not null) output.WriteLine($"TIME: index built from the database {built} ms, ranked {clock.ElapsedMilliseconds - built} ms.");
        return 0;
    }

    private static T WithStores<T>(Func<BrainDb, BrainDb, T> work, BrainDb? db, BrainDb? board, int connectSeconds)
    {
        if (db is not null && board is not null) return work(db, board);
        return BrainDb.ReadWithFallback(snapshot =>
        {
            using var b = db is null ? BrainDb.OpenRead(KorStandardsConnection.Login.Brain, snapshot, connectSeconds) : null;
            using var w = board is null ? BrainDb.OpenRead(KorStandardsConnection.Login.Board, snapshot, connectSeconds) : null;
            return work(db ?? b!, board ?? w!);
        }, BrainDb.IsSnapshotRefused, BrainDb.IsDeadlock, Thread.Sleep);
    }

    /// <summary><c>kor recall --rebuild</c>: the index from the database to <see cref="IndexPath"/>. One at a time: a lock file younger than two
    /// minutes means another rebuild is running, and this one does nothing.</summary>
    private static int RecallRebuild(TextWriter output, BrainDb? db, BrainDb? board)
    {
        var lockPath = IndexPath + ".lock";
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        // CreateNew fails when a live rebuild holds the lock (review row 1248: check-then-write let two start). A lock older than two
        // minutes is a dead rebuild's and is deleted first; two processes taking over the same dead lock at once can both rebuild
        // (review row 1250). That race is accepted: each rebuild writes its own tmp file and replaces the index whole (Save), so the
        // cost is one extra rebuild, never a broken file.
        static bool Busy(Exception e) => e is IOException or UnauthorizedAccessException;
        try
        {
            if (File.Exists(lockPath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(lockPath) >= TimeSpan.FromMinutes(2)) File.Delete(lockPath);
        }
        catch (Exception e) when (Busy(e)) { return 0; }
        try
        {
            using var held = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write);
            held.Write(Encoding.ASCII.GetBytes(Environment.ProcessId.ToString(CultureInfo.InvariantCulture)));   // who holds it
        }
        catch (Exception e) when (Busy(e)) { return 0; }
        try
        {
            var ix = WithStores((b, w) => BrainRecall.Build(b, w), db, board, 15);
            BrainRecall.Save(ix, IndexPath);
            output.WriteLine($"kor recall: index rebuilt, {ix.Docs.Count} docs, {ix.Postings.Count} words, {IndexPath}");
        }
        finally { try { File.Delete(lockPath); } catch (Exception e) when (Busy(e)) { } }
        return 0;
    }

    /// <summary>The index for a hook call: the local file with only <paramref name="terms"/> loaded; when it is stale a detached rebuild is
    /// started (ShellExecute, no inherited handles, so Claude Code does not wait on it); when there is none, built here.</summary>
    private static BrainRecall.Index HookIndex(IReadOnlySet<string> terms, BrainDb? db, BrainDb? board)
    {
        if (db is not null && board is not null) return BrainRecall.Build(db, board);   // tests: the database they pass
        var ix = BrainRecall.Load(IndexPath, terms);
        if (ix is null)
        {
            RecallRebuild(TextWriter.Null, null, null);
            // another process held the rebuild lock: build in memory for this call rather than fail (review row 1248)
            return BrainRecall.Load(IndexPath, terms) ?? WithStores((b, w) => BrainRecall.Build(b, w), null, null, 3);
        }
        // only the installed kor.exe starts a rebuild: under 'dotnet kor.dll' or a test host ProcessPath is dotnet/testhost (review row 1240)
        if (DateTime.UtcNow - ix.BuiltUtc > IndexFreshFor && Environment.ProcessPath is { } exe
            && string.Equals(Path.GetFileName(exe), "kor.exe", StringComparison.OrdinalIgnoreCase))
        {
            var lockPath = IndexPath + ".lock";
            if (!File.Exists(lockPath) || DateTime.UtcNow - File.GetLastWriteTimeUtc(lockPath) > TimeSpan.FromMinutes(2))
            {
                // ShellExecute starts it without inheriting this process's handles, so the hook's stdout pipe closes when this process
                // exits and Claude Code does not wait on the rebuild (review row 1240: CreateProcess with inheritance could hold it open)
                var psi = new ProcessStartInfo(exe, "recall --rebuild") { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden };
                using var _ = Process.Start(psi);
            }
        }
        return ix;
    }

    private static string LedgerPath(string session) =>
        Path.Combine(Path.GetTempPath(), "kor-recall", string.Concat(session.Where(c => char.IsLetterOrDigit(c) || c == '-')) + ".txt");

    private static HashSet<string> ReadLedger(string? session, string prefix)
    {
        if (string.IsNullOrEmpty(session)) return new HashSet<string>();
        var p = LedgerPath(session);
        return File.Exists(p) ? File.ReadAllLines(p).Where(l => l.StartsWith(prefix, StringComparison.Ordinal)).Select(l => l[prefix.Length..]).ToHashSet() : new HashSet<string>();
    }

    private static void AddLedger(string? session, string prefix, IEnumerable<string> keys)
    {
        if (string.IsNullOrEmpty(session)) return;
        var p = LedgerPath(session);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.AppendAllLines(p, keys.Select(k => prefix + k));
    }

    private static int RecallHook(Options o, Func<string?> stdin, TextWriter output, BrainDb? db, BrainDb? board)
    {
        var raw = stdin();
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        string? ev, session, text, jobText;
        bool stopActive;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return 0;
            string? S(string n) => root.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            ev = S("hook_event_name");
            session = S("session_id");
            stopActive = root.TryGetProperty("stop_hook_active", out var sa) && sa.ValueKind == JsonValueKind.True;
            text = ev switch
            {
                "UserPromptSubmit" => S("prompt"),
                "PreToolUse" => root.TryGetProperty("tool_input", out var ti) ? BrainRecall.MomentText(S("tool_name"), ti) : null,
                "Stop" or "SubagentStop" => S("last_assistant_message"),
                _ => null,
            };
            // row 2042: a Write works a job by WHERE it writes, not by what its document quotes (10 Nov 23:5x a census doc listing
            // jobs/31218-01-..., 31180-01 and 01819-01 paths brought three fix lists)
            jobText = ev == "PreToolUse" && S("tool_name") == "Write" && root.TryGetProperty("tool_input", out var wi)
                      && wi.ValueKind == JsonValueKind.Object && wi.TryGetProperty("file_path", out var fp) && fp.ValueKind == JsonValueKind.String
                ? fp.GetString() : text;
        }
        catch (JsonException) { return 0; }
        if (string.IsNullOrWhiteSpace(text)) return 0;
        if (ev is "Stop" or "SubagentStop")
        {
            if (o.Get("--push-back") is null || stopActive || text.Length < PushBack.MinReplyChars) return 0;
            var proposal = PushBack.ProposalText(text);
            var ranked = proposal.Length == 0 ? new List<BrainRecall.Ranked>() : BrainRecall.Rank(proposal, HookIndex(BrainRecall.QueryWords(proposal), db, board));
            var v = PushBack.Decide(text, ranked, ReadLedger(session, "refuse "));
            if (!v.Refuse) return 0;
            AddLedger(session, "refuse ", new[] { v.Key! });
            output.WriteLine(JsonSerializer.Serialize(new { decision = "block", reason = v.Reason }, RecallJson));
            return 0;
        }
        var terms = BrainRecall.QueryWords(text);
        var worked = JobAsks.WorkedJobs(jobText ?? "");
        var jobTerms = JobAsks.TermsFor(worked);
        if (terms.Count < BrainRecall.MinTermsHit && jobTerms.Count == 0) return 0;
        var ix = HookIndex(terms.Union(jobTerms).ToHashSet(StringComparer.Ordinal), db, board);
        var parts = new List<string>();
        if (terms.Count >= BrainRecall.MinTermsHit)
        {
            var all = BrainRecall.Rank(text, ix);
            var say = BrainRecall.HookText(all, ReadLedger(session, "deliver "), o.Int("--top") ?? HookTop, HookLeadChars, out var refs);
            if (say is not null) { AddLedger(session, "deliver ", refs); parts.Add(say); }
        }
        // row 2031 (trap c): the first moment in a session that WORKS a job (row 2042: a job folder, --job, "job NNNNN"; not any 5-digit
        // number) brings that job's fix list - the open rows naming it that quote Ian; Ian's standing asks once per session as one line
        var listed = ReadLedger(session, "job ");
        var standingShown = listed.Contains(StandingKey);
        foreach (var job in worked.Where(j => !listed.Contains(j)))
        {
            var say = JobAsks.HookText(JobAsks.For(job, ix), !standingShown);
            if (say is null) continue;
            parts.Add(say);
            var keys = new List<string> { job };
            if (!standingShown && say.Contains("IAN'S STANDING ASKS", StringComparison.Ordinal)) { keys.Add(StandingKey); standingShown = true; }
            AddLedger(session, "job ", keys);
        }
        if (parts.Count == 0) return 0;
        output.WriteLine(JsonSerializer.Serialize(new { hookSpecificOutput = new { hookEventName = ev, additionalContext = string.Join("\n", parts) } }, RecallJson));
        return 0;
    }

    /// <summary><c>kor recall --job NNNNN</c>: the job's fix list (<see cref="JobAsks"/>, row 2031), fresh from the database - the read a job's
    /// run start makes before its brief or fix list is written.</summary>
    private static int RecallJob(string job, TextWriter output, BrainDb? db, BrainDb? board)
    {
        if (JobAsks.Candidates(job) is not [var n] || n != job.Trim()) throw new ArgumentException($"--job takes a 5-digit job number (31218), not '{job}'");
        var ix = WithStores((b, w) => BrainRecall.Build(b, w), db, board, 15);
        if (JobAsks.JobsIn(n, ix).Count == 0) output.WriteLine($"NOTE: no open row names {n} as NNNNN-NN and fewer than two name it, so the hook would not take {n} as a job.");
        output.WriteLine(JobAsks.Text(JobAsks.For(n, ix)));
        return 0;
    }

    /// <summary>The dry run over one transcript: each real prompt and each judged tool call as the hook would see it (one ledger for the
    /// whole file), and each turn's last reply through the push-back, all on one index built now. Reads only.</summary>
    private static int RecallReplay(string path, int? last, double floor, TextWriter output, BrainDb? db, BrainDb? board)
    {
        if (!File.Exists(path)) throw new ArgumentException($"no transcript at {path}");
        var moments = new List<(string Kind, string Text)>();
        string? reply = null;
        foreach (var line in File.ReadLines(path))
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); } catch (JsonException) { continue; }
            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object) continue;
                var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (!msg.TryGetProperty("content", out var content)) continue;
                if (type == "user" && content.ValueKind == JsonValueKind.String)
                {
                    var p = content.GetString() ?? "";
                    if (p.Length == 0 || p.StartsWith('<')) continue;
                    if (reply is not null) { moments.Add(("Stop", reply)); reply = null; }
                    moments.Add(("UserPromptSubmit", p));
                }
                else if (type == "assistant" && content.ValueKind == JsonValueKind.Array)
                    foreach (var c in content.EnumerateArray())
                    {
                        var ct = c.TryGetProperty("type", out var x) ? x.GetString() : null;
                        if (ct == "text") reply = c.GetProperty("text").GetString();
                        else if (ct == "tool_use" && c.TryGetProperty("input", out var input)
                                 && BrainRecall.MomentText(c.GetProperty("name").GetString(), input) is { Length: > 0 } mt)
                            moments.Add(("PreToolUse:" + c.GetProperty("name").GetString(), mt));
                    }
            }
        }
        if (reply is not null) moments.Add(("Stop", reply));
        if (last is > 0 && moments.Count > last) moments = moments.Skip(moments.Count - last.Value).ToList();
        var ix = WithStores((b, w) => BrainRecall.Build(b, w), db, board, 15);
        int calls = 0, added = 0, bytes = 0, most = 0, replies = 0, refusals = 0;
        long ms = 0, slowest = 0;
        var delivered = new HashSet<string>();
        var refused = new HashSet<string>();
        foreach (var (kind, text) in moments)
        {
            if (kind == "Stop")
            {
                replies++;
                var proposal = PushBack.ProposalText(text);
                var v = PushBack.Decide(text, proposal.Length == 0 ? new List<BrainRecall.Ranked>() : BrainRecall.Rank(proposal, ix), refused);
                if (!v.Refuse) continue;
                refused.Add(v.Key!);
                refusals++;
                output.WriteLine($"PUSH-BACK on reply: {BrainRecall.FirstSentence(text, 200)}\n  -> {v.Reason}");
                continue;
            }
            calls++;
            if (BrainRecall.QueryWords(text).Count < BrainRecall.MinTermsHit) continue;
            var sw = Stopwatch.StartNew();
            var ranked = BrainRecall.Rank(text, ix);
            sw.Stop();
            ms += sw.ElapsedMilliseconds;
            slowest = Math.Max(slowest, sw.ElapsedMilliseconds);
            var say = BrainRecall.HookText(ranked, delivered, HookTop, HookLeadChars, out var refs, floor);
            if (say is null) continue;
            delivered.UnionWith(refs);
            var json = JsonSerializer.Serialize(new { hookSpecificOutput = new { hookEventName = kind.Split(':')[0], additionalContext = say } }, RecallJson);
            var n = Encoding.UTF8.GetByteCount(json);
            added++;
            bytes += n;
            most = Math.Max(most, n);
            output.WriteLine($"ADDED {n} B on {kind}: {BrainRecall.FirstSentence(text, 120)} => {string.Join(" | ", refs)}");
        }
        output.WriteLine($"REPLAY {Path.GetFileName(path)} (floor {floor}): {calls} hook calls (prompts + Bash/PowerShell/Write/Agent/SendMessage), {added} added context " +
                         $"({(calls == 0 ? 0 : 100.0 * added / calls).ToString("0", CultureInfo.InvariantCulture)}%); bytes added: {bytes} total, " +
                         $"{(calls == 0 ? 0 : bytes / calls)} mean per call, {(added == 0 ? 0 : bytes / added)} mean per call that added, {most} most; " +
                         $"rank {(calls == 0 ? 0 : ms / calls)} ms mean, {slowest} ms slowest (in memory; the hook also loads the index file). " +
                         $"Replies: {replies}, push-backs: {refusals}.");
        return 0;
    }

    private static int ModuleRecallVerb(Options o, TextWriter output, BrainDb? db)
    {
        var module = o.Get("--module")!;
        var task = o.Positional.ElementAtOrDefault(1);
        if (string.IsNullOrWhiteSpace(task)) throw new ArgumentException("recall needs the task: kor recall --module M \"<task>\"");
        if (task.Length > MemoryRecall.MostTaskChars) throw new ArgumentException($"the task is {task.Length} chars; recall searches at most {MemoryRecall.MostTaskChars}, and will not search part of it");
        var budget = o.Get("--budget") is null ? MemoryRecall.DefaultBudgetTokens
            : o.Int("--budget") ?? throw new ArgumentException($"--budget takes a whole number, not '{o.Get("--budget")}'");
        if (budget < 1_000) throw new ArgumentException("--budget under 1,000 tokens leaves no room for the head and one section");
        var found = db is not null ? MemoryRecall.Search(db, module, task)
            : BrainDb.Read(KorStandardsConnection.Login.Brain, d => MemoryRecall.Search(d, module, task));
        if (found.ModuleRecords == 0) throw new ArgumentException($"module '{module}' holds no live record: nothing was searched");
        output.Write(MemoryRecall.Render(module, task, found, budget, DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));
        return 0;
    }
}
