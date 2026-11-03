using System.Data;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kor.Operations.Framework.Capture;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.Framework.Records;

/// <summary>What one run of an issue's check gave. Outcome: pass | fail | error | dirty (171's CK_Event_CheckShape).</summary>
public sealed record CheckResult(string Outcome, string? Repo, string? Commit, string Detail, int? Total = null);

/// <summary>
/// K5b - an issue is cleared only by evidence (Framework docs/KNOWLEDGE-IDE-SPEC-K5-K6.md). Runs an issue's check:
/// <list type="bullet">
/// <item>test - CheckRef "&lt;repo&gt;/&lt;path to .csproj&gt;|&lt;fully qualified test name&gt;": <c>dotnet test &lt;proj&gt;
/// --no-restore --filter FullyQualifiedName=&lt;fqn&gt;</c>; pass only if the run classifies pass AND Total &gt;= 1
/// (0 tests = error - a filter that matches nothing proves nothing).</item>
/// <item>gate - the argv of an allowlisted exe (kor, netops, dotnet); exit 0 = pass.</item>
/// <item>query - "brain:SELECT ..." or "board:SELECT ..." in a transaction that is rolled back: exactly 1 row with a bit
/// column Pass.</item>
/// </list>
/// A dirty tree (<c>git status --porcelain --untracked-files=no</c>) is 'dirty', never evidence: the code under test is not
/// the commit the event would cite. 'source' issues are resolved by <c>kor resolve</c> (their file changed), not run.
/// Not covered: whether the check really tests the issue (SELECT 1 AS Pass clears it); a test or gate issue regresses
/// unseen if nobody runs its check. Same-class miss: a query issue regressing in another module's data between sessions.
/// </summary>
public static class IssueCheck
{
    /// <summary>Runs a process: (exit code, stdout+stderr), or null when it could not start or overran.</summary>
    public delegate (int Exit, string Output)? ProcessRunner(string exe, IReadOnlyList<string> args, string dir, TimeSpan timeout);

    public static readonly string[] GateAllowlist = { "kor", "netops", "dotnet" };

    private static readonly Regex TotalLine = new(@"\bTotal(?: tests)?:\s*(\d+)", RegexOptions.CultureInvariant);

    public static CheckResult Run(StoredRecord issue, TimeSpan timeout, string? fromDir = null, ProcessRunner? runner = null)
    {
        runner ??= RealRunner;
        if (issue.Kind != "issue" || issue.CheckKind is null || issue.CheckRef is null)
            return new CheckResult("error", null, null, $"#{issue.Id} is not an issue with a check");
        return issue.CheckKind switch
        {
            "test" => Test(issue.CheckRef, timeout, fromDir, runner),
            "gate" => Gate(issue, timeout, fromDir, runner),
            "query" => Query(issue, timeout, fromDir),
            _ => new CheckResult("error", null, null, $"a {issue.CheckKind} check is not run: `kor resolve --issue {issue.Id}` once its file changed"),
        };
    }

    // ---- test -------------------------------------------------------------------------------------------------------
    /// <summary>"Repo/path/X.csproj|Fqn" -> (repo leaf, project path in the repo, fqn), or null.</summary>
    public static (string Repo, string Project, string Fqn)? ParseTestRef(string checkRef)
    {
        var bar = checkRef.LastIndexOf('|');
        if (bar <= 0 || bar == checkRef.Length - 1) return null;
        var left = checkRef[..bar].Replace('\\', '/').Trim();
        var fqn = checkRef[(bar + 1)..].Trim();
        var slash = left.IndexOf('/');
        if (slash <= 0 || !left.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || fqn.Length == 0 || fqn.Contains(' ')) return null;
        return (left[..slash], left[(slash + 1)..], fqn);
    }

    private static CheckResult Test(string checkRef, TimeSpan timeout, string? fromDir, ProcessRunner runner)
    {
        if (ParseTestRef(checkRef) is not { } t)
            return new CheckResult("error", null, null, $"test check ref '{checkRef}' is not '<repo>/<path>.csproj|<fully qualified name>'");
        var dir = RepoLocator.Find(t.Repo, fromDir);
        if (dir is null) return new CheckResult("error", t.Repo, null, $"repo {t.Repo} is not on this machine");
        var (commit, dirty) = HeadAndDirty(dir);
        if (commit is null) return new CheckResult("error", t.Repo, null, $"git failed in {dir}");
        if (dirty is not null) return new CheckResult("dirty", t.Repo, commit, $"uncommitted changes - not evidence: {dirty}");
        var run = runner("dotnet", new[] { "test", Path.Combine(dir, t.Project), "--no-restore", "--filter", $"FullyQualifiedName={t.Fqn}" },
                         dir, timeout);
        if (run is null) return new CheckResult("error", t.Repo, commit, $"dotnet test did not start or overran {timeout.TotalSeconds:0} s");
        return Classify(t.Repo, commit, run.Value.Exit, run.Value.Output);
    }

    /// <summary>A test run's verdict: pass only when it classifies pass AND ran at least one test.</summary>
    public static CheckResult Classify(string? repo, string? commit, int exit, string output)
    {
        var total = TotalLine.Matches(output).Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(-1).Last();
        var c = ToolClassifier.Classify("dotnet test", output, exit);
        var detail = Tail(output, 1500);
        if (total == 0 || (total < 0 && c?.Outcome == "pass"))
            return new CheckResult("error", repo, commit, "0 tests ran - the filter matches nothing, which proves nothing\n" + detail, Math.Max(total, 0));
        return c?.Outcome switch
        {
            "pass" => new CheckResult("pass", repo, commit, detail, total),
            "fail" => new CheckResult("fail", repo, commit, detail, total),
            _ => new CheckResult("error", repo, commit, "no pass/fail summary\n" + detail, total < 0 ? null : total),
        };
    }

    // ---- gate -------------------------------------------------------------------------------------------------------
    private static CheckResult Gate(StoredRecord issue, TimeSpan timeout, string? fromDir, ProcessRunner runner)
    {
        var argv = SplitArgs(issue.CheckRef!);
        var exe = argv.Count == 0 ? "" : Path.GetFileNameWithoutExtension(argv[0]).ToLowerInvariant();
        if (!GateAllowlist.Contains(exe))
            return new CheckResult("error", null, null, $"gate '{argv.FirstOrDefault()}' is not one of {string.Join(", ", GateAllowlist)}");
        var dir = ModuleDir(issue, fromDir);
        if (dir is null) return new CheckResult("error", null, null, $"the repo of lane {issue.Lane} is not on this machine");
        var (commit, dirty) = HeadAndDirty(dir);
        var repo = Git.Leaf(dir);
        if (commit is null) return new CheckResult("error", repo, null, $"git failed in {dir}");
        if (dirty is not null) return new CheckResult("dirty", repo, commit, $"uncommitted changes - not evidence: {dirty}");
        var path = exe switch
        {
            "kor" => @"C:\KOR-Operations\kor\kor.exe",
            "netops" => @"C:\KOR-Operations\cli\netops.exe",
            _ => "dotnet",
        };
        var run = runner(path, argv.Skip(1).ToList(), dir, timeout);
        if (run is null) return new CheckResult("error", repo, commit, $"{exe} did not start or overran {timeout.TotalSeconds:0} s");
        return new CheckResult(run.Value.Exit == 0 ? "pass" : "fail", repo, commit, $"exit {run.Value.Exit}\n{Tail(run.Value.Output, 1500)}");
    }

    // ---- query ------------------------------------------------------------------------------------------------------
    private static CheckResult Query(StoredRecord issue, TimeSpan timeout, string? fromDir)
    {
        var dir = ModuleDir(issue, fromDir);
        var repo = dir is null ? null : Git.Leaf(dir);
        var commit = dir is null ? null : HeadAndDirty(dir).Commit;
        if (commit is null) return new CheckResult("error", repo, null, $"the repo of lane {issue.Lane} is not on this machine (a check event cites a commit)");
        var (login, sql) = ParseQueryRef(issue.CheckRef!);
        if (sql is null) return new CheckResult("error", repo, commit, "a query check ref is 'brain:SELECT ...' or 'board:SELECT ...'");
        try
        {
            // never committed: a check reads, it never writes - under SNAPSHOT (READ COMMITTED if refused), so a check at
            // kor start never waits on another session's open transaction (3 Nov, migration 174)
            var (rows, pass) = BrainDb.Read(login, q =>
            {
                using var c = q.Command(sql, Math.Max(1, (int)timeout.TotalSeconds));
                using var r = c.ExecuteReader();
                int? p = null;
                var n = 0;
                while (r.Read())
                {
                    n++;
                    var col = Enumerable.Range(0, r.FieldCount).FirstOrDefault(i => string.Equals(r.GetName(i), "Pass", StringComparison.OrdinalIgnoreCase), -1);
                    if (r.FieldCount != 1 || col < 0 || r.GetFieldType(col) != typeof(bool) || r.IsDBNull(col)) p = -1;
                    else p = r.GetBoolean(col) ? 1 : 0;
                }
                return (n, p);
            });
            if (rows != 1 || pass is null or -1)
                return new CheckResult("error", repo, commit, $"a query check returns exactly 1 row with 1 bit column Pass - got {rows} row(s)");
            return new CheckResult(pass == 1 ? "pass" : "fail", repo, commit, $"Pass = {pass == 1}");
        }
        catch (SqlException e) { return new CheckResult("error", repo, commit, $"query failed: {e.Message}"); }
    }

    public static (KorStandardsConnection.Login Login, string? Sql) ParseQueryRef(string checkRef)
    {
        var t = checkRef.Trim();
        var (login, rest) = t.StartsWith("brain:", StringComparison.OrdinalIgnoreCase) ? (KorStandardsConnection.Login.Brain, t[6..])
                          : t.StartsWith("board:", StringComparison.OrdinalIgnoreCase) ? (KorStandardsConnection.Login.Board, t[6..])
                          : (KorStandardsConnection.Login.Brain, (string?)null);
        if (rest is null) return (login, null);
        rest = rest.Trim();
        return Regex.IsMatch(rest, @"^(SELECT|WITH)\b", RegexOptions.IgnoreCase) && !Writes.IsMatch(rest) && !rest.Contains(';')
            ? (login, rest) : (login, null);
    }

    /// <summary>
    /// A query check or source READS. Refused up front: any write or DDL keyword as a whole word (a CTE can end in UPDATE,
    /// SELECT ... INTO creates a table; review row 165) - even inside a string literal, which is the price of not parsing
    /// T-SQL. The second line of defence is the transaction, which is always rolled back.
    /// </summary>
    private static readonly Regex Writes = new(
        @"\b(INSERT|UPDATE|DELETE|MERGE|INTO|EXEC|EXECUTE|DROP|ALTER|CREATE|TRUNCATE|GRANT|DENY|REVOKE|BACKUP|RESTORE|DBCC|SHUTDOWN|KILL|OPENROWSET|OPENQUERY|OPENDATASOURCE|WAITFOR)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // ---- plumbing ---------------------------------------------------------------------------------------------------
    private static string? ModuleDir(StoredRecord issue, string? fromDir) =>
        RepoLocator.Find(issue.Lane, fromDir) ?? Git.TopLevel(fromDir ?? Environment.CurrentDirectory);

    /// <summary>HEAD (lowercase) and the first dirty tracked path, or null when clean.</summary>
    public static (string? Commit, string? Dirty) HeadAndDirty(string dir)
    {
        var head = RecordStore.Lower(Git.Run(dir, "rev-parse", "HEAD")?.Trim());
        var status = Git.Run(dir, "status", "--porcelain", "--untracked-files=no");
        if (status is null) return (null, null);
        var lines = status.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (head, lines.Length == 0 ? null : $"{lines.Length} file(s), e.g. {lines[0]}");
    }

    public static List<string> SplitArgs(string s)
    {
        var args = new List<string>();
        var cur = new StringBuilder();
        var quoted = false;
        var any = false;
        foreach (var ch in s)
        {
            if (ch == '"') { quoted = !quoted; any = true; continue; }
            if (char.IsWhiteSpace(ch) && !quoted)
            {
                if (any) { args.Add(cur.ToString()); cur.Clear(); any = false; }
                continue;
            }
            cur.Append(ch);
            any = true;
        }
        if (any) args.Add(cur.ToString());
        return args;
    }

    private static string Tail(string s, int max) => s.Length <= max ? s : "..." + s[^max..];

    public static (int Exit, string Output)? RealRunner(string exe, IReadOnlyList<string> args, string dir, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return null;
            p.StandardInput.Close();
            var o = p.StandardOutput.ReadToEndAsync();
            var e = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit((int)Math.Min(int.MaxValue, timeout.TotalMilliseconds)))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* gone */ }
                return null;
            }
            p.WaitForExit();
            return (p.ExitCode, o.Result + (e.Result.Length > 0 ? "\n" + e.Result : ""));
        }
        catch { return null; }
    }
}

/// <summary>
/// Turns a check run into the record: ONE 'check' event (Kind check, IssueId, Commit, Outcome - never updated), then
/// a pass of an open issue resolves it, and a fail of a resolved issue opens its regression. 'dirty' and 'error' are
/// recorded and change nothing.
/// </summary>
public static class IssueResolver
{
    public sealed record Outcome(int? EventId, string Result, int? ResolutionId, int? RegressionId, string Line);

    public static Outcome CheckAndRecord(BrainDb db, StoredRecord issue, CheckResult r, string actor, string? session)
    {
        if (r.Commit is null)
            return new Outcome(null, r.Outcome, null, null, $"#{issue.Id} {issue.Name}: {r.Outcome} - not recorded (no commit to cite): {FirstLine(r.Detail)}");
        var ev = BrainStore.Insert(new BrainEvent(issue.Lane, "check", r.Repo, r.Commit, issue.CheckRef, r.Outcome,
            JsonSerializer.Serialize(new { issue = issue.Id, total = r.Total, detail = r.Detail }, GitCapture.Json), actor, session,
            issue.Id), db);
        return Apply(db, issue, ev, r.Outcome, actor, $"brain.Event #{ev}");
    }

    /// <summary>After a check event: pass + open -> resolution; fail + resolved -> regression.</summary>
    public static Outcome Apply(BrainDb db, StoredRecord issue, int ev, string outcome, string actor, string what)
    {
        if (outcome == "pass" && issue.OpenIssue)
        {
            var res = RecordStore.Resolve(db, issue.Id, ev, actor, $"its {issue.CheckKind} check passed ({what})");
            return new Outcome(ev, outcome, res, null, $"#{issue.Id} {issue.Name}: pass ({what}) -> RESOLVED by #{res}");
        }
        // Resolved = superseded BY A RESOLUTION. A restated issue is superseded by the NEW issue (TR_Memory_Restate): it is
        // not resolved, a fail of it is no regression, and its truth lives in the restatement (review row 165).
        var resolvedBy = RecordStore.ResolutionOf(db, issue);
        if (outcome == "fail" && resolvedBy is not null)
        {
            var reg = RecordStore.OpenRegression(db, issue.Id, ev, actor);
            return new Outcome(ev, outcome, null, reg, $"#{issue.Id} {issue.Name}: FAIL after its resolution ({what}) -> REGRESSION #{reg} open");
        }
        var state = issue.OpenIssue ? (outcome == "pass" ? "" : " - still open")
                  : resolvedBy is not null ? $" - already resolved by #{resolvedBy}"
                  : issue.SupersededBy is { } by ? $" - it was restated as #{by}: check that one"
                  : "";
        return new Outcome(ev, outcome, null, null, $"#{issue.Id} {issue.Name}: {outcome} ({what}){state}");
    }

    private static string FirstLine(string s) => s.Split('\n')[0];
}
