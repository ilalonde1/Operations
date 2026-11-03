using System.Reflection;
using System.Text;
using Kor.Operations.Framework;
using Kor.Operations.Framework.Capture;

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
    /// installed kor.exe). Tests pass <see cref="BrainSession.BeginRolledBack"/>, so nothing they write is kept.</param>
    internal static int Run(string[] args, Func<string?> stdin, TextWriter output, TextWriter error, string hookErrorLog,
                            BrainSession? db = null)
    {
        var hook = args.Contains("--hook");
        var o = Options.Parse(args.Where(a => a != "--hook").ToArray());
        try
        {
            return (o.Positional.FirstOrDefault() ?? "") switch
            {
                "--version" or "version" => Version(output),
                "ping" => PingVerb(output, hook),
                "capture-git" => CaptureGit(o, stdin, output, hook, db),
                "capture-tool" => CaptureTool(o, stdin, output, hook, db),
                "capture-compact" => CaptureCompact(o, stdin, output, hook, db),
                "capture-end" => CaptureEnd(o, stdin, output, hook, db),
                "events" => Events(o, output, db),
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
        private static readonly string[] Valued = { "--module", "--dir", "--result", "--last", "--kind" };

        public static Options Parse(string[] args)
        {
            var pos = new List<string>();
            var named = new Dictionary<string, string>();
            for (var i = 0; i < args.Length; i++)
            {
                if (Valued.Contains(args[i]) && i + 1 < args.Length) { named[args[i]] = args[++i]; continue; }
                if (args[i] == "--full") { named["--full"] = "1"; continue; }
                pos.Add(args[i]);
            }
            return new Options(pos, named);
        }

        public string? Get(string name) => Named.TryGetValue(name, out var v) ? v : null;
        public int? Int(string name) => int.TryParse(Get(name), out var n) ? n : null;
    }

    // ---- capture verbs (K3) ---------------------------------------------------------------------------------------
    private static int CaptureGit(Options o, Func<string?> stdin, TextWriter output, bool hook, BrainSession? db)
    {
        var hookName = o.Positional.ElementAtOrDefault(1) ?? throw new ArgumentException("capture-git needs the hook name");
        var hookArgs = o.Positional.Skip(2).ToList();
        var input = hookName == "pre-push" ? stdin() : null;
        var e = GitCapture.Build(hookName, hookArgs, o.Get("--dir") ?? Environment.CurrentDirectory, input,
                                 o.Int("--result"), o.Get("--module"));
        var id = BrainStore.Insert(e, db);
        if (!hook) output.WriteLine($"recorded brain.Event {id}: {e.Module} {e.Kind} {e.Commit}");
        return 0;
    }

    private static int CaptureTool(Options o, Func<string?> stdin, TextWriter output, bool hook, BrainSession? db)
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

    private static int CaptureCompact(Options o, Func<string?> stdin, TextWriter output, bool hook, BrainSession? db)
    {
        var h = ClaudeCapture.Parse(stdin() ?? throw new ArgumentException("capture-compact reads the hook JSON on stdin"));
        var (lane, id) = ClaudeCapture.Compact(h, o.Get("--module"), db);
        if (!hook) output.WriteLine($"recorded brain.Event {id}, brain.LaneState {lane?.ToString() ?? "none (empty summary)"}");
        return 0;
    }

    private static int CaptureEnd(Options o, Func<string?> stdin, TextWriter output, bool hook, BrainSession? db)
    {
        var h = ClaudeCapture.Parse(stdin() ?? throw new ArgumentException("capture-end reads the hook JSON on stdin"));
        var e = ClaudeCapture.End(h, o.Get("--module"));
        var id = BrainStore.Insert(e, db);
        if (!hook) output.WriteLine($"recorded brain.Event {id}: {e.Module} {e.Kind}");
        return 0;
    }

    // ---- read-back (K6 groundwork) --------------------------------------------------------------------------------
    private static int Events(Options o, TextWriter output, BrainSession? db)
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
        return 2;
    }
}
