using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Kor.Operations.Framework.Enroll;
using Kor.Operations.Framework.Start;

namespace Kor.Operations.Framework.Cli;

/// <summary>
/// Board row 1569: <c>kor start-proof --module M --dir D [--control] [--baseline] [--wiring] [--wire N] [--model M] [--claude path] [--out F] [--temp-dir D]</c>.
/// One no-tools cold session in D, graded from its own transcript (<see cref="ColdProof"/>). The proof session runs the settings the
/// wiring will have, on THIS kor (the built Debug one before an install), and with no project settings of the old wiring beside it;
/// --control runs the repo's own settings as they stand, the known-bad run the grader must fail. --baseline runs the repo's own SessionStart
/// lines in the proof's mode, which the token arithmetic needs (see below). --wiring prints the lines instead.
/// A prompt is the only thing it spends: no hook runs it, and the one test that does (its cleanup when the session fails) gives it a --claude that spends none.
/// </summary>
public static partial class Program
{
    private static readonly string[] SessionEnv =
    {
        "CLAUDECODE", "CLAUDE_CODE_SESSION_ID", "CLAUDE_CODE_BRIDGE_SESSION_ID", "CLAUDE_CODE_MESSAGING_SOCKET", "CLAUDE_CODE_MESSAGING_TOKEN",
        "CLAUDE_CODE_CHILD_SESSION", "CLAUDE_PID", "CLAUDE_CODE_ENTRYPOINT", "CLAUDE_CODE_SESSION_ATTENDED",
    };

    private static readonly string[] NoTools =
    {
        "Read", "Write", "Edit", "MultiEdit", "NotebookEdit", "Bash", "PowerShell", "Grep", "Glob", "LS", "WebFetch", "WebSearch", "Agent", "Task", "Skill",
        "TodoWrite", "ToolSearch", "Monitor", "SendMessage", "Artifact",
    };

    private static int StartProofVerb(Options o, TextWriter output, BrainDb? db)
    {
        var module = o.Get("--module") ?? throw new ArgumentException("start-proof needs --module <lane>");
        var dir = o.Get("--dir") ?? throw new ArgumentException("start-proof needs --dir <the repo's start folder>");
        var settingsPath = Path.Combine(dir, ".claude", "settings.json");
        var settings = File.ReadAllText(settingsPath, Encoding.UTF8);
        var wire = o.Int("--wire") ?? EnrollCheck.PagesWired;
        if (o.Get("--wiring") is not null)
        {
            const string live = "C:/KOR-Operations/kor/kor.exe";
            foreach (var c in ColdProof.Wiring(settings, module, live, wire)) output.WriteLine(c);
            output.WriteLine("PreToolUse, matcher *: " + ColdProof.GateCommand(module, live));
            return 0;
        }
        var control = o.Get("--control") is not null;
        var start = new ModuleState.Start(null, "startup");
        var (side, resume, _) = db is not null ? ModuleState.ReadPages(db, module, start, dir)
            : BrainDb.Read(KorStandardsConnection.Login.Brain, d => ModuleState.ReadPages(d, module, start, dir));
        var questions = ColdProof.Make(side, resume);
        var exe = (Environment.ProcessPath ?? throw new InvalidOperationException("no process path")).Replace('\\', '/');
        var args = new List<string> { "-p", "--output-format", "json", "--permission-mode", "dontAsk", "--strict-mcp-config", "--effort", "low", "--max-turns", "1" };
        if (o.Get("--model") is { } model) args.AddRange(new[] { "--model", model });
        string? temp = null, tempText = null;
        var baseline = o.Get("--baseline") is not null;
        if (!control)
        {
            // The proof's settings: the wiring on this kor, and NOT the project settings (their 4+4 pages would load beside it).
            // Measured 5 Nov on 2.1.318: leaving out the project source also leaves out the project's CLAUDE.md, and --add-dir with
            // CLAUDE_CODE_ADDITIONAL_DIRECTORIES_CLAUDE_MD does not bring it back. So a proof's tokens are not a real start's:
            // --baseline runs the repo's OWN SessionStart lines in the same mode, and a real start after the wiring is the control's
            // tokens + (proof - baseline).
            temp = Path.Combine(o.Get("--temp-dir") ?? Path.GetTempPath(), $"kor-start-proof-{Guid.NewGuid():N}.json");
            var commands = baseline ? ColdProof.CurrentSessionStart(settings) : ColdProof.Wiring(settings, module, exe, wire);
            var hooks = commands.Select(c => new { type = "command", command = c, timeout = 30 }).ToList();
            tempText = JsonSerializer.Serialize(new { hooks = new { SessionStart = new[] { new { hooks } } } });
            args.AddRange(new[] { "--setting-sources", "user,local", "--settings", temp });
        }
        args.Add("--disallowedTools");
        args.AddRange(NoTools);
        var psi = new ProcessStartInfo(o.Get("--claude") ?? Environment.GetEnvironmentVariable("CLAUDE_CODE_EXECPATH") ?? "claude")
        {
            WorkingDirectory = dir, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false),
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        foreach (var e in SessionEnv) psi.Environment.Remove(e);
        var clock = Stopwatch.StartNew();
        string? session = null, result = null;
        double? cost = null;
        try
        {
            if (temp is not null) File.WriteAllText(temp, tempText!, new UTF8Encoding(false));
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("claude did not start");
            var stderr = p.StandardError.ReadToEndAsync();
            var stdout = p.StandardOutput.ReadToEndAsync();
            p.StandardInput.Write(ColdProof.Prompt(questions));
            p.StandardInput.Close();
            if (!p.WaitForExit(TimeSpan.FromMinutes(10))) { p.Kill(true); throw new TimeoutException("the cold session did not finish in 10 minutes"); }
            try
            {
                using var doc = JsonDocument.Parse(stdout.Result);
                session = Str(doc.RootElement, "session_id");
                result = Str(doc.RootElement, "result");
                cost = doc.RootElement.TryGetProperty("total_cost_usd", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDouble() : null;
            }
            catch (JsonException) { throw new InvalidOperationException($"the cold session gave no JSON: {stderr.Result}"); }
        }
        finally { if (temp is not null) File.Delete(temp); }
        if (session is null) throw new InvalidOperationException("the cold session's JSON named no session_id, so it has no transcript to grade");
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
        var transcript = Directory.EnumerateFiles(root, session + ".jsonl", SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new InvalidOperationException($"no transcript for session {session} under {root}");
        var lines = StartGate.ReadLines(transcript).ToList();
        var delivered = ColdProof.Delivered(lines);
        var g = ColdProof.Grade(questions, result, delivered);
        var tokens = ColdProof.TokensAtRequestOne(lines);
        var w = new StringWriter();
        w.WriteLine($"{(control ? "CONTROL" : baseline ? "BASELINE" : "PROOF")} {module}: {g.Passed} of {g.Of} right; invented {g.Invented}; pages in its context {g.PagesHeld} of {g.PagesTotal}, its count {(g.PagesRight ? "right" : "WRONG")}; " +
                    $"tokens at request 1: {tokens?.ToString() ?? "not found"}; delivered {delivered.Length} chars; {clock.Elapsed.TotalSeconds:0} s; cost {cost?.ToString("0.00") ?? "?"} USD; kor {(control ? "installed, the repo's settings" : exe)}; session {session}; transcript {transcript}");
        foreach (var l in g.Lines) w.WriteLine($"  {(l.Pass ? "PASS" : "FAIL")} {l.Key}: {l.Detail}");
        output.Write(w.ToString());
        if (o.Get("--out") is { } outFile) File.AppendAllText(outFile, w.ToString(), new UTF8Encoding(false));
        return control || baseline ? (g.Passed < g.Of ? 0 : 3) : (g.Passed == g.Of ? 0 : 3);
    }
}
