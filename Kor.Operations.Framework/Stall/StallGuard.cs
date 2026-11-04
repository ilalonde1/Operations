using System.Text.RegularExpressions;
using Kor.Operations.Framework.Capture;

namespace Kor.Operations.Framework.Stall;

/// <summary>What a failed call failed ON. <paramref name="Key"/> is compared (lower case, '/' for '\'); <paramref name="Name"/>
/// is the word a diagnosis record must hold to clear it.</summary>
/// <param name="Kind">resource (a mailbox, host, URL, share, path, server named by the command and by the failure),
/// subject (a word the failing line starts with that the command quotes - "jmarkulin [x] ERROR ... Forbidden"),
/// family (the command's program and verb - a failure of the session's own code), query (the exact command - an empty
/// result belongs to the query that found nothing).</param>
public sealed record StallTarget(string Key, string Kind, string Name);

/// <summary>A call's failure: its class, what it failed on, the line of output that says so, and (exit/error) the
/// normalised error text two failures must share.</summary>
public sealed record StallFailure(StallCall Call, string Class, IReadOnlyList<StallTarget> Targets, string Evidence, string? Signature);

public sealed record StallDecision(bool Refuse, string Reason, StallTarget? Target = null, StallFailure? First = null,
                                   StallFailure? Second = null, string? ClearedBy = null)
{
    /// <summary>The other targets this call touches that are stalled too (each needs naming in the record).</summary>
    public IReadOnlyList<StallTarget> Also { get; init; } = Array.Empty<StallTarget>();
}

/// <summary>
/// G1, the stall guard (board row 1308; Ian said YES 3 Nov via the OverLORD, from the NetworkOps session's proposal):
/// when the same TARGET has failed twice in a row in a session, the next attempt on it is REFUSED at PreToolUse, naming
/// both failures, until a diagnosis naming the target is recorded (after the first of the two: a record resets the count).
///
/// WHOSE FAILURE IT IS decides the target (the 3 Nov judges, pass 5): a PERMISSION refusal belongs to the resource, by
/// any route (below); an MCP tool's opaque error, or a timeout of an MCP/WebFetch call, belongs to that ROUTE on that
/// host (the MCP server + the host its identity fields name - never its purpose text); a shell TIMEOUT or EXIT CODE
/// belongs to the COMMAND (its family; only the same command again is a repeat - a script with the hanging step removed
/// is a fix); an EMPTY result belongs to the exact query.
///
/// THE TARGET IS THE RESOURCE, NOT THE COMMAND (seed NETOPS-FORBIDDEN-LOOP, NetworkOps 7b3e2b20): Graph read the
/// jmarkulin/jdesroches mailboxes and got Forbidden at :306 (exit code 0 - the refusal is in the output) and :329, then
/// the session tried the SAME mailboxes through Outlook MAPI (:333, :421) and the same script again (:382, :431). Keyed
/// on the command string, :333 would pass. So a PERMISSION refusal's target is read from the command AND the failing line:
/// <list type="number">
/// <item>a PATH the failing line names ("Access to the path 'E:\Archives' is denied" is E:\Archives, not its host);</item>
/// <item>else RESOURCES the command names (emails and their local part, URLs as host+path, UNC shares and their host, KOR-
/// hosts with a digit, private IPs, domain names, sqlcmd -S servers, absolute paths) that the failing line also names;</item>
/// <item>else SUBJECTS: the first word of a failing line, when the command holds that word inside a quoted string (the
/// seed's "jmarkulin [marrokal] ERROR ... Forbidden" from -BoxList "...,jmarkulin,jdesroches");</item>
/// <item>else the command's resources wherever they are (paths too);</item>
/// <item>else the FAMILY (program + verb/script: "python oq.py", "dotnet test x --filter F", "netops run").</item>
/// </list>
/// A later call is ON a target when its text (command; WebFetch url; an MCP tool's input JSON) names the key as a
/// whole word, case-insensitive, '\' read as '/' - in a shell call, in a step that is not only ABOUT it (git, echo,
/// grep, cat, Get-Content, kor ... name a host without contacting it); a family by its family; a query by the
/// identical command. A record (kor record, kor_brain.py note) is never refused: it is the way out.
///
/// FAILURE CLASSES, read from the OUTPUT (never only the exit code):
/// <list type="bullet">
/// <item>refused: a refusal in a status context - "403 (Forbidden)", "status code does not indicate success: Forbidden",
/// "HTTP 401", "Access is denied", "Access to the path ... is denied", "Permission denied", "Login failed for user",
/// AccessDenied, UnauthorizedAccessException, "System error 5", "not authorized", "insufficient privileges". A bare 401 or
/// 403 is NOT one (the corpus's lexical class held 167 such rows in the window, most of them numbers in data).</item>
/// <item>timeout: "timed out", "timeout expired", "operation has timed out", ETIMEDOUT, TimeoutException. Claude Code's
/// "did not complete within its N timeout and was moved to the background" is NOT one: the command is still running;
/// nor is a reported field ("timed out: True" in a script's summary).</item>
/// <item>exit (shell) / error (MCP, WebFetch): a non-zero exit code (shell) or an error result, with the same normalised
/// error line AND last line twice. grep/diff/robocopy-style exits that report rather than fail are not failures, nor a
/// build or test whose own output says it passed (the code is a later step's).</item>
/// <item>empty: a query (grep, find, ls, Get-ChildItem, Select-String, Get-Content, oq.py, curl ...) that printed nothing.</item>
/// </list>
/// A line of DATA is never read as a failure: in the output of a command whose every segment only reads text (grep, cat,
/// sed, Get-Content ...) only that reader's own error lines count; a grep-format line ("file:12:", "12-") never counts;
/// and a phrase the command itself contains (grep "Forbidden") is not a refusal.
///
/// IN A ROW: a target is stalled when the LAST TWO attempts on it that ran (answered, not refused by a hook, not still
/// running) both failed in the same class on it - and, for exit/error, with the same error line. A success in between
/// starts the count again; so does a NEW TURN started by Ian or a peer session after the second failure (a grant given,
/// a sign-in renewed, "retry" - the world outside answered; a stall is a run of attempts nobody answered). A message
/// typed while the session works (a mid-turn queued message - Ian's "Come on????" at 7b3e2b20:394) is not a turn and
/// re-opens nothing; a prompt BEFORE the second failure re-opens nothing either (the second failure answered it). Both within
/// <see cref="Window"/> of the new attempt. A FAMILY target is the session's OWN
/// command, so only that command again (the text of either failing call, whitespace folded) is refused - a changed
/// command is a new attempt (an inline python -c / -Command script is hashed into its family). A FAMILY or QUERY
/// target is also re-opened by any edit after the second failure, or by a call that itself edits (Edit/Write/MultiEdit/NotebookEdit, or a
/// command that writes: sed -i, Set-Content, Out-File, &gt; file, cp/mv, git checkout/restore/apply/pull ..., a build or
/// install): an edit-run-fail loop is work, not a stall. A RESOURCE's refusal is never re-opened by editing your files.
///
/// CLEARED BY A DIAGNOSIS: a brain.Memory record of kind finding, dead-end, lesson or feedback whose name, description
/// or body holds the target's Name, made after the FIRST of the two failures: a diagnosis RESETS the count, so a new
/// stall needs two new failures after it (the tie-break judge on c6cb3844:18162 - a second record demanded right after
/// the first, before the isolating call, was friction). The row's "after the second failure" is the usual case. Row 1308 says "finding or dead-end"; the lead
/// widened it to lesson and feedback on 3 Nov because the seed's own diagnosis was brain.Memory #119744, a kor_brain.py
/// feedback note (its body names jmarkulin and jdesroches) - and :551, the retry after it, must be allowed. A decision
/// does not count: a decision is not a diagnosis. Live, the record is read from the DB, only when a refusal is pending;
/// the replay reads the transcript's kor record / kor_brain.py note commands and the body files the session wrote.
///
/// TOOLS: Bash, PowerShell, WebFetch and every MCP tool (mcp__*: the NetworkOps run/run_fix loops on KOR-302N). An MCP
/// or WebFetch error counts only when its input names a resource (a host, a URL): the tool alone is too wide a target.
///
/// MEASURED (kor stall --replay, 3 Nov, 565 main transcripts of the 13 KOR folders since 4 Oct): see board row 1308's
/// Evidence for the counts and the judges' verdicts; every refusal judged legitimate work was fixed or is named there.
///
/// DOES NOT COVER: Read/Grep/Glob/Edit/Write and other built-in tools (not judged; an Edit only re-opens); a failure whose
/// words are not in the lists (a refusal phrased "you do not have rights", a hang that prints nothing and is stopped by
/// TaskStop - no failure is ever written); a background run's outcome (its task-notification is not read: only the
/// launch's own result, which is no failure); the same resource named two different ways (a mailbox by its display name
/// in one call and its address in the next); a stall that alternates classes (refused, timeout, refused - never two of a
/// kind in a row); a stall split across sessions or across the parent and its helpers (each transcript is judged alone);
/// failures older than the live tail (<see cref="StallTranscript.TailBytes"/>). The transcript is written asynchronously
/// (Claude Code's docs: it "may lag"): a result not yet on disk is no failure, so missing state only ever ALLOWS - G1
/// never refuses on a result it has not read. No database: the diagnosis cannot be read, so the call is ALLOWED (fails
/// open, said in the hook error log). A diagnosis is matched by WHOLE WORD, in any lane, not by meaning: a record that names the target and
/// says nothing about the failure clears it. A new turn from Ian or a peer re-opens a target whatever it says (one that
/// is about something else re-opens it too), and a mid-turn message never does, even "it's fixed, retry". A shell command that hangs or fails twice and is retried with any change
/// (a flag, a script) is not refused - only a permission refusal follows the resource across commands. A stall inside a
/// helper is judged on the helper's own transcript only when the hook input carries agent_id.
/// Same-class fault it would miss: the seed's own shape through a second name - Graph refused on
/// "jmarkulin@korstructural.com", and the next try opens the mailbox by display name ("John Markulin") through Outlook:
/// no token is shared, so the third attempt runs.
/// </summary>
public static class StallGuard
{
    /// <summary>Both failures must be within this long of the new attempt (set from the dry run - see the replay).</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(2);

    public const int MaxScan = 2_000_000;

    public static readonly string[] DiagnosisKinds = { "finding", "dead-end", "lesson", "feedback" };

    private const RegexOptions I = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>Does G1 judge this tool? Bash, PowerShell, WebFetch and MCP tools.</summary>
    public static bool Judged(string tool) => tool is "Bash" or "PowerShell" or "WebFetch" || tool.StartsWith("mcp__", StringComparison.Ordinal);

    public static bool IsShell(string tool) => tool is "Bash" or "PowerShell";

    // ---- outcomes ------------------------------------------------------------------------------------------------------
    private static readonly Regex Refusal = new(
        @"\b40[13]\b[\s:-]*\(?\s*(?:Forbidden|Unauthorized)\b" +
        @"|\b(?:Forbidden|Unauthorized)\s*\(\s*(?:Forbidden|Unauthorized)\s*\)" +
        @"|status code does not indicate success:\s*(?:\d+\s*)?\(?(?:Forbidden|Unauthorized)" +
        @"|\bHTTP(?:/[\d.]+)?\s+40[13]\b" +
        @"|\bAccess (?:is |was )?denied\b|\bAccess to the path\b.{0,300}?\bis denied" +
        @"|\bPermission denied\b|\bLogin failed for user\b|\b(?:Error)?AccessDenied(?:Exception)?\b|\bUnauthorizedAccessException\b" +
        @"|\bSystem error 5\b|\bnot authori[sz]ed\b|\binsufficient privileges\b", I);

    private static readonly Regex Timeout = new(
        // "timed out: False" is a report that it did NOT (NetworkOps 2b3d2fa7:331, a search's own summary - first pass)
        @"\btimed out\b(?!\s*[:=])|\btimeout expired\b|\boperation has timed out\b|\bETIMEDOUT\b|\bTimeoutException\b|\bdeadline exceeded\b", I);

    private static readonly Regex MovedToBackground = new(@"did not complete within its \S+ timeout and was moved to the background|Command running in background", I);

    private static readonly Regex ExitCode = new(@"\AExit code (?<n>-?\d+)", I);

    // a line of data a reader printed: grep -n "file:12:" / "12-" / cat -n "   12\t"
    private static readonly Regex DataLine = new(@"\A(?:[^\s:]+:\d+[:-]|\s*\d+[:\t-])", RegexOptions.CultureInvariant);

    private static readonly HashSet<string> Readers = new(StringComparer.OrdinalIgnoreCase)
    {
        "grep", "egrep", "fgrep", "rg", "sed", "awk", "cat", "head", "tail", "less", "more", "type", "select-string", "sls",
        "get-content", "gc", "findstr", "jq", "wc", "cut", "sort", "uniq", "tr", "column", "select-object", "where-object",
        "format-table", "ft", "out-string", "echo", "printf", "write-output", "write-host",
        // pass 3: "| fl" was a family (92278189:110)
        "fl", "format-list", "fw", "select", "?", "%", "foreach-object", "sort-object", "measure-object", "out-null", "out-host",
        "group-object", "convertto-json", "convertfrom-json",
    };

    private static readonly HashSet<string> Informational = new(StringComparer.OrdinalIgnoreCase)
    {
        "grep", "egrep", "fgrep", "rg", "findstr", "diff", "fc", "cmp", "comm", "test", "[", "[[", "where", "which", "robocopy",
        "select-string", "sls", "compare-object", "wc", "git diff", "git grep", "git merge-base", "git rev-parse", "git ls-files",
    };

    private static readonly HashSet<string> Queries = new(StringComparer.OrdinalIgnoreCase)
    {
        "grep", "egrep", "fgrep", "rg", "find", "ls", "dir", "gci", "get-childitem", "select-string", "sls", "findstr",
        "get-content", "gc", "cat", "head", "tail", "type", "test-path", "sqlcmd", "oq.py", "oqwide.py", "curl", "wget",
        "invoke-restmethod", "irm", "invoke-webrequest", "iwr", "git log", "git grep", "git ls-files", "git show", "jq",
        "get-process", "get-service", "get-scheduledtask", "get-itemproperty", "resolve-dnsname", "nslookup",
    };

    private static readonly string[] EmptyMarkers =
    {
        "(Bash completed with no output)", "(PowerShell completed with no output)", "No matches found", "No files found", "(no output)",
    };

    private enum Outcome { Unknown, Success, Failure }

    /// <summary>A result that is not a run's outcome: a hook's refusal, the user's, an input error, an interrupt, a
    /// launch in (or moved to) the background.</summary>
    public static bool NotARun(string? output)
    {
        if (output is null) return true;
        var o = output.TrimStart();
        return ToolClassifier.IsHookRefusal(o)
               || o.StartsWith("PreToolUse:", StringComparison.Ordinal) || o.StartsWith("PermissionRequest:", StringComparison.Ordinal)
               || o.StartsWith("The user doesn't want to proceed", StringComparison.Ordinal)
               || o.Contains("tool use was rejected", StringComparison.Ordinal)
               || o.StartsWith("<tool_use_error>", StringComparison.Ordinal)
               || o.StartsWith("[Request interrupted", StringComparison.Ordinal)
               || MovedToBackground.IsMatch(o.Length > 4000 ? o[..2000] + o[^2000..] : o);
    }

    /// <summary>The call's failure, or null (a success, or no outcome to read).</summary>
    public static StallFailure? Classify(StallCall c)
    {
        if (!Judged(c.Tool) || !c.Answered || NotARun(c.Output)) return null;
        var output = c.Output!.Length > MaxScan ? c.Output[..MaxScan] : c.Output;
        var text = c.Text ?? "";
        var shell = IsShell(c.Tool);
        var segments = shell ? Segments(c) : new List<string>();
        var programs = segments.Select(Program).Where(p => p.Length > 0).ToList();
        var readerOnly = shell && programs.Count > 0 && programs.All(p => Readers.Contains(p));
        var lines = output.Replace("\r", "").Split('\n');
        var lowText = text.ToLowerInvariant();

        List<string> Hits(Regex rx)
        {
            var hits = new List<string>();
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                // a markdown heading or a quoted line is a document being printed, not a status ("## card 24: ... Permission
                // denied", 32f94af3:52715 - pass 2)
                if (line.Length == 0 || DataLine.IsMatch(raw) || line.StartsWith('#') || line.StartsWith('>')) continue;
                if (MovedToBackground.IsMatch(line)) continue;
                var m = rx.Match(line);
                if (!m.Success) continue;
                if (lowText.Contains(m.Value.ToLowerInvariant())) continue;                       // the command searched for it
                if (readerOnly && !programs.Any(p => line.StartsWith(p + ":", StringComparison.OrdinalIgnoreCase))) continue;
                hits.Add(line);
            }
            return hits;
        }

        var refused = Hits(Refusal);
        if (refused.Count > 0) return Make(c, "refused", refused, null);
        var timedOut = Hits(Timeout);
        if (timedOut.Count > 0)
        {
            // a timeout is the COMMAND's (what it ran, how long it waited), not the host's: the family, and only the same
            // command again is a repeat (pass 5, judges: a KOR-FS01 search script that hit its own budget twice made every
            // later FS01 call a "repeat"). MCP / WebFetch: the route on the host named in the input.
            if (shell)
            {
                var fam = segments.Select(FamilyOfSegment).LastOrDefault(f => f is not null && !Readers.Contains(f.Split(' ')[0])) ?? FamilyOf(c);
                return fam is null ? null : new StallFailure(c, "timeout", new List<StallTarget> { FamilyTarget(fam) }, Clip(timedOut[0], 200), null);
            }
            var tres = InputResources(c);
            return tres.Count == 0 ? null
                : new StallFailure(c, "timeout", tres.Select(r => new StallTarget($"{RouteOf(c)}|{r.Key}", "route", r.Name)).ToList(), Clip(timedOut[0], 200), null);
        }

        if (shell)
        {
            var em = ExitCode.Match(output.TrimStart());
            // a query that found nothing: no output at all, or only its exit code (grep's "Exit code 1" with no lines)
            var body = em.Success ? output.TrimStart()[em.Length..] : output;
            if (IsEmpty(body) && FamilyOf(c) is { } q && IsQuery(q, segments))
                return new StallFailure(c, "empty", new[] { QueryTarget(c, q) }, em.Success ? $"(no output, {em.Value.ToLowerInvariant()})" : "(no output)", null);
            if (em.Success && int.TryParse(em.Groups["n"].Value, out var code) && code != 0)
            {
                // the exit code is the LAST command's: its family is the target (a ";"-chain's first step is not what failed)
                // (a filter it pipes into - tail, Select-Object - is not it either)
                var fam = segments.Select(FamilyOfSegment).LastOrDefault(f => f is not null && !Readers.Contains(f.Split(' ')[0])) ?? FamilyOf(c);
                var last = segments.Count > 0 ? FamilyOfSegment(segments[^1]) : fam;
                // a build or test whose own output says it passed: the code is a later step's (d8312bb3:41049 "0 Error(s)"
                // then exit 1 - first pass; the same rule as ToolClassifier's)
                var run = ToolClassifier.Classify(text, output, code);
                if (run is { Kind: "build" or "test", Outcome: "pass" }) return null;
                if (fam is not null && !IsInformational(fam) && !(last is not null && IsInformational(last)))
                {
                    var sig = $"exit {code}: {Signature(lines.Skip(1))}";
                    var evidence = Clip(ErrorLine(lines.Skip(1)) ?? $"Exit code {code}", 200);
                    // an exit code is the COMMAND's, not the host's: the target is the family, and only the same command again
                    // is a repeat (pass 5, judges: netops runs on KOR-BK01/KOR-RDS01 whose next call carried a script with
                    // the hanging step removed were fixes, not retries)
                    return new StallFailure(c, "exit", new List<StallTarget> { FamilyTarget(fam) }, evidence, sig);
                }
                return null;
            }
            return null;
        }
        if (c.IsError)
        {
            var sig = Signature(lines);
            var res = InputResources(c);
            // an MCP / WebFetch error with no resource named is no target: the tool alone is too wide (eb9075b2:113986 -
            // Claude Docs answers flagged as errors on three different edits - first pass)
            if (res.Count == 0) return null;
            // an opaque tool error is the ROUTE's on that host (the MCP server, or WebFetch): an admin-share copy to the same
            // host does not go through it (pass 5, judges: \\KOR-210\C$ copies while networkops_run was failing)
            var route = RouteOf(c);
            return new StallFailure(c, "error", res.Select(r => new StallTarget($"{route}|{r.Key}", "route", r.Name)).ToList(),
                                    Clip(ErrorLine(lines) ?? "error", 200), sig);
        }
        return null;
    }

    private static StallFailure Make(StallCall c, string cls, List<string> hits, string? sig)
    {
        var text = c.Text ?? "";
        var lowHits = Norm(string.Join("\n", hits));
        var cmdRes = IsShell(c.Tool) ? Resources(text, paths: true) : InputResources(c);
        // the most specific thing refused: a PATH the failing lines name (a script's own target - "Access to the path
        // 'E:\Archives' is denied" on KOR-FS01 is E:\Archives, not the whole host: pass 6, 2b3d2fa7:514/537)
        var linePaths = Resources(string.Join("\n", hits), paths: true).Where(r => r.Key.Length > 3 && r.Key[1] == ':').ToList();
        if (linePaths.Count > 0)
            return new StallFailure(c, cls, linePaths.GroupBy(t => t.Key).Select(g => g.First()).ToList(), Clip(hits[0], 200), sig);
        var targets = cmdRes.Where(r => Contains(lowHits, r.Key) || Contains(lowHits, r.Name.ToLowerInvariant())).ToList();
        if (targets.Count == 0)
        {
            var quoted = QuotedText(text);
            foreach (var h in hits)
                if (Subject.Match(h) is { Success: true } s && s.Groups[1].Value is var w && !Stop.Contains(w)
                    && quoted.Any(q => Contains(q, w.ToLowerInvariant())))
                    targets.Add(new StallTarget(w.ToLowerInvariant(), "subject", w));
        }
        if (targets.Count == 0) targets = cmdRes;
        if (targets.Count == 0)
        {
            var fam = IsShell(c.Tool) ? FamilyOf(c) : c.Tool.ToLowerInvariant();
            if (fam is not null) targets.Add(FamilyTarget(fam));
        }
        targets = targets.GroupBy(t => t.Key).Select(g => g.First()).ToList();
        return new StallFailure(c, cls, targets, Clip(hits[0], 200), sig);
    }

    // ---- targets ---------------------------------------------------------------------------------------------------------
    private static readonly Regex Email = new(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+", RegexOptions.CultureInvariant);
    private static readonly Regex Url = new(@"https?://[^\s""'<>|()\[\]{}`\\]+", I);
    private static readonly Regex Unc = new(@"(?:\\\\|(?<![:/\w])//)(?<host>[A-Za-z][\w.-]*)[\\/]+(?<share>[^\\/\s""'|;,)]+)", RegexOptions.CultureInvariant);
    private static readonly Regex KorHost = new(@"(?<![\w/\\.-])KOR-(?=[A-Za-z0-9-]*\d)[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*(?![\w/\\-])", I);
    private static readonly Regex PrivateIp = new(@"(?<![\d.])(?:10|127|172|192)\.\d{1,3}\.\d{1,3}\.\d{1,3}(?![\d.])", RegexOptions.CultureInvariant);
    private static readonly Regex Domain = new(@"(?<![\w.@/-])[a-z0-9-]+(?:\.[a-z0-9-]+)*\.(?:com|ca|net|org|io|gov|edu|local)(?![\w.-])", I);
    private static readonly Regex SqlServer = new(@"(?:^|\s)-S\s+[""']?(?<s>[^\s""';]+)", RegexOptions.CultureInvariant);
    private static readonly Regex WinPath = new(@"(?<![\w/])(?:[A-Za-z]:[\\/]|/[a-z]/)[^\s""'|;<>*?]+", RegexOptions.CultureInvariant);
    private static readonly Regex QuotedPath = new(@"[""'](?<p>(?:[A-Za-z]:[\\/]|/[a-z]/)[^""'\n]+)[""']", RegexOptions.CultureInvariant);
    // a status line's first word; a markdown line ("## card 24: ... Permission denied", 32f94af3:52715 - first pass) is data
    private static readonly Regex Subject = new(@"\A[\s=>|\[\]-]*([A-Za-z][\w.@-]{2,})", RegexOptions.CultureInvariant);
    private static readonly Regex Quoted = new(@"""[^""\n]*""|'[^'\n]*'", RegexOptions.CultureInvariant);

    private static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
    {
        "error", "errors", "warning", "warn", "exception", "the", "line", "fatal", "failed", "failure", "fail", "response", "request",
        "http", "https", "invoke", "system", "access", "permission", "denied", "forbidden", "unauthorized", "sqlcmd", "curl", "python",
        "traceback", "file", "cannot", "could", "unable", "status", "code", "msg", "true", "false", "null", "none", "from", "with",
        "and", "for", "connect", "connection", "remote", "server", "timeout", "timed", "write", "read", "info", "debug", "exit",
        "get", "set", "new", "out", "result", "results", "message", "login", "user", "the", "this", "that", "not", "operation",
        "command", "process", "script", "path", "item", "object", "value", "time", "date", "test", "tests", "build", "run",
    };

    /// <summary>The resources a text names (keys normalised). <paramref name="paths"/>: include absolute file paths.</summary>
    public static List<StallTarget> Resources(string text, bool paths)
    {
        var found = new List<StallTarget>();
        var rest = text;
        foreach (Match m in Url.Matches(rest))
        {
            var u = m.Value.TrimEnd('.', ',', ';', ':');
            if (!Uri.TryCreate(u, UriKind.Absolute, out var uri)) continue;
            var key = (uri.Host + uri.AbsolutePath.TrimEnd('/')).ToLowerInvariant();
            found.Add(new StallTarget(key, "resource", uri.Host.ToLowerInvariant()));
        }
        rest = Url.Replace(rest, " ");
        foreach (Match m in Email.Matches(rest))
        {
            var e = m.Value.Trim('.').ToLowerInvariant();
            if (!e.Contains('@')) continue;
            var local = e[..e.IndexOf('@')];
            found.Add(new StallTarget(e, "resource", local));
            if (local.Length >= 3) found.Add(new StallTarget(local, "resource", local));
        }
        rest = Email.Replace(rest, " ");
        foreach (Match m in Unc.Matches(rest))
        {
            var host = m.Groups["host"].Value.ToLowerInvariant();
            found.Add(new StallTarget($"//{host}/{m.Groups["share"].Value.ToLowerInvariant()}", "resource", host));
        }
        foreach (Match m in KorHost.Matches(rest)) found.Add(new StallTarget(m.Value.ToLowerInvariant(), "resource", m.Value));
        foreach (Match m in PrivateIp.Matches(rest)) found.Add(new StallTarget(m.Value, "resource", m.Value));
        foreach (Match m in Domain.Matches(rest))
            if (!m.Value.Contains('/') && !m.Value.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
                found.Add(new StallTarget(m.Value.ToLowerInvariant(), "resource", m.Value.ToLowerInvariant()));
        foreach (Match m in SqlServer.Matches(rest))
        {
            var s = m.Groups["s"].Value.ToLowerInvariant();
            if (!s.StartsWith('$')) found.Add(new StallTarget(s.Replace('\\', '/'), "resource", s.Split('\\', '/', ',')[0]));
        }
        if (paths)
        {
            foreach (Match m in QuotedPath.Matches(rest)) AddPath(m.Groups["p"].Value);
            foreach (Match m in WinPath.Matches(Quoted.Replace(rest, " "))) AddPath(m.Value);
        }
        return found.GroupBy(t => t.Key).Select(g => g.First()).ToList();

        void AddPath(string p)
        {
            var key = NormPath(p.TrimEnd('.', ',', ')', ':'));
            if (key.Length < 6) return;
            var name = key.TrimEnd('/').Split('/').Last();
            if (name.Length < 3) return;
            found.Add(new StallTarget(key, "resource", name));
        }
    }

    // the input fields that NAME what an MCP tool acts on; free text (purpose, script, param, prompt) only mentions things
    // (pass 5, judges: a KOR-302N call whose purpose said "so KOR-210 can be given" counted as a KOR-210 failure)
    private static readonly HashSet<string> IdentityKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "host", "hosts", "device", "devices", "name", "computer", "computername", "server", "target", "url", "uri", "mailbox",
        "user", "email", "share", "path", "file_path",
    };

    /// <summary>The resources a non-shell call acts on: WebFetch's url; an MCP tool's identity fields (host, device, url ...).</summary>
    public static List<StallTarget> InputResources(StallCall c)
    {
        var text = c.Text ?? "";
        if (c.Tool == "WebFetch") return Resources(text, paths: false);
        var values = new List<string>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    if (!IdentityKeys.Contains(p.Name)) continue;
                    if (p.Value.ValueKind == System.Text.Json.JsonValueKind.String) values.Add(p.Value.GetString()!);
                    else if (p.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
                        values.AddRange(p.Value.EnumerateArray().Where(v => v.ValueKind == System.Text.Json.JsonValueKind.String).Select(v => v.GetString()!));
                }
        }
        catch (System.Text.Json.JsonException) { return new List<StallTarget>(); }
        return values.SelectMany(v => Resources(" " + v + " ", paths: true)).GroupBy(t => t.Key).Select(g => g.First()).ToList();
    }

    /// <summary>The route a non-shell call takes: its MCP server ("mcp__networkops"), or "webfetch".</summary>
    public static string RouteOf(StallCall c)
    {
        if (c.Tool.StartsWith("mcp__", StringComparison.Ordinal))
        {
            var parts = c.Tool.Split("__");
            return parts.Length >= 2 ? "mcp__" + parts[1].ToLowerInvariant() : c.Tool.ToLowerInvariant();
        }
        return c.Tool.ToLowerInvariant();
    }

    public static string NormPath(string p)
    {
        var s = p.Replace('\\', '/').ToLowerInvariant();
        if (Regex.IsMatch(s, @"\A/[a-z]/")) s = s[1] + ":" + s[2..];
        // a JSON-escaped path ("E:\\Archives") is the same path: runs of '/' after the first character fold to one
        // (only inside a drive path: a UNC "//host" and a URL keep theirs)
        return Regex.Replace(s, @"(?<=\b[a-z]:/?[^\s""']*)/{2,}", "/");
    }

    private static string Norm(string s) => NormPath(s);

    private static List<string> QuotedText(string text) => Quoted.Matches(text).Select(m => m.Value.ToLowerInvariant()).ToList();

    /// <summary>Does <paramref name="hay"/> (lower case) hold <paramref name="key"/> as a whole word?</summary>
    public static bool Contains(string hay, string key)
    {
        if (key.Length == 0) return false;
        var i = 0;
        while ((i = hay.IndexOf(key, i, StringComparison.Ordinal)) >= 0)
        {
            var before = i == 0 ? ' ' : hay[i - 1];
            var after = i + key.Length >= hay.Length ? ' ' : hay[i + key.Length];
            if (!IsWord(before) && !IsWord(after)) return true;
            i++;
        }
        return false;
    }

    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';

    // ---- families and queries ------------------------------------------------------------------------------------------
    private static readonly HashSet<string> Trivial = new(StringComparer.OrdinalIgnoreCase)
    {
        "cd", "set-location", "sl", "pushd", "popd", "date", "get-date", "echo", "printf", "write-host", "write-output", "set",
        "export", "source", "sleep", "start-sleep", "true", ":", "set -o", "clear", "cls", "$errorActionPreference", "import-module",
        "exit", "return", "throw",
    };

    private static readonly HashSet<string> TwoWord = new(StringComparer.OrdinalIgnoreCase)
    {
        "dotnet", "git", "netops", "kor", "npm", "gh", "az", "docker", "winget", "pip", "kubectl", "claude",
    };

    private static readonly HashSet<string> Interp = new(StringComparer.OrdinalIgnoreCase)
    {
        "python", "python3", "py", "node", "pwsh", "powershell", "bash", "sh", "&", ".", "call", "ruby", "perl",
    };

    private static readonly Regex ScriptExtension = new(@"\.(?:py|ps1|psm1|js|mjs|sh|bash|rb|pl|cmd|bat)[""']?\z", I);

    private static List<string> Segments(StallCall c) =>
        CommandSegments.Split(c.Text ?? "", c.Tool == "PowerShell" ? Shell.PowerShell : Shell.Bash).Select(s => s.Trim()).ToList();

    private static List<string> Words(string segment) => Enroll.EnrollCheck.SplitArgs(segment);

    private static string Base(string w)
    {
        var s = w.Replace('\\', '/').TrimEnd('/');
        var b = s.Contains('/') ? s[(s.LastIndexOf('/') + 1)..] : s;
        if (b.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) b = b[..^4];
        return b.ToLowerInvariant();
    }

    /// <summary>The program a segment runs (its first word's base name), "" for an assignment or nothing.</summary>
    private static string Program(string segment)
    {
        var w = Words(segment);
        if (w.Count == 0) return "";
        if (w[0].StartsWith('$') && w.Count > 1 && w[1] == "=") return "";
        if (Regex.IsMatch(w[0], @"\A\$\w+=")) return "";
        // PowerShell's call operators: "& C:/.../netops.exe run" runs netops
        if (w[0] is "&" or "." && w.Count > 1) return Base(w[1]);
        return Base(w[0]);
    }

    /// <summary>The family of a segment: its program, plus the script an interpreter runs or the verb of a two-word
    /// tool, plus a dotnet verb's project (null = an assignment or a trivial step).</summary>
    public static string? FamilyOfSegment(string segment)
    {
        var w = Words(segment).Where(x => x.Length > 0).ToList();
        if (w.Count == 0) return null;
        if (w[0].StartsWith('$') || Regex.IsMatch(w[0], @"\A\w+=")) return null;
        var prog = Base(w[0]);
        // "1" of a "2>&1" the splitter cut, a "}" or ")" - no program (pass 3: family "1")
        if (prog.Length == 0 || (!char.IsLetter(prog[0]) && prog is not ("&" or "." or "[" or "[["))) return null;
        if (Trivial.Contains(prog) || (prog == "set" && w.Count > 1 && w[1] == "-o")) return null;
        var args = w.Skip(1).Where(a => !a.StartsWith('-')).ToList();
        if (Interp.Contains(prog))
        {
            // inline code IS the session's own code: a changed script is a different family (NetworkOps 7b3e2b20:652/658/665
            // - three different python -c scripts, the third fixed, were one family before this)
            var ci = w.FindIndex(a => a is "-c" or "-Command" or "-EncodedCommand");
            if (ci >= 0) return $"{prog} -c#{Fnv(NormCommand(string.Join(" ", w.Skip(ci + 1))))}";
            // the script: the first argument with a script's extension (a flag's value - "-ExecutionPolicy Bypass" - is not
            // one: 372fa4b9:55446 read "powershell bypass" as a family - first pass), else the first non-flag argument
            var script = args.FirstOrDefault(a => ScriptExtension.IsMatch(a)) ?? args.FirstOrDefault(a => !a.StartsWith('-'));
            return script is null ? prog : $"{(prog is "&" or "." or "call" ? "" : prog + " ")}{Base(script)}".Trim();
        }
        if (TwoWord.Contains(prog))
        {
            var verb = args.FirstOrDefault();
            if (verb is null) return prog;
            if (prog == "dotnet" && verb is "test" or "build" or "run" && args.Count > 1)
            {
                // a test run's target is the tests it selects: another --filter is another target (32f94af3:23338 - pass 2)
                var fi = w.FindIndex(a => a.Equals("--filter", StringComparison.OrdinalIgnoreCase));
                var filter = verb == "test" && fi >= 0 && fi + 1 < w.Count ? " --filter " + w[fi + 1] : "";
                return $"dotnet {verb} {Base(args[1])}{filter}";
            }
            return $"{prog} {verb.ToLowerInvariant()}";
        }
        return prog;
    }

    /// <summary>The call's family: its first non-trivial segment's.</summary>
    public static string? FamilyOf(StallCall c) =>
        IsShell(c.Tool) ? Segments(c).Select(FamilyOfSegment).FirstOrDefault(f => f is not null) : c.Tool.ToLowerInvariant();

    private static bool IsInformational(string family)
    {
        var parts = family.Split(' ');
        return Informational.Contains(family) || Informational.Contains(parts[0])
               || (parts.Length > 1 && Informational.Contains(parts[0] + " " + parts[1]));
    }

    private static bool IsQuery(string family, List<string> segments)
    {
        var parts = family.Split(' ');
        if (Queries.Contains(family) || Queries.Contains(parts[0]) || Queries.Contains(parts[^1])) return true;
        return segments.Select(FamilyOfSegment).Any(f => f is not null && (Queries.Contains(f) || Queries.Contains(f.Split(' ')[^1])));
    }

    private static StallTarget FamilyTarget(string family)
    {
        var parts = family.Split(' ');
        // the whole family is the name a record must hold ("netops run", not "run" - a finding saying "running" is no
        // diagnosis of it: review row 407); an inline script's is its interpreter
        var name = parts[^1].StartsWith("-c#", StringComparison.Ordinal) ? parts[0] : family;
        return new(family.ToLowerInvariant(), "family", name);
    }

    private static string Fnv(string s)
    {
        uint h = 2166136261;
        foreach (var ch in s) { h ^= ch; h *= 16777619; }
        return h.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static string NormCommand(string text) => Regex.Replace(text.Trim(), @"\s+", " ").ToLowerInvariant();

    private static StallTarget QueryTarget(StallCall c, string family) => new(NormCommand(c.Text ?? ""), "query", family.Split(' ')[^1]);

    private static bool IsEmpty(string output)
    {
        var o = output.Trim();
        return o.Length == 0 || EmptyMarkers.Any(m => o.Equals(m, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly Regex ErrorWord = new(@"error|exception|fail|denied|refused|not found|cannot|can't|invalid|fatal|traceback|unable", I);

    private static string? FirstErrorLine(IEnumerable<string> lines)
    {
        var l = lines.Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        return l.FirstOrDefault(x => ErrorWord.IsMatch(x)) ?? l.FirstOrDefault();
    }

    /// <summary>The line that says what failed: a Python traceback's LAST line (its exception; the first line is always
    /// "Traceback (most recent call last):"), else the first line with an error word, else the first line.</summary>
    private static string? ErrorLine(IEnumerable<string> lines)
    {
        var l = lines.Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        if (l.Any(x => x.StartsWith("Traceback (most recent call last)", StringComparison.Ordinal)))
            return l.LastOrDefault(x => Regex.IsMatch(x, @"\A[\w.]+(?:Error|Exception|Exit|Interrupt)\b")) ?? l[^1];
        return FirstErrorLine(l);
    }

    /// <summary>The error line with digits, hex ids and spacing folded, so a re-run's timestamps do not split it.</summary>
    /// Plus the LAST line: two failures are the same only when they end the same way too (d8312bb3:66532/66540 - the same
    /// first line "base64 length 96", then two different sqlcmd errors - pass 2).
    public static string Signature(IEnumerable<string> lines)
    {
        var l = lines.Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        static string Fold(string line)
        {
            var s = Regex.Replace(line.ToLowerInvariant(), @"[0-9a-f]{8,}|\d+", "#");
            s = Regex.Replace(s, @"\s+", " ");
            return s.Length > 160 ? s[..160] : s;
        }
        return Fold(ErrorLine(l) ?? "") + " | " + Fold(l.LastOrDefault() ?? "");
    }

    // ---- on target, edits, the decision ---------------------------------------------------------------------------------
    public static bool OnTarget(StallCall c, StallTarget t)
    {
        if (!Judged(c.Tool)) return false;
        var f = FactsMemo.GetValue(c, x =>
        {
            var segs = IsShell(x.Tool) ? Segments(x) : new List<string>();
            return new Facts(
                IsShell(x.Tool) ? segs.Select(FamilyOfSegment).Where(s => s is not null).Select(s => s!.ToLowerInvariant()).ToList()
                                : new List<string> { x.Tool.ToLowerInvariant() },
                NormCommand(x.Text ?? ""), NormPath(x.Text ?? ""),
                segs.Select(s => (Program(s), NormPath(s))).ToList());
        });
        if (t.Kind == "family") return f.Families.Contains(t.Key);
        if (t.Kind == "query") return f.Command == t.Key;
        if (!IsShell(c.Tool))
        {
            // a non-shell call names what it acts on in its identity fields only (never its purpose text)
            var ids = f.Identity ??= string.Join("\n", InputResources(c).Select(r => r.Key));
            if (t.Kind != "route") return Contains(ids, t.Key);
            var bar = t.Key.IndexOf('|');
            return bar > 0 && RouteOf(c) == t.Key[..bar] && Contains(ids, t.Key[(bar + 1)..]);
        }
        if (t.Kind == "route") return false;   // a shell call does not go through an MCP server or WebFetch
        if (!Contains(f.Path, t.Key)) return false;
        // a shell call is ON a resource when a step that is not only ABOUT it names it: a commit message, an echo, a
        // search of files or a record naming KOR-302N does not contact KOR-302N (pass 4: a git commit in c6cb3844:18983
        // was a "repeat" attempt on the host) - and a `kor record` naming the target is its diagnosis, never refused
        return f.Segments.Any(s => Contains(s.Text, t.Key) && !About.Contains(s.Program));
    }

    /// <summary>Programs whose naming of a resource is not an attempt on it: they write or search text about it.</summary>
    private static readonly HashSet<string> About = new(StringComparer.OrdinalIgnoreCase)
    {
        "git", "gh", "echo", "printf", "write-host", "write-output", "kor", "grep", "egrep", "rg", "select-string", "sls", "findstr",
        "get-content", "gc", "cat", "type", "sed", "awk", "head", "tail", "jq", "wc", "sort", "uniq", "less", "more",
    };

    private sealed record Facts(List<string> Families, string Command, string Path, List<(string Program, string Text)> Segments)
    {
        public string? Identity { get; set; }
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<StallCall, Facts> FactsMemo = new();

    private static readonly Regex WritesShell = new(
        @"\bsed\s+(?:-\w*\s+)*-i|\bSet-Content\b|\bAdd-Content\b|\bOut-File\b|\btee\b|\bCopy-Item\b|\bMove-Item\b|\bRemove-Item\b|\bNew-Item\b" +
        @"|(?:^|[;&|(\s])(?:cp|mv|rm|mkdir|touch|patch)\s|\bgit\s+(?:checkout|restore|apply|stash|reset|pull|merge|rebase|cherry-pick|switch)\b" +
        @"|\bdotnet\s+(?:build|publish|restore)\b|\b(?:npm|pip)\s+install\b|install-kor|\bkor(?:\.exe)?[""']?\s+(?:record|state)\b" +
        @"|(?<![\d&>-])>{1,2}\s*(?!&|/dev/null|\$null|nul\b)[^\s|;&>]", I);

    /// <summary>Did the session change something it runs (a file edit, or a command that writes)?</summary>
    public static bool IsEdit(StallCall c) =>
        c.Tool is "Edit" or "Write" or "MultiEdit" or "NotebookEdit" || (IsShell(c.Tool) && WritesShell.IsMatch(c.Text ?? ""));

    /// <summary>
    /// The decision for <paramref name="next"/>, given the calls of the session that RAN before it (in order; calls a hook
    /// refused and calls with no result yet are skipped as no outcome). <paramref name="diagnosed"/> answers "is there a
    /// diagnosis naming this target made after this time?" with the record that clears it, or null.
    /// </summary>
    public static StallDecision Decide(StallCall next, IReadOnlyList<StallCall> history, Func<StallTarget, DateTimeOffset, string?> diagnosed,
                                       DateTimeOffset now, TimeSpan? window = null)
    {
        if (!Judged(next.Tool) || string.IsNullOrEmpty(next.Text)) return new StallDecision(false, "not a judged tool");
        // the diagnosis itself (kor record, kor_brain.py note) is never refused, whatever it names
        // - only when the call does nothing else: "kor record ...; <retry>" is a retry (review row 407)
        // (a filter it pipes into - "| Select-Object -Last 5", seed :446 - is not something else)
        if (IsShell(next.Tool) && Segments(next).Where(s => FamilyOfSegment(s) is { } f && !Readers.Contains(f.Split(' ')[0])).ToList() is { Count: > 0 } work
            && work.All(s => RecordCmd.IsMatch(s)))
            return new StallDecision(false, "a record (the way out) is never refused");
        var since = now - (window ?? Window);
        var recent = history.Where(h => h.Answered && (h.ResultAt ?? h.At) is { } t && t >= since).ToList();
        if (recent.Count == 0) return new StallDecision(false, "no call in the window");
        static StallFailure? F(StallCall c) => Memo.GetValue(c, x => new Box(Classify(x))).Failure;
        var candidates = recent.Select(F).Where(f => f is not null).SelectMany(f => f!.Targets).GroupBy(t => t.Key).Select(g => g.First())
                               .Where(t => OnTarget(next, t)).ToList();
        string? cleared = null;
        var stalled = new List<(StallTarget T, StallFailure A, StallFailure B)>();
        foreach (var t in candidates)
        {
            // the last two attempts on t that RAN (an outcome to read)
            var attempts = recent.Where(c => Judged(c.Tool) && !NotARun(c.Output) && OnTarget(c, t)).ToList();
            if (attempts.Count < 2) continue;
            var a = F(attempts[^2]);
            var b = F(attempts[^1]);
            if (a is null || b is null || a.Class != b.Class) continue;
            if (!a.Targets.Any(x => x.Key == t.Key) || !b.Targets.Any(x => x.Key == t.Key)) continue;
            if (a.Signature != b.Signature) continue;
            // a message from Ian or a peer since the second failure: the world outside may have changed (pass 7, judges:
            // c6cb3844:23778, the probe sent after a peer said "Ian has renewed the NetworkOps sign-in"). A stall is a run
            // of attempts nobody outside answered.
            if (history.Any(c => c.Tool == StallTranscript.Prompt && c.Line > b.Call.Line)) continue;
            // the call itself changes what it runs (sed -i ... && dotnet build: d8312bb3:41079 - first pass)
            if (t.Kind is "family" or "query" && IsEdit(next)) continue;
            // a family is the session's OWN command: one it has changed is a new attempt, not a repeat - only the same
            // command again (either failing one) is refused (92278189:110, an inline PowerShell parse rewritten - pass 3)
            if (t.Kind == "family" && NormCommand(next.Text) != NormCommand(a.Call.Text ?? "") && NormCommand(next.Text) != NormCommand(b.Call.Text ?? ""))
                continue;
            if (t.Kind is "family" or "query" && recent.Any(c => c.Line > b.Call.Line && IsEdit(c))
                || t.Kind is "family" or "query" && history.Any(c => c.Line > b.Call.Line && !c.Answered && IsEdit(c)))
                continue;
            // a diagnosis RESETS the count: one made after the FIRST of the two failures leaves at most one failure since it
            // ("allowed again once a diagnosis exists" - a new stall needs two new failures; tie-break judge on c6cb3844:18162,
            // the isolating call after the record and one more failure)
            var firstAt = a.Call.ResultAt ?? a.Call.At ?? now;
            if (diagnosed(t, firstAt) is { } rec) { cleared ??= $"'{t.Name}' failed twice, cleared by {rec}"; continue; }
            stalled.Add((t, a, b));
        }
        if (stalled.Count == 0) return new StallDecision(false, cleared ?? "no target of this call failed twice in a row", ClearedBy: cleared);
        // every stalled target the call touches is named, so one record can clear them all (pass 6: the seed's Graph
        // failures named jmarkulin AND jdesroches; a deny naming one left the other refused at :382)
        var (t0, a0, b0) = stalled[0];
        var others = stalled.Skip(1).Select(s => s.T).ToList();
        var reason = Reason(t0, a0, b0) + (others.Count == 0 ? "" : $" Also stalled in this call: {string.Join(", ", others.Select(o => $"'{o.Name}'"))} - name each in the record.");
        return new StallDecision(true, reason, t0, a0, b0) { Also = others };
    }

    // a call's classification, kept per call object (reference identity): a replay decides thousands of calls over the
    // same history
    private sealed record Box(StallFailure? Failure);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<StallCall, Box> Memo = new();

    private static string Local(StallCall c) =>
        (c.ResultAt ?? c.At) is { } t ? t.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) : "?";

    private static string Where(StallCall c) => c.Line > 0 ? $"line {c.Line}" : "transcript";

    public static string Reason(StallTarget t, StallFailure a, StallFailure b)
    {
        string What(StallFailure f) => $"{f.Class} at {Local(f.Call)} ({Where(f.Call)}): \"{SecretGuard.Mask(f.Evidence)}\"";
        var how = t.Kind switch
        {
            "family" => $"the command family '{t.Key}'",
            "query" => $"this exact query ({t.Name})",
            _ => $"'{t.Name}'",
        };
        return $"kor stall (G1 stall guard, board row 1308): {how} failed twice in a row in this session - {What(a)}; then {What(b)}. " +
               $"A third attempt on it is refused until a diagnosis naming '{t.Name}' is recorded (a record made since the first of these failures counts): " +
               $"kor record --module <lane> --kind finding (or dead-end) --name <name> --says \"what the two failures mean for {t.Name} ...\" --source ... " +
               "(a lesson, or a kor_brain.py note of kind feedback, naming it counts too; a decision does not). " +
               (t.Kind is "family" or "query" ? "An edit to what it runs re-opens it. " : "Another route to the same resource is the same target. ") +
               "Wrong refusal? Say so to the lead with this text (board row 1308).";
    }

    // ---- the replay's diagnosis: read off the transcript -----------------------------------------------------------------
    private static readonly Regex RecordCmd = new(@"\bkor(?:\.exe)?[""']?\s+record\b|kor_brain\.py[""']?\s+note\b", I);
    private static readonly Regex KindArg = new(@"--kind[\s=]+[""']?(?<k>[\w-]+)", I);

    /// <summary>
    /// The replay's stand-in for the DB: a kor record / kor_brain.py note command after <paramref name="after"/> that did not
    /// fail, of a diagnosis kind, whose text - with the content of any file the session Wrote that the command names (its
    /// --body-file) - holds the target's name. Returns "transcript line N" or null.
    /// </summary>
    public static string? DiagnosedInTranscript(IReadOnlyList<StallCall> history, StallTarget t, DateTimeOffset after)
    {
        var name = t.Name.ToLowerInvariant();
        foreach (var c in history)
        {
            if (!IsShell(c.Tool) || (c.At ?? DateTimeOffset.MinValue) <= after || !RecordCmd.IsMatch(c.Text ?? "")) continue;
            if (c.IsError || c.Output is null || NotARun(c.Output)) continue;
            var kind = KindArg.Match(c.Text!) is { Success: true } k ? k.Groups["k"].Value.ToLowerInvariant() : null;
            if (kind is null || !DiagnosisKinds.Contains(kind)) continue;
            var text = (c.Text ?? "").ToLowerInvariant();
            var bodies = history.Where(w => w.Tool == "Write" && w.Content is not null && w.Line < c.Line
                                            && text.Contains(Base(w.Text), StringComparison.Ordinal))
                                .Select(w => w.Content!.ToLowerInvariant());
            if (Contains(text, name) || bodies.Any(b => Contains(b, name)))   // a whole word (review row 407)
                return $"transcript line {c.Line} (kor record/note --kind {kind})";
        }
        return null;
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 3)] + "...";
}
