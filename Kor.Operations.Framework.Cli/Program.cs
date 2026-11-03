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
public static class Program
{
    /// <summary>Where a hook-mode failure is logged by the installed kor.exe.</summary>
    public static readonly string DefaultHookErrorLog =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KOR", "kor-hook-errors.log");

    public static int Main(string[] args) => Run(args, ReadStdin, Console.Out, Console.Error, DefaultHookErrorLog);

    /// <summary>For tests: no stdin, and hook failures logged to a throwaway file, never the real log.</summary>
    internal static int Run(string[] args, TextWriter output, TextWriter error) =>
        Run(args, () => null, output, error, Path.Combine(Path.GetTempPath(), "kor-test-hook-errors.log"));

    /// <param name="hookErrorLog">the file a hook-mode failure is appended to (tests pass their own, review row 85).</param>
    /// <param name="db">the brain session every read and write goes through; null = each opens its own and commits (the
    /// installed kor.exe). Tests pass <see cref="BrainDb.OpenRolledBack"/>, so nothing they write is kept.</param>
    internal static int Run(string[] args, Func<string?> stdin, TextWriter output, TextWriter error, string hookErrorLog,
                            BrainDb? db = null)
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
                "capture-compact" => CaptureCompact(o, stdin, output, hook, db),
                "capture-end" => CaptureEnd(o, stdin, output, hook, db),
                "events" => Events(o, output, db),
                "fresh" => Fresh(o, stdin, output, hook, db),
                "recheck" => Recheck(o, output, db),
                "record" => Record(o, output, db),
                _ => hook ? 0 : Usage(error),
            };
        }
        catch (Exception e)
        {
            if (hook) { LogHookError(hookErrorLog, o, e); return 0; }
            error.WriteLine($"kor: {e.Message}");
            return 1;
        }
    }

    internal sealed record Options(List<string> Positional, Dictionary<string, string> Named)
    {
        private static readonly string[] Valued =
        {
            "--module", "--dir", "--result", "--last", "--kind", "--because", "--body-file", "--source-path", "--name",
            "--says", "--source",
        };

        private static readonly string[] Flags = { "--full", "--file-changed" };

        public static Options Parse(string[] args)
        {
            var pos = new List<string>();
            var named = new Dictionary<string, string>();
            for (var i = 0; i < args.Length; i++)
            {
                if (Valued.Contains(args[i]) && i + 1 < args.Length) { named[args[i]] = args[++i]; continue; }
                if (Flags.Contains(args[i])) { named[args[i]] = "1"; continue; }
                pos.Add(args[i]);
            }
            return new Options(pos, named);
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
                                 o.Int("--result"), o.Get("--module"));
        var id = BrainStore.Insert(e, db);
        if (!hook) output.WriteLine($"recorded brain.Event {id}: {e.Module} {e.Kind} {e.Commit}");
        // K5: HEAD may have moved - re-check every record citing this repo, AFTER the event is safely written, in the same
        // process, capped at 3 s. A freshness failure never loses the event or fails the git command.
        if (hookName != "pre-push" && e.Repo is not null)
        {
            var dir = o.Get("--dir") ?? Environment.CurrentDirectory;
            var run = Task.Run(() => BrainDb.Use(db, d => Freshness.CheckRepo(d, e.Repo, dir)));
            var line = "";
            try
            {
                // A caller's unit of work (a test) is never left in use by an abandoned task: wait it out (review row 139).
                if (db is not null ? run.Wait(Timeout.Infinite) : run.Wait(FreshnessCap))
                {
                    var r = run.Result;
                    line = $"freshness of repo {e.Repo}: {r.Rows.Count} checked, {r.Count(Verdict.Current)} current, " +
                           $"{r.Count(Verdict.Stale)} stale, {r.Count(Verdict.Unverifiable)} unverifiable";
                }
                else line = $"freshness of repo {e.Repo}: over {FreshnessCap.TotalSeconds:0} s - not waited for";
            }
            catch (AggregateException x) { line = $"freshness of repo {e.Repo} failed: {x.InnerException?.Message}"; LogHookError(hookErrorLog, o, x.InnerException ?? x); }
            if (!hook) output.WriteLine(line);
        }
        return 0;
    }

    internal static TimeSpan FreshnessCap = TimeSpan.FromSeconds(3);

    // ---- K5: nothing stale ------------------------------------------------------------------------------------------
    private static int Fresh(Options o, Func<string?> stdin, TextWriter output, bool hook, BrainDb? db)
    {
        if (o.Get("--file-changed") is not null) return FileChanged(stdin, output, hook, db);
        var module = o.Get("--module") ?? throw new ArgumentException("fresh needs --module <lane> (or --file-changed)");
        var report = BrainDb.Use(db, d => Freshness.CheckModule(d, module, o.Get("--dir")));
        var heads = string.Join(", ", report.RepoHeads.Select(h => $"{h.Key} {(h.Value is null ? "UNREADABLE" : h.Value[..7])}"));
        var stale = report.Rows.Where(r => r.Verdict != Verdict.Current).ToList();
        if (hook && stale.Count == 0) return 0;   // a hook prints only when it has something to say
        output.WriteLine($"kor fresh: module {module} - {report.Rows.Count} source-checked record(s): {report.Count(Verdict.Current)} current, " +
                         $"{report.Count(Verdict.Stale)} STALE, {report.Count(Verdict.Unverifiable)} unverifiable" +
                         (heads.Length > 0 ? $" (HEAD {heads})" : ""));
        foreach (var r in hook ? stale : report.Rows)
        {
            var tag = r.Verdict switch { Verdict.Stale => "STALE", Verdict.Unverifiable => "UNVERIFIABLE", _ => "current" };
            output.WriteLine($"{tag} #{r.Id} {r.Kind} {r.Name}  {r.Repo ?? r.SourceKind}:{r.Path}  - {r.Why}");
            if (r.Verdict == Verdict.Stale)
                output.WriteLine($"   recheck: kor recheck {r.Id} confirm --because \"...\"  |  supersede --because \"...\" --body-file F  |  withdraw --because \"...\"");
        }
        return 0;
    }

    /// <summary>
    /// "path[@rev]" -> (path, rev). The rev starts at the last '@' that does not open a reflog selector, so
    /// "docs/a.md@HEAD@{1}" is (docs/a.md, HEAD@{1}) (review row 139); no rev = HEAD.
    /// </summary>
    internal static (string Path, string Rev) SplitGitSource(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) throw new ArgumentException("--source git: needs a path");
        for (var at = spec.LastIndexOf('@'); at > 0; at = spec.LastIndexOf('@', at - 1))
            if (at + 1 < spec.Length && spec[at + 1] != '{') return (spec[..at], spec[(at + 1)..]);
        return (spec, "HEAD");
    }

    /// <summary>FileChanged hook:a terminal message when an edited file is the source of a live record. Writes nothing.</summary>
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
        return BrainDb.Use(db, d =>
        {
            var old = RecordStore.Get(d, id) ?? throw new RecordRefusedException($"no record #{id}");
            if (action == "withdraw")
            {
                RecordStore.Withdraw(d, id, by, because);
                output.WriteLine($"#{id} withdrawn (retired, no replacement): {because}");
                return 0;
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
            output.WriteLine($"#{id} superseded by #{newId} ({action}): {old.Kind} {old.Name} now cites {source.Repo}:{source.Path}@{source.Commit[..7]} blob {source.Blob[..7]}");
            return 0;
        });
    }

    /// <summary>
    /// kor record --module M --kind finding|decision|dead-end|lesson --name N --says D [--body-file F] --source git:path[@rev] [--dir D].
    /// The K5 subset of K4's verb: git sources only, through RecordStore (verified against git); the same live name is
    /// superseded. TODO(K4 / migration 171): --session, run:/query: sources and the 'record' event that moves K4's marker
    /// (CK_Event_Kind has no 'record' before 171).
    /// </summary>
    private static int Record(Options o, TextWriter output, BrainDb? db)
    {
        var lane = o.Get("--module") ?? throw new ArgumentException("record needs --module <lane>");
        var kind = o.Get("--kind") ?? throw new ArgumentException("record needs --kind finding|decision|dead-end|lesson");
        if (kind is "issue" or "resolution")
            throw new RecordRefusedException($"kind '{kind}' is written by `kor issue` (needs migration 171), never by kor record");
        if (kind is not ("finding" or "decision" or "dead-end" or "lesson"))
            throw new ArgumentException($"record kind '{kind}' is not finding|decision|dead-end|lesson");
        var name = o.Get("--name") ?? throw new ArgumentException("record needs --name <slug>");
        var says = o.Get("--says") ?? throw new ArgumentException("record needs --says \"...\"");
        var body = o.Get("--body-file") is { } f ? File.ReadAllText(f) : says;
        var src = o.Get("--source") ?? throw new ArgumentException("record needs --source git:path[@rev]");
        if (!src.StartsWith("git:", StringComparison.Ordinal))
            throw new RecordRefusedException("only git:path[@rev] sources are written before K4 (run:/query: need migration 171)");
        var (path, rev) = SplitGitSource(src[4..]);
        var source = RecordStore.At(o.Get("--dir") ?? Environment.CurrentDirectory, path, rev);
        var by = RecordStore.Actor();
        var rec = new NewRecord(lane, name, kind, says, body, by, source);
        return BrainDb.Use(db, d =>
        {
            var live = RecordStore.LiveByName(d, lane, name);
            var id = live is null ? RecordStore.Insert(d, rec) : RecordStore.Supersede(d, live.Id, rec, by, "restated by kor record");
            output.WriteLine($"recorded brain.Memory #{id}: {lane} {kind} {name} - {source.Repo}:{source.Path}@{source.Commit[..7]} blob {source.Blob[..7]}" +
                             (live is null ? "" : $" (supersedes #{live.Id})"));
            return 0;
        });
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
        return 0;
    }

    private static int CaptureCompact(Options o, Func<string?> stdin, TextWriter output, bool hook, BrainDb? db)
    {
        var h = ClaudeCapture.Parse(stdin() ?? throw new ArgumentException("capture-compact reads the hook JSON on stdin"));
        var (lane, id) = ClaudeCapture.Compact(h, o.Get("--module"), db);
        if (!hook) output.WriteLine($"recorded brain.Event {id}, brain.LaneState {lane?.ToString() ?? "none (empty summary)"}");
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
        error.WriteLine("       kor fresh --module M [--dir D] [--hook] | kor fresh --file-changed [--hook]   (FileChanged JSON on stdin)");
        error.WriteLine("       kor recheck <id> confirm|supersede|withdraw --because \"...\" [--body-file F] [--says D] [--source-path P] [--dir D]");
        error.WriteLine("       kor record --module M --kind finding|decision|dead-end|lesson --name N --says D --source git:path[@rev] [--body-file F] [--dir D]");
        return 2;
    }
}
