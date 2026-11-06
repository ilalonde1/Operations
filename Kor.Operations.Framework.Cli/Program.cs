using System.Reflection;
using System.Text;
using Kor.Operations.Framework;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Records;

namespace Kor.Operations.Framework.Cli;

/// <summary>
/// kor.exe - verb dispatch in the netops.exe style (Kor.Operations.NetworkOps.Cli/Program.cs).
/// <c>--hook</c> mode: it prints only when it has something to say and ALWAYS exits 0 - a hook must never fail the
/// session (or a git command) because the command hiccupped. A failure in hook mode is appended to
/// %LOCALAPPDATA%\KOR\kor-hook-errors.log so a lost capture is findable. Without <c>--hook</c>: 0 = ok, 1 = a check
/// failed, 2 = usage.
/// </summary>
public static partial class Program
{
    /// <summary>Where a hook-mode failure is logged by the installed kor.exe.</summary>
    public static readonly string DefaultHookErrorLog =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KOR", "kor-hook-errors.log");

    public static int Main(string[] args)
    {
        // UTF-8 on a redirected stdout/stderr (found 3 Nov 03:3x: `kor start --text > key.txt` wrote the console's OEM code
        // page - é as 0x82, ó as 0xA2, so 437/850, not cp1252 (review row 209) - and every non-ASCII character in a record
        // reached a pipe - a hook, a Bash tool - as invalid UTF-8). Hook JSON was unaffected
        // (System.Text.Json escapes non-ASCII), plain text was not.
        try { Console.OutputEncoding = new UTF8Encoding(false); } catch (IOException) { } catch (PlatformNotSupportedException) { }
        return Run(args, ReadStdin, Console.Out, Console.Error, DefaultHookErrorLog);
    }

    /// <summary>For tests: no stdin, and hook failures logged to a throwaway file, never the real log.</summary>
    internal static int Run(string[] args, TextWriter output, TextWriter error) =>
        Run(args, () => null, output, error, Path.Combine(Path.GetTempPath(), "kor-test-hook-errors.log"));

    /// <param name="hookErrorLog">the file a hook-mode failure is appended to (tests pass their own, review row 85).</param>
    /// <param name="db">the brain session every read and write goes through; null = each opens its own and commits (the
    /// installed kor.exe). Tests pass <see cref="BrainDb.OpenRolledBack"/>, so nothing they write is kept.</param>
    /// <param name="board">the Board login's unit of work for kor enforce's refusal row and kor checks' hits; null = each
    /// opens its own (the installed kor.exe). Tests pass a rolled-back one.</param>
    internal static int Run(string[] args, Func<string?> stdin, TextWriter output, TextWriter error, string hookErrorLog,
                            BrainDb? db = null, BrainDb? board = null)
    {
        var hook = args.Contains("--hook");
        var o = Options.Parse(args.Where(a => a != "--hook").ToArray());
        try
        {
            return (o.Positional.FirstOrDefault() ?? "") switch
            {
                "--version" or "version" => Version(output),
                "ping" => PingVerb(output, hook),
                "capture-git" => CaptureGit(o, stdin, output, hook, db, hookErrorLog),
                "capture-tool" => CaptureTool(o, stdin, output, hook, db),
                "capture-compact" => CaptureCompact(o, stdin, output, hook, db, hookErrorLog),
                "capture-end" => CaptureEnd(o, stdin, output, hook, db),
                "events" => Events(o, output, db),
                "fresh" => Fresh(o, stdin, output, hook, db),
                "recheck" => Recheck(o, output, db),
                "record" => Record(o, output, db),
                "memory" => Memory(o, output, db),
                "capture-start" => CaptureStart(o, stdin, output, hook, db),
                "capture-check" => CaptureCheck(o, stdin, output, hook, db),
                "issue" => Issue(o, output, db),
                "check" => Check(o, output, hook, db),
                "resolve" => Resolve(o, output, db),
                "start" => StartVerb(o, stdin, output, hook, db, hookErrorLog),
                "enforce" => EnforceVerb(o, stdin, output, hook, db, board, hookErrorLog),
                "check-add" => CheckAdd(o, output, db),
                "check-retire" => CheckRetire(o, output, db),
                "check-test" => CheckTest(o, output, db),
                "checks" => Checks(o, output, db, board),
                "secret-scan" => SecretScan(o, stdin, output, db),
                "state" => State(o, output, db),
                "migration-lint" => MigrationLintVerb(o, stdin, output),
                "enroll-check" => EnrollCheckVerb(o, output, hook, db),
                "read" => ReadVerb(o, output, db),
                "start-gate" => StartGateVerb(o, stdin, output, hook),
                "start-proof" => StartProofVerb(o, output, db),
                "stall" => StallVerb(o, stdin, output, hook, db, board, hookErrorLog),
                "context" => ContextVerb(o, stdin, output, hook, board, hookErrorLog),
                "spend" => SpendVerb(o, output),
                _ => hook ? UnknownInHook(o, output) : Usage(error),
            };
        }
        catch (Exception e)
        {
            if (hook) return HookFailure(o, e, output, hookErrorLog);
            error.WriteLine($"kor: {e.Message}");
            return 1;
        }
    }

    /// <summary>A hook-mode failure: logged, exit 0; the fresh hook also says so in one line (review row 146).</summary>
    private static int HookFailure(Options o, Exception e, TextWriter output, string hookErrorLog)
    {
        LogHookError(hookErrorLog, o, e);
        if (IsFreshModule(o)) output.WriteLine($"kor fresh: COULD NOT CHECK - {OneLine(e.Message)} (logged)");
        // row 1209: the enrolment check says it could not check, never nothing (a silent hook reads as a pass)
        if (o.Positional.FirstOrDefault() == "enroll-check") output.WriteLine($"kor enroll-check: COULD NOT CHECK - {OneLine(e.Message)} (logged)");
        return 0;
    }

    private static bool IsFreshModule(Options o) => o.Positional.FirstOrDefault() == "fresh" && o.Get("--file-changed") is null;

    private static string OneLine(string? s)
    {
        var t = Redact.Text(s)?.ReplaceLineEndings(" ").Trim();
        if (string.IsNullOrEmpty(t)) return "no reason given";
        return t.Length <= 300 ? t : t[..297] + "...";
    }

    /// <summary>
    /// An unknown verb in hook mode prints ONE line naming it and this kor's version, never the usage (review row 81), so
    /// an old kor.exe under a newer settings.json is visible (review row 146). Still exit 0.
    /// </summary>
    private static int UnknownInHook(Options o, TextWriter output)
    {
        var verb = o.Positional.FirstOrDefault();
        output.WriteLine(string.IsNullOrEmpty(verb)
            ? $"kor: no verb given ({VersionText()})"
            : $"kor: unknown verb '{OneLine(verb)}' - {VersionText()} is older than the hook that calls it, or the hook is wrong");
        return 0;
    }

    internal sealed record Options(List<string> Positional, Dictionary<string, string> Named)
    {
        private static readonly string[] Valued =
        {
            "--module", "--dir", "--result", "--last", "--kind", "--because", "--body-file", "--source-path", "--name",
            "--says", "--source", "--cap", "--session", "--description", "--check", "--check-ref", "--issue",
            "--kinds", "--restates", "--timeout", "--part", "--start-source", "--cache-dir", "--page", "--pages", "--cutoff",
            "--tool", "--pattern", "--memory", "--hits", "--command", "--path", "--cwd", "--file", "--what", "--days", "--tag",
            "--replay", "--since", "--window-hours", "--auto-compact-window", "--auto-compact-model", "--ceiling", "--spend-root",
            "--pages-wired", "--subject", "--model", "--claude", "--wire", "--out", "--temp-dir",
        };

        /// <summary>Options given more than once, every value kept in order (check-add's examples).</summary>
        private static readonly string[] Repeated = { "--refuse", "--allow" };

        private static readonly string[] Flags = { "--explain", "--full", "--file-changed", "--resolved", "--commit-hook", "--no-helpers", "--text", "--offsets", "--all", "--never-records", "--control", "--wiring", "--baseline" };

        /// <summary>Every --owner given, in order (kor start --part board: one lead, several board names).</summary>
        public List<string> Owners { get; init; } = new();

        /// <summary>Every value of a repeated option (--refuse, --allow), in order.</summary>
        public Dictionary<string, List<string>> Many { get; init; } = new();

        public List<string> All(string name) => Many.TryGetValue(name, out var v) ? v : new List<string>();

        public static Options Parse(string[] args)
        {
            var pos = new List<string>();
            var named = new Dictionary<string, string>();
            var owners = new List<string>();
            var many = new Dictionary<string, List<string>>();
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--owner" && i + 1 < args.Length) { owners.Add(args[++i]); continue; }
                if (Repeated.Contains(args[i]) && i + 1 < args.Length)
                {
                    if (!many.TryGetValue(args[i], out var list)) many[args[i]] = list = new List<string>();
                    list.Add(args[++i]);
                    continue;
                }
                if (Valued.Contains(args[i]) && i + 1 < args.Length) { named[args[i]] = args[++i]; continue; }
                if (Flags.Contains(args[i])) { named[args[i]] = "1"; continue; }
                pos.Add(args[i]);
            }
            return new Options(pos, named) { Owners = owners, Many = many };
        }

        public string? Get(string name) => Named.TryGetValue(name, out var v) ? v : null;
        public int? Int(string name) => int.TryParse(Get(name), out var n) ? n : null;
    }

    // ---- capture verbs (K3) ---------------------------------------------------------------------------------------
    private static int CaptureGit(Options o, Func<string?> stdin, TextWriter output, bool hook, BrainDb? db, string hookErrorLog)
    {
        var hookName = o.Positional.ElementAtOrDefault(1) ?? throw new ArgumentException("capture-git needs the hook name");
        var hookArgs = o.Positional.Skip(2).ToList();
        var input = hookName == "pre-push" ? stdin() : null;
        var e = GitCapture.Build(hookName, hookArgs, o.Get("--dir") ?? Environment.CurrentDirectory, input,
                                 o.Int("--result"), o.Get("--module"), o.Get("--session"));
        var id = BrainStore.Insert(e, db);
        if (!hook) output.WriteLine($"recorded brain.Event {id}: {e.Module} {e.Kind} {e.Commit}");
        // K5: HEAD may have moved - re-check every record citing this repo, AFTER the event is safely written, in the same
        // process, capped at 3 s. A freshness failure never loses the event or fails the git command.
        if (hookName != "pre-push" && e.Repo is not null)
        {
            var dir = o.Get("--dir") ?? Environment.CurrentDirectory;
            // A caller's unit of work (a test) is never left in use by an abandoned task: wait it out (review row 139).
            var line = RecheckWithin(e.Repo, () => BrainDb.Use(db, d => Freshness.CheckRepo(d, e.Repo, dir)),
                                     db is not null ? Timeout.InfiniteTimeSpan : FreshnessCap,
                                     x => LogHookError(hookErrorLog, o, x));
            if (!hook) output.WriteLine(line);
        }
        // K5b: a commit that names "#issue N" runs those issues' checks now (spec: post-commit, 120 s cap). Never fails git.
        if (hookName == "post-commit")
        {
            try
            {
                var lines = CommitHookCheck(o.Get("--dir") ?? Environment.CurrentDirectory, db, CommitCheckCap);
                if (lines is not null && !hook) output.WriteLine(lines);
            }
            catch (Exception x) { LogHookError(hookErrorLog, o, x); if (!hook) output.WriteLine($"kor check --commit-hook failed: {x.Message}"); }
        }
        return 0;
    }

    internal static readonly TimeSpan CommitCheckCap = TimeSpan.FromSeconds(120);

    internal static readonly TimeSpan FreshnessCap = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Runs the re-check on its own task and waits at most <paramref name="cap"/>: over the cap it is not waited for (the
    /// process ends and its uncommitted transaction rolls back); a failure is logged. Either way the line says what
    /// happened - the git command is never failed or held.
    /// </summary>
    internal static string RecheckWithin(string repo, Func<FreshnessReport> check, TimeSpan cap, Action<Exception> log)
    {
        var run = Task.Run(check);
        try
        {
            if (!run.Wait(cap)) return $"freshness of repo {repo}: over {Secs(cap)} s - not waited for";
            var r = run.Result;
            return $"freshness of repo {repo}: {r.Rows.Count} checked, {r.Count(Verdict.Current)} current, " +
                   $"{r.Count(Verdict.Stale)} stale, {r.Count(Verdict.Unverifiable)} unverifiable";
        }
        catch (AggregateException x)
        {
            var inner = x.InnerException ?? x;
            log(inner);
            return $"freshness of repo {repo} failed: {inner.Message}";
        }
    }

    // ---- K5: nothing stale ------------------------------------------------------------------------------------------
    private static int Fresh(Options o, Func<string?> stdin, TextWriter output, bool hook, BrainDb? db)
    {
        if (o.Get("--file-changed") is not null) return FileChanged(stdin, output, hook, db);
        var module = o.Get("--module") ?? throw new ArgumentException("fresh needs --module <lane> (or --file-changed)");
        var cap = double.TryParse(o.Get("--cap"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s)
                  && s >= 0 ? TimeSpan.FromSeconds(s) : FreshCap;
        // The cap is honoured inside the check (it stops before the next repo and reports the rest unchecked); the hard
        // wait (cap + grace) covers a hang inside one call. A caller's unit of work (a test) is always waited out.
        return FreshReport(module, () => BrainDb.Use(db, d => Freshness.CheckModule(d, module, o.Get("--dir"), budget: cap)),
                           db is not null ? Timeout.InfiniteTimeSpan : cap + FreshGrace, cap, output, hook);
    }

    /// <summary>Tests: the fresh hook with an injected check, through the same report and the same hook-failure line.</summary>
    internal static int RunFreshHookForTests(Func<FreshnessReport> check, string module, TextWriter output, string hookErrorLog)
    {
        var o = Options.Parse(new[] { "fresh", "--module", module });
        try { return FreshReport(module, check, Timeout.InfiniteTimeSpan, FreshCap, output, hook: true); }
        catch (Exception e) { return HookFailure(o, e, output, hookErrorLog); }
    }

    private static int FreshReport(string module, Func<FreshnessReport> check, TimeSpan hardWait, TimeSpan cap, TextWriter output, bool hook)
    {
        var report = CappedCheck(check, hardWait);
        if (report is null)
        {
            output.WriteLine($"kor fresh: PARTIAL - cap hit after {Secs(cap + FreshGrace)} s - {module}: nothing came back in time (not waited for)");
            return 0;
        }
        // Review row 146: hook or not, ONE verdict line always comes first - a silent hook could not be told from a broken one.
        output.WriteLine(VerdictLine(module, report));
        var stale = report.Rows.Where(r => r.Verdict != Verdict.Current).ToList();
        foreach (var r in hook ? stale : report.Rows)
        {
            var tag = r.Verdict switch { Verdict.Stale => "STALE", Verdict.Unverifiable => "UNVERIFIABLE", _ => "current" };
            output.WriteLine($"{tag} #{r.Id} {r.Kind} {r.Name}  {r.Repo ?? r.SourceKind}:{r.Path}  - {r.Why}");
            if (r.Verdict == Verdict.Stale)
                output.WriteLine($"   recheck: kor recheck {r.Id} confirm --because \"...\"  |  supersede --because \"...\" --body-file F  |  withdraw --because \"...\"");
        }
        return 0;
    }

    /// <summary>The SessionStart hook runs for 30 s; kor fresh stops checking at 10 s and reports what it has.</summary>
    internal static readonly TimeSpan FreshCap = TimeSpan.FromSeconds(10);

    /// <summary>Beyond the cap, how long to wait for a check stuck inside one call (a connect, a git read) before giving up.</summary>
    internal static readonly TimeSpan FreshGrace = TimeSpan.FromSeconds(5);

    /// <summary>The report, or null when it did not come back within <paramref name="hard"/>. A failure is rethrown as itself.</summary>
    internal static FreshnessReport? CappedCheck(Func<FreshnessReport> check, TimeSpan hard)
    {
        var run = Task.Run(check);
        try { return run.Wait(hard) ? run.Result : null; }
        catch (AggregateException x) when (x.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(x.InnerException).Throw();
            throw;
        }
    }

    /// <summary>Seconds with at most one decimal, always with a '.' (review row 154: never the machine's culture).</summary>
    internal static string Secs(TimeSpan t) => Freshness.Secs(t);

    internal static string VerdictLine(string module, FreshnessReport r)
    {
        var heads = string.Join(", ", r.RepoHeads.Select(h => $"{h.Key} {(h.Value is null ? "UNREADABLE" : h.Value[..7])}"));
        var counts = $"{r.Count(Verdict.Current)} current, {r.Count(Verdict.Stale)} stale, {r.Count(Verdict.Unverifiable)} unverifiable";
        // review row 150: a capped run that read no repo must not claim there were no records
        var at = heads.Length > 0 ? $" at HEAD {heads}"
               : r.Partial ? " - no repo was read before the cap"
               : r.Rows.Count > 0 ? "" : " (no source-checked records)";
        return r.Partial
            ? $"kor fresh: PARTIAL - cap hit after {Secs(r.Elapsed)} s - {module}: {counts}{at}"
            : $"kor fresh: {module} - {counts}{at}";
    }

    /// <summary>
    /// "path[@rev]" -> (path, rev), decided by GIT, not by the text (review row 140: a path may hold '@', as in
    /// node_modules/@scope/x.js). Each '@' that does not open a reflog selector ("@{") is tried from the right, then the
    /// whole text as a path at HEAD; the first (path, rev) where <c>rev:path</c> is a tracked blob wins. None -> the whole
    /// text at HEAD, which RecordStore.At then refuses with its reason.
    /// </summary>
    /// <param name="tracked">(rev, path) -> is path tracked at rev; the real one asks git in the repo.</param>
    internal static (string Path, string Rev) SplitGitSource(string spec, Func<string, string, bool> tracked)
    {
        if (string.IsNullOrWhiteSpace(spec)) throw new ArgumentException("--source git: needs a path");
        for (var at = spec.LastIndexOf('@'); at > 0; at = spec.LastIndexOf('@', at - 1))
        {
            if (at + 1 >= spec.Length || spec[at + 1] == '{') continue;
            var (path, rev) = (spec[..at], spec[(at + 1)..]);
            if (tracked(rev, path)) return (path, rev);
        }
        return (spec, "HEAD");
    }

    internal static Func<string, string, bool> TrackedIn(string dir) => (rev, path) =>
    {
        var top = Git.TopLevel(dir);
        if (top is null) return false;
        try { return RecordStore.BlobAt(top, rev, RecordStore.Relative(top, path)) is not null; }
        catch (RecordRefusedException) { return false; }   // not a path inside the repo
    };

    /// <summary>FileChanged hook: a terminal message when an edited file is the source of a live record. Writes nothing.</summary>
    private static int FileChanged(Func<string?> stdin, TextWriter output, bool hook, BrainDb? db)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(stdin() ?? throw new ArgumentException("fresh --file-changed reads the hook JSON on stdin"));
        var path = doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("file_path", out var fp) && fp.ValueKind == System.Text.Json.JsonValueKind.String
            ? fp.GetString()! : throw new ArgumentException("the FileChanged input has no file_path");
        var report = BrainDb.Use(db, d => Freshness.CheckPath(d, path));
        var changed = report.Rows.Where(r => r.Verdict != Verdict.Current).ToList();
        if (changed.Count == 0)
        {
            if (!hook) output.WriteLine($"kor fresh: {path} - {report.Rows.Count} record(s) cite it, none changed");
            return 0;
        }
        var text = $"kor: {path} is the source of {string.Join(", ", changed.Select(r => $"#{r.Id} {r.Kind} {r.Name} ({r.Lane})"))}" +
                   " - it no longer matches the cited blob. Commit, then `kor fresh --module <lane>` and `kor recheck <id> ...`.";
        output.WriteLine(hook ? System.Text.Json.JsonSerializer.Serialize(new { systemMessage = text }) : text);
        return 0;
    }

    /// <summary>kor recheck &lt;id&gt; confirm|supersede|withdraw --because "..." [--body-file F] [--says D] [--source-path P] [--dir D]</summary>
    private static int Recheck(Options o, TextWriter output, BrainDb? db)
    {
        if (!int.TryParse(o.Positional.ElementAtOrDefault(1), out var id)) throw new ArgumentException("recheck needs a record id");
        var action = o.Positional.ElementAtOrDefault(2) ?? throw new ArgumentException("recheck needs confirm|supersede|withdraw");
        var because = o.Get("--because") ?? throw new ArgumentException("recheck needs --because \"...\"");
        var by = RecordStore.Actor();
        var line = BrainDb.Use(db, d =>
        {
            var old = RecordStore.Get(d, id) ?? throw new RecordRefusedException($"no record #{id}");
            // up front, before any git read or write: an issue or resolution never goes through recheck
            RecordStore.RefuseIssueKinds(old, action == "withdraw" ? "withdrawn" : "superseded");
            if (action == "withdraw")
            {
                RecordStore.Withdraw(d, id, by, because);
                return ($"#{id} withdrawn (retired, no replacement): {because}");
            }
            if (action is not ("confirm" or "supersede")) throw new ArgumentException($"unknown recheck action '{action}' (confirm|supersede|withdraw)");
            if (old.SourceKind != "git" || old.SourceRepo is null || old.SourcePath is null)
                throw new RecordRefusedException($"#{id} is {old.SourceKind ?? "unsourced"}-sourced: only a git-sourced record is rechecked against HEAD");
            var repoDir = RepoLocator.Find(old.SourceRepo, o.Get("--dir"))
                          ?? throw new RecordRefusedException($"repo {old.SourceRepo} is not on this machine beside the current one - cannot recheck");
            var body = old.Body;
            if (action == "supersede")
            {
                var file = o.Get("--body-file") ?? throw new ArgumentException("recheck supersede needs --body-file F (the corrected record)");
                body = File.ReadAllText(file);
            }
            var source = RecordStore.AtHead(repoDir, action == "supersede" ? o.Get("--source-path") ?? old.SourcePath : old.SourcePath);
            var replacement = new NewRecord(old.Lane, old.Name, old.Kind, o.Get("--says") ?? old.Description, body, by, source,
                                            Source: $"recheck {action} of #{id}: {because}");
            var newId = RecordStore.Supersede(d, id, replacement, by, $"recheck {action}: {because}");
            return ($"#{id} superseded by #{newId} ({action}): {old.Kind} {old.Name} now cites {source.Repo}:{source.Path}@{source.Commit[..7]} blob {source.Blob[..7]}");
        });
        output.WriteLine(line);   // after the commit (review row 165)
        return 0;
    }

    private static int CaptureTool(Options o, Func<string?> stdin, TextWriter output, bool hook, BrainDb? db)
    {
        var h = ClaudeCapture.Parse(stdin() ?? throw new ArgumentException("capture-tool reads the hook JSON on stdin"));
        var e = ClaudeCapture.Tool(h, o.Get("--module"));
        if (e is null)
        {
            if (!hook) output.WriteLine("nothing to record");
            return 0;
        }
        var id = BrainStore.Insert(e, db);
        if (!hook) output.WriteLine($"recorded brain.Event {id}: {e.Module} {e.Kind} {e.Outcome} {e.Ref}");
        // K5b: a test run is evidence for the issues it names (a pass of the exact filter resolves; a named failure of a
        // resolved issue opens its regression) - after the event is safely written, in a unit of work of its own.
        if (e.Kind == "test" && e.Repo is not null)
        {
            var repoDir = RepoLocator.Find(e.Repo, h.Cwd is { } cwd && Directory.Exists(cwd) ? cwd : null);
            var output2 = h.Error is null ? h.Output : (h.Output ?? "") + "\n" + h.Error;
            var lines = BrainDb.Use(db, d => TestRunIssues.Map(d, e, h.Command, output2, repoDir, ClaudeCapture.ActorOf(h)));
            if (!hook) foreach (var l in lines) output.WriteLine(l);
        }
        return 0;
    }

    /// <summary>PostCompact. A credential value in the summary is masked and the rest stored; ONE line in the hook error log
    /// names the mask (rule, length, where - never the value), and outside hook mode the same line is printed.</summary>
    private static int CaptureCompact(Options o, Func<string?> stdin, TextWriter output, bool hook, BrainDb? db, string hookErrorLog)
    {
        var h = ClaudeCapture.Parse(stdin() ?? throw new ArgumentException("capture-compact reads the hook JSON on stdin"));
        var (lane, id, masked) = ClaudeCapture.Compact(h, o.Get("--module"), db);
        if (SecretGuard.MaskedLine(masked, $"compact summary (brain.LaneState {lane})") is { } line)
        {
            LogHookLine(hookErrorLog, o, line);
            if (!hook) output.WriteLine(line);
        }
        if (!hook) output.WriteLine($"recorded brain.Event {id}, brain.LaneState {lane?.ToString() ?? "none (empty summary)"}");
        return 0;
    }

    /// <summary>
    /// kor state --module M (--file F | --text T): a resume block into brain.LaneState (the C# replacement for
    /// <c>kor_brain.py state</c>). Session = CLAUDE_CODE_SESSION_ID. A credential value is MASKED, not refused - a lost
    /// resume costs more than a masked token - and one line says so (rule, length, where; never the value).
    /// </summary>
    private static int State(Options o, TextWriter output, BrainDb? db)
    {
        var module = o.Get("--module") ?? throw new ArgumentException("state needs --module <lane>");
        var file = o.Get("--file");
        // --text is also kor start's flag, so its value is the next positional word
        var text = o.Get("--text") is not null ? o.Positional.ElementAtOrDefault(1) : null;
        if ((file is null) == (text is null)) throw new ArgumentException("state needs exactly one of --file F or --text T");
        var block = text ?? File.ReadAllText(file!, new UTF8Encoding(false));
        if (string.IsNullOrWhiteSpace(block)) throw new ArgumentException("state: the resume block is empty - nothing written");
        var by = $"{Environment.UserName} kor state";
        var (id, masked) = BrainStore.InsertLaneStateMasked(module, block, by, db, BrainStore.CurrentSession());
        if (SecretGuard.MaskedLine(masked, "resume block") is { } line) output.WriteLine(line);
        output.WriteLine($"brain.LaneState {id}: {module}, {block.Length} characters, session {BrainStore.CurrentSession() ?? "none (not in a Claude session)"}");
        return 0;
    }

    private static int CaptureEnd(Options o, Func<string?> stdin, TextWriter output, bool hook, BrainDb? db)
    {
        var h = ClaudeCapture.Parse(stdin() ?? throw new ArgumentException("capture-end reads the hook JSON on stdin"));
        var e = ClaudeCapture.End(h, o.Get("--module"));
        var id = BrainStore.Insert(e, db);
        if (!hook) output.WriteLine($"recorded brain.Event {id}: {e.Module} {e.Kind}");
        return 0;
    }

    // ---- read-back (K6 groundwork) --------------------------------------------------------------------------------
    private static int Events(Options o, TextWriter output, BrainDb? db)
    {
        var module = o.Get("--module") ?? throw new ArgumentException("events needs --module <name>");
        var rows = BrainStore.Last(module, o.Int("--last") ?? 10, o.Get("--kind"), db);
        var full = o.Get("--full") is not null;
        output.WriteLine($"{rows.Count} event(s) for module {module}, newest first");
        foreach (var r in rows)
        {
            output.WriteLine($"#{r.Id} {r.AtUtc:yyyy-MM-dd HH:mm:ss zzz} {r.Kind} outcome={r.Outcome ?? "-"} repo={r.Repo ?? "-"} commit={r.Commit ?? "-"}");
            output.WriteLine($"   actor: {r.Actor}");
            if (r.Ref is not null) output.WriteLine($"   ref: {r.Ref}");
            if (r.Detail is not null) output.WriteLine($"   detail: {(full || r.Detail.Length <= 300 ? r.Detail : r.Detail[..300] + " ...")}");
        }
        return 0;
    }

    // ---- plumbing -------------------------------------------------------------------------------------------------
    /// <summary>All of stdin, or null when there is none; never blocks longer than 5 s.</summary>
    private static string? ReadStdin()
    {
        if (!Console.IsInputRedirected) return null;
        var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        var read = Task.Run(() => stdin.ReadToEnd());
        return read.Wait(TimeSpan.FromSeconds(5)) ? read.Result : null;
    }

    private static void LogHookError(string path, Options o, Exception e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var verb = string.Join(' ', o.Positional.Take(2));
            var module = o.Get("--module") is { } m ? $" module={m}" : "";
            var where = $" cwd={Environment.CurrentDirectory}";
            File.AppendAllText(path,
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} {verb}{module}{where}: {e.GetType().Name}: {Redact.Text(e.Message)}{Environment.NewLine}");
        }
        catch { /* a hook never fails, not even on its own log */ }
    }

    /// <summary>One line in the hook log that is not a failure (capture-compact's mask). The line is SecretGuard-masked
    /// again on the way in, so it can never carry a value.</summary>
    private static void LogHookLine(string path, Options o, string line)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var verb = string.Join(' ', o.Positional.Take(2));
            var module = o.Get("--module") is { } m ? $" module={m}" : "";
            File.AppendAllText(path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} {verb}{module}: {Redact.Text(SecretGuard.Mask(line))}{Environment.NewLine}");
        }
        catch { /* a hook never fails, not even on its own log */ }
    }

    internal static string VersionText()
    {
        var asm = typeof(Program).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? asm.GetName().Version?.ToString() ?? "0";
        return $"kor {info}";
    }

    private static int Version(TextWriter output)
    {
        output.WriteLine(VersionText());
        return 0;
    }

    private static int PingVerb(TextWriter output, bool hook)
    {
        var results = Ping.Run();
        foreach (var r in results)
            output.WriteLine($"{(r.Ok ? "ok  " : "FAIL")} {r.Login}: {r.Detail}");
        return hook || results.All(r => r.Ok) ? 0 : 1;
    }

    private static int Usage(TextWriter error)
    {
        error.WriteLine("usage: kor --version | kor ping [--hook]");
        error.WriteLine("       kor capture-git <post-commit|post-merge|post-checkout|pre-push> [git's hook args] [--result N] [--module M] [--dir D] [--hook]");
        error.WriteLine("       kor capture-tool|capture-compact|capture-end [--module M] [--hook]   (hook JSON on stdin)");
        error.WriteLine("       kor events --module M [--last N] [--kind K] [--full]");
        error.WriteLine("       kor fresh --module M [--dir D] [--cap SECONDS (10)] [--hook] | kor fresh --file-changed [--hook]   (FileChanged JSON on stdin)");
        error.WriteLine("       kor recheck <id> confirm|supersede|withdraw --because \"...\" [--body-file F] [--says D] [--source-path P] [--dir D]");
        error.WriteLine("       kor record --module M --kind finding|decision|dead-end|lesson|reference --name N --says D --source git:path[@rev]|run:<event id>|query:SELECT... [--session S] [--body-file F] [--dir D]");
        error.WriteLine("       kor memory --module M [--last N]");
        error.WriteLine("       kor capture-start|capture-check [--module M] [--hook]   (SessionStart / Stop JSON on stdin)");
        error.WriteLine("       kor capture-check --replay <transcript.jsonl|list.txt> [--since ISO-UTC]   (row 1393 dry run: every turn through the Stop rules; reads only)");
        error.WriteLine("       kor issue --module M --name N --description D --check test|gate|query|source --check-ref R [--source-path P] [--body-file F] [--restates ID]");
        error.WriteLine("       kor check --module M [--issue N] [--resolved] [--kinds test,gate,query] [--timeout S] | kor check --commit-hook [--dir D]");
        error.WriteLine("       kor resolve --issue N --because \"...\"   (a source-checked issue whose file changed)");
        error.WriteLine("       kor enforce --module M --hook   (PreToolUse JSON on stdin; Bash|PowerShell|Write|Edit|MultiEdit|NotebookEdit)");
        error.WriteLine("       kor check-add --memory ID --module M|Office --tool Bash[,PowerShell] --pattern P --refuse E [--refuse E2]... --allow A [--allow A2]... --says S");
        error.WriteLine("       kor check-retire <id> --because \"...\" | kor check-test [--module M] [--tool T --command C|--path P [--cwd D]] | kor checks [--module M] [--hits N]");
        error.WriteLine("       kor secret-scan [--explain] [--offsets]   (read-only: ids and counts of brain rows holding a credential value; --offsets adds offset, length and the 40 characters BEFORE each value)");
        error.WriteLine("       kor secret-scan --text - [--what W]   (the text on stdin: 'clean' exit 0, or one refusal line exit 1 - never the value)");
        error.WriteLine("       kor state --module M (--file F | --text T)   (a resume block -> brain.LaneState; a credential value is MASKED and said in one line)");
        error.WriteLine("       kor migration-lint <file.sql> | - --name NNN_x.sql [--cutoff N (177)]   (a migration above the cut-off: L1 a 'PROBES:' header line, L2 no hand-written probe handling without migration.usp_MigrationProbe; one line per finding, exit 1)");
        error.WriteLine("       kor read --module M --subject \"words\" [--page N] [--all]   (row 1569: the bodies for a subject, whole, X of Y)");
        error.WriteLine("       kor start-gate --module M --hook   (row 1569, PreToolUse every tool: refused until every memory page is held and the subject's bodies are read; KOR_START_GATE=off lifts it)");
        error.WriteLine("       kor start-proof --module M --dir D [--control] [--baseline] [--wiring] [--model M] [--wire N] [--claude path] [--out F]   (row 1569: one no-tools cold test, graded from its own transcript; --wiring prints the settings lines)");
        error.WriteLine("       kor stall --hook   (PreToolUse JSON on stdin; Bash|PowerShell|WebFetch|mcp__.*: G1 stall guard, board row 1308)");
        error.WriteLine("       kor stall --replay <transcript.jsonl|list.txt> [--since ISO-UTC] [--window-hours H] [--never-records]   (row 1308 dry run: every judged call through G1; reads only)");
        error.WriteLine($"       kor context --module M --hook   (PreToolUse every tool, UserPromptSubmit, Stop: G5 context line, board row 1474 - WARN {Context.ContextGuard.N(Context.ContextGuard.WarnTokens)} and every {Context.ContextGuard.N(Context.ContextGuard.WarnStepTokens)} after, STOP {Context.ContextGuard.N(Context.ContextGuard.StopTokens)})");
        error.WriteLine("       kor context --replay <transcript.jsonl|list.txt> [--since ISO-UTC] [--auto-compact-window N [--auto-compact-model M]]   (row 1474 dry run; reads only)");
        error.WriteLine("       kor spend   (G6, board row 1559: the one spend line, an estimate; kor context's hooks carry it; warns only)");
        error.WriteLine("       kor enroll-check [--all] [--dir D] [--days N (7)] [--hook]   (every framework hook in .claude/settings.json and .git/hooks, and a session-start in brain.Event where sessions ran; one verdict line, exit 1 on a finding - 0 with --hook)");
        error.WriteLine("       kor start --module M [--owner O]... [--no-helpers] [--part record|state|memory|records|pages|board|all] [--pages-wired W (record)] [--tag T (board: only rows tagged [T] or [was T]; with no --owner, any owner's, no owner named)] [--page N --pages M (memory, records)] [--hook | --text] [--dir D] [--session S] [--start-source S]   (SessionStart JSON on stdin with --hook)");
        return 2;
    }
}
