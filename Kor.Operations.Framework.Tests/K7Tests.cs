using System.Data;
using System.Text;
using System.Text.Json;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Enforce;
using Kor.Operations.Framework.Records;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// K7 - enforced (Framework docs/KNOWLEDGE-IDE-SPEC-K4-K7.md, the spec's 15 tests, numbered, then the two closed gaps).
/// LIVE on KorStandards: brain as korbrain_app and oversight as the Board login, each in a rolled-back unit of work, module
/// zz-test-k7-&lt;time&gt;; every rule here names a tool that does not exist (zzk7tool), so the live Office checks never
/// interfere and these never touch a real command.
/// Covers: check-add's refusals (misses its example, matches an allow, matches a mention, a non-lesson target); enforce
/// denies the repeat naming brain.Check and brain.Memory, allows quoted / commit-message / heredoc mentions, denies after
/// cd &amp;&amp;; Office reaches every module, another module's does not; retired allows; a check failing its known answers is
/// skipped; the refusal row is written (Matched redacted); fails open with no DB and no cache; a Write path rule; other
/// tools and bad input allowed; every LIVE check passes its known answers. Gaps closed: PowerShell backtick escapes and
/// continuations; a repeat inside a script the command runs (and a sub-shell string).
/// Does NOT cover: Claude Code applying the deny (the live headless proof does); the settings.json wiring (framework
/// selftest and the live proof); timing of the 3 s connect.
/// </summary>
[Collection(LiveDb.Name)]
public class K7Tests
{
    private static readonly string Module = $"zz-test-k7-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..4]}";

    private const string Pattern = @"(?:\S*[\\/])?zzk7tool(?:\.exe)?\s+--danger\b";
    private static readonly string[] Refuse = { "zzk7tool --danger now", @"C:/x/zzk7tool.exe --danger" };
    private static readonly string[] Allow = { "zzk7tool --safe", "echo zzk7tool --danger-zone-not" };

    private static string RepoDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, ".git"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("not inside the Operations checkout");
    }

    private static string FrameworkDir() => RepoLocator.Find("Framework", RepoDir())!;

    private static (int Code, string Out, string Err) Run(BrainDb db, BrainDb? board, string? stdin, params string[] args)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var code = Program.Run(args, () => stdin, o, e, Path.Combine(Path.GetTempPath(), $"kor-k7-{Guid.NewGuid():N}.log"), db, board);
        return (code, o.ToString(), e.ToString());
    }

    private static int Lesson(BrainDb db, string name, string kind = "lesson") =>
        RecordStore.Insert(db, new NewRecord(Module, name, kind, $"k7 {name}", $"k7 body {name}", "k7-test",
                                             RecordStore.AtHead(FrameworkDir(), "docs/KNOWLEDGE-IDE.md")));

    private static string[] AddArgs(int memory, string module, string tool, string pattern, IEnumerable<string> refuse, IEnumerable<string> allow)
    {
        var a = new List<string> { "check-add", "--memory", memory.ToString(), "--module", module, "--tool", tool, "--pattern", pattern,
                                   "--says", "zzk7tool --danger is never run (k7 test)" };
        foreach (var r in refuse) { a.Add("--refuse"); a.Add(r); }
        foreach (var x in allow) { a.Add("--allow"); a.Add(x); }
        return a.ToArray();
    }

    private static int AddCheck(BrainDb db, int memory, string module = "", string tool = "Bash")
    {
        var (code, o, e) = Run(db, null, null, AddArgs(memory, module == "" ? Module : module, tool, Pattern, Refuse, Allow));
        Assert.True(code == 0, e + o);
        return int.Parse(System.Text.RegularExpressions.Regex.Match(o, @"brain\.Check #(\d+)").Groups[1].Value);
    }

    private static string Hook(string tool, string field, string value, string? cwd = null) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["session_id"] = "zz-k7-session", ["transcript_path"] = "C:/x/k7.jsonl", ["cwd"] = cwd ?? RepoDir(),
        ["hook_event_name"] = "PreToolUse", ["tool_name"] = tool, ["tool_input"] = new Dictionary<string, string> { [field] = value },
    });

    private static string Enforce(BrainDb db, BrainDb? board, string tool, string value, string? module = null, string? cwd = null)
    {
        var (code, o, e) = Run(db, board, Hook(tool, Rules.FieldOf(tool), value, cwd), "enforce", "--module", module ?? Module, "--hook");
        Assert.True(code == 0, e);
        return o;
    }

    private static bool Denied(string hookOut) => hookOut.Contains("\"permissionDecision\":\"deny\"");

    private static int Checks(BrainDb db)
    {
        using var c = db.Command("SELECT COUNT(*) FROM brain.[Check]");
        return Convert.ToInt32(c.ExecuteScalar());
    }

    // 1-3: check-add refuses a rule that fails its known answers, before anything is written
    [Theory]
    [InlineData("misses", "zzk7tool --nothing-like-it")]
    [InlineData("allow", @"(?:\S*[\\/])?zzk7tool\b")]
    [InlineData("mention", ".*zzk7tool --danger")]
    public void Live01to03_AddRefusesARuleThatFailsItsKnownAnswers(string why, string pattern)
    {
        using var db = BrainDb.OpenRolledBack();
        var m = Lesson(db, "k7-add-" + why);
        var before = Checks(db);
        var (code, _, e) = Run(db, null, null, AddArgs(m, Module, "Bash", pattern, Refuse, Allow));
        Assert.Equal(1, code);
        Assert.Contains("fails its known answers", e);
        Assert.Contains(why switch { "misses" => "is not matched", "allow" => "allow example", _ => "as a mention" }, e);
        Assert.Equal(before, Checks(db));
    }

    // 4
    [Fact]
    public void Live04_AddRefusesANonLessonMemory()
    {
        using var db = BrainDb.OpenRolledBack();
        var finding = Lesson(db, "k7-a-finding", "finding");
        var (code, _, e) = Run(db, null, null, AddArgs(finding, Module, "Bash", Pattern, Refuse, Allow));
        Assert.Equal(1, code);
        Assert.Contains("a check enforces an issue or a lesson", e);
    }

    // 5
    [Fact]
    public void Live05_EnforceDeniesTheRepeatNamingTheCheckAndTheMemory()
    {
        using var db = BrainDb.OpenRolledBack();
        using var board = BrainDb.OpenRolledBack(KorStandardsConnection.Login.Board);
        var m = Lesson(db, "k7-deny");
        var id = AddCheck(db, m);
        var o = Enforce(db, board, "Bash", "zzk7tool --danger now");
        Assert.True(Denied(o), o);
        using var doc = JsonDocument.Parse(o);
        var reason = doc.RootElement.GetProperty("hookSpecificOutput").GetProperty("permissionDecisionReason").GetString()!;
        Assert.Contains($"brain.Check #{id}", reason);
        Assert.Contains($"brain.Memory #{m}", reason);
        Assert.Equal("PreToolUse", doc.RootElement.GetProperty("hookSpecificOutput").GetProperty("hookEventName").GetString());
    }

    // 6
    [Theory]
    [InlineData("echo \"zzk7tool --danger now\"")]
    [InlineData("git commit -m \"note: never run zzk7tool --danger\"")]
    [InlineData("git commit -F - <<'EOF'\nzzk7tool --danger now\nEOF")]
    [InlineData("grep -n 'zzk7tool --danger' notes.md")]
    public void Live06_QuotedAndHeredocMentionsAreAllowed(string command)
    {
        using var db = BrainDb.OpenRolledBack();
        AddCheck(db, Lesson(db, "k7-mention"));
        Assert.False(Denied(Enforce(db, null, "Bash", command)));
    }

    // 7
    [Theory]
    [InlineData("cd \"C:/x\" && zzk7tool --danger now")]
    [InlineData("ls; zzk7tool --danger")]
    [InlineData("echo hi | zzk7tool --danger")]
    [InlineData("FOO=1 zzk7tool --danger")]
    [InlineData("echo $(zzk7tool --danger)")]
    [InlineData("git commit -m \"run $(zzk7tool --danger)\"")]   // a substitution inside "..." RUNS
    public void Live07_DeniedAfterCdAndInAnyRealSegment(string command)
    {
        using var db = BrainDb.OpenRolledBack();
        using var board = BrainDb.OpenRolledBack(KorStandardsConnection.Login.Board);
        AddCheck(db, Lesson(db, "k7-cd"));
        Assert.True(Denied(Enforce(db, board, "Bash", command)), command);
    }

    // 8
    [Fact]
    public void Live08_OfficeReachesEveryModuleAnotherModulesDoesNot()
    {
        using var db = BrainDb.OpenRolledBack();
        using var board = BrainDb.OpenRolledBack(KorStandardsConnection.Login.Board);
        var m = Lesson(db, "k7-office");
        AddCheck(db, m, module: Module);
        Assert.False(Denied(Enforce(db, board, "Bash", "zzk7tool --danger", module: "zz-test-k7-other")));
        // an Office check on a separate lesson reaches the other module
        var office = Lesson(db, "k7-office-2");
        var (code, _, e) = Run(db, null, null, AddArgs(office, "Office", "Bash", @"zzk7office\b", new[] { "zzk7office x" }, new[] { "zzk7officer" }));
        Assert.True(code == 0, e);
        Assert.True(Denied(Enforce(db, board, "Bash", "zzk7office x", module: "zz-test-k7-other")));
        Assert.True(Denied(Enforce(db, board, "Bash", "zzk7office x", module: Module)));
    }

    // 9
    [Fact]
    public void Live09_ARetiredCheckAllows()
    {
        using var db = BrainDb.OpenRolledBack();
        var id = AddCheck(db, Lesson(db, "k7-retire"));
        var (code, _, e) = Run(db, null, null, "check-retire", id.ToString(), "--because", "k7 test: retired");
        Assert.True(code == 0, e);
        Assert.False(Denied(Enforce(db, null, "Bash", "zzk7tool --danger")));
        Assert.Equal(1, Run(db, null, null, "check-retire", id.ToString(), "--because", "again").Code);   // once only
    }

    // 10
    [Fact]
    public void Live10_ARuleFailingItsKnownAnswersIsSkippedAndLogged()
    {
        using var db = BrainDb.OpenRolledBack();
        var m = Lesson(db, "k7-skip");
        // past check-add, straight into the table: '.*' also matches every mention
        using (var c = db.Command("INSERT brain.[Check] (MemoryId, Module, Tool, Field, Pattern, MustRefuse, MustAllow, Says, CreatedBy) " +
                                  "VALUES (@m, @mod, N'Bash', N'command', N'.*zzk7skip', N'[\"zzk7skip\"]', N'[\"ls\"]', N'k7 skip', N'k7-test')"))
        {
            c.Parameters.Add("@m", SqlDbType.Int).Value = m;
            c.Parameters.Add("@mod", SqlDbType.NVarChar, 60).Value = Module;
            c.ExecuteNonQuery();
        }
        var log = Path.Combine(Path.GetTempPath(), $"kor-k7-skip-{Guid.NewGuid():N}.log");
        var o = new StringWriter();
        Program.Run(new[] { "enforce", "--module", Module, "--hook" }, () => Hook("Bash", "command", "zzk7skip"), o, new StringWriter(), log, db);
        Assert.False(Denied(o.ToString()));
        Assert.Contains("fails its known answers", File.ReadAllText(log));
    }

    // 11
    [Fact]
    public void Live11_TheRefusalIsLoggedRedacted()
    {
        using var db = BrainDb.OpenRolledBack();
        using var board = BrainDb.OpenRolledBack(KorStandardsConnection.Login.Board);
        var id = AddCheck(db, Lesson(db, "k7-log"));
        int Rows()
        {
            using var c = board.Command("SELECT COUNT(*) FROM oversight.GuardRefusal WHERE Guard = N'kor-enforce' AND [Rule] = @r");
            c.Parameters.Add("@r", SqlDbType.NVarChar, 200).Value = CheckStore.RuleName(id);
            return Convert.ToInt32(c.ExecuteScalar());
        }
        Assert.Equal(0, Rows());
        // a synthetic value on the same command line: the row carries it redacted (CK_GuardRefusal_NoSecret would refuse it raw)
        Assert.True(Denied(Enforce(db, board, "Bash", "zzk7tool --danger Password=Zx9vQ2mT")));
        Assert.Equal(1, Rows());
        using var r = board.Command("SELECT TOP 1 Matched, [Session], Repo FROM oversight.GuardRefusal WHERE [Rule] = @r ORDER BY Id DESC");
        r.Parameters.Add("@r", SqlDbType.NVarChar, 200).Value = CheckStore.RuleName(id);
        using var rd = r.ExecuteReader();
        Assert.True(rd.Read());
        Assert.DoesNotContain("Zx9vQ2mT", rd.GetString(0));
        Assert.StartsWith("zzk7tool --danger", rd.GetString(0));
        Assert.Equal("zz-k7-session", rd.GetString(1));
        Assert.Equal(Git.Leaf(Git.TopLevel(RepoDir())), rd.GetString(2));   // the checkout's own folder name (review row 227)
        rd.Close();
        // kor checks --hits reads it
        var (code, o, e) = Run(db, board, null, "checks", "--module", Module, "--hits", "1");
        Assert.True(code == 0, e);
        Assert.Contains($"brain.Check #{id}", o);
        Assert.Contains("hits=1", o);
    }

    // 12
    [Fact]
    public void Test12_FailsOpenWithNoDbAndNoCacheAndUsesAStaleCacheWhileDown()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"kor-k7-cache-{Guid.NewGuid():N}");
        var now = DateTimeOffset.UtcNow;
        var logged = new List<Exception>();
        var reads = 0;
        IReadOnlyList<LiveCheck> Down() { reads++; throw new InvalidOperationException("db down"); }
        Assert.Null(CheckCache.Get(dir, Down, now, logged.Add, out var s1));
        Assert.Equal("none", s1);
        Assert.Null(CheckCache.Get(dir, Down, now.AddSeconds(5), logged.Add, out _));   // back-off: no second wait on the server
        Assert.Equal(1, reads);
        var check = new LiveCheck(1, 2, "lesson", "x", "Office", "Bash", "command", "zzk7tool", new[] { "zzk7tool" }, new[] { "ls" }, "s");
        Assert.Single(CheckCache.Get(dir, () => new[] { check }, now.AddMinutes(2), logged.Add, out var s2)!);
        Assert.Equal("db", s2);
        Assert.Single(CheckCache.Get(dir, Down, now.AddMinutes(10), logged.Add, out var s3)!);   // cache older than 5 min, db down
        Assert.Equal("stale cache (db unreadable)", s3);
        Assert.Single(CheckCache.Get(dir, () => throw new Exception("not called"), now.AddMinutes(10.5), logged.Add, out var s4)!);
        Assert.Equal("cache", s4);   // held down for 60 s
        // review row 227: still down after the back-off with only the outage marker -> still "none", never an empty "stale cache"
        var dir2 = Path.Combine(Path.GetTempPath(), $"kor-k7-cache-{Guid.NewGuid():N}");
        Assert.Null(CheckCache.Get(dir2, Down, now, logged.Add, out _));
        Assert.Null(CheckCache.Get(dir2, Down, now.AddMinutes(2), logged.Add, out var s5));
        Assert.Equal("none", s5);
    }

    // 13
    [Fact]
    public void Live13_AWritePathRule()
    {
        using var db = BrainDb.OpenRolledBack();
        using var board = BrainDb.OpenRolledBack(KorStandardsConnection.Login.Board);
        var m = Lesson(db, "k7-write");
        var (code, _, e) = Run(db, null, null, AddArgs(m, Module, "Write", @"[A-Za-z]:/zzk7-forbidden/",
                                                        new[] { "C:/zzk7-forbidden/a.txt" }, new[] { "C:/zzk7-allowed/a.txt" }));
        Assert.True(code == 0, e);
        Assert.True(Denied(Enforce(db, board, "Write", @"C:\zzk7-forbidden\b.txt")));
        Assert.False(Denied(Enforce(db, board, "Write", @"C:\zzk7-allowed\b.txt")));
        Assert.False(Denied(Enforce(db, board, "Bash", "C:/zzk7-forbidden/b.txt")));   // a Write rule is not a Bash rule
    }

    // 14
    [Theory]
    [InlineData("{\"tool_name\":\"Read\",\"tool_input\":{\"file_path\":\"zzk7tool --danger\"}}")]
    [InlineData("{\"tool_name\":\"Bash\",\"tool_input\":{}}")]
    [InlineData("{\"tool_name\":\"Bash\",\"tool_input\":\"zzk7tool --danger\"}")]
    [InlineData("not json")]
    [InlineData("[1]")]
    [InlineData("")]
    public void Live14_OtherToolsAndBadInputAreAllowed(string stdin)
    {
        using var db = BrainDb.OpenRolledBack();
        AddCheck(db, Lesson(db, "k7-bad-input"));
        var o = new StringWriter();
        Assert.Equal(0, Program.Run(new[] { "enforce", "--module", Module, "--hook" }, () => stdin, o, new StringWriter(),
                                    Path.Combine(Path.GetTempPath(), $"kor-k7-{Guid.NewGuid():N}.log"), db));
        Assert.Equal("", o.ToString());
    }

    // 15
    [Fact]
    public void Live15_EveryLiveCheckPassesItsKnownAnswers()
    {
        var live = BrainDb.Read(KorStandardsConnection.Login.Brain, CheckStore.Live);
        foreach (var s in live) Assert.True(Rules.KnownAnswerFailures(s.Check).Count == 0,
                                            $"brain.Check #{s.Check.Id}: {string.Join("; ", Rules.KnownAnswerFailures(s.Check))}");
    }

    // ---- gap 1 closed: PowerShell backtick escaping --------------------------------------------------------------------
    [Fact]
    public void Gap1_PowerShellBacktickEscapesAreDataAndContinuationsJoin()
    {
        // `; is a literal semicolon: ONE command (Write-Output), never a second command
        Assert.Equal(new[] { "Write-Output a`; zzk7tool --danger" }, CommandSegments.Split("Write-Output a`; zzk7tool --danger", Shell.PowerShell));
        // `" inside "..." does not close it: the separator after it is still data
        Assert.Single(CommandSegments.Split("Write-Output \"a`\"; zzk7tool --danger\"", Shell.PowerShell));
        // "" inside "..." is an escaped quote too
        Assert.Single(CommandSegments.Split("Write-Output \"a\"\"; zzk7tool --danger\"", Shell.PowerShell));
        // a backtick line continuation (LF and CRLF) joins the lines: the rule sees the whole command
        Assert.Single(CommandSegments.Split("zzk7tool `\n  --danger", Shell.PowerShell));
        Assert.Single(CommandSegments.Split("zzk7tool `\r\n  --danger", Shell.PowerShell));
        Assert.NotNull(Rules.Matches(Pattern, "PowerShell", "zzk7tool `\r\n--danger"));
        // K3's neutraliser (unchanged) would have split each of these - the gap this closes
        Assert.Contains(';', ToolClassifier.Neutralise("Write-Output a`; zzk7tool --danger"));
        // and a real PowerShell separator still separates
        Assert.Equal(2, CommandSegments.Split("cd C:/x; zzk7tool --danger", Shell.PowerShell).Count);
        Assert.NotNull(Rules.Matches(Pattern, "PowerShell", "Set-Location C:/x; zzk7tool --danger"));
        Assert.Null(Rules.Matches(Pattern, "PowerShell", "Write-Output a`; zzk7tool --danger"));
        // bash: \; is find's argument, not a separator; a comment never runs
        Assert.Single(CommandSegments.Split(@"find . -exec echo {} \; zzk7tool --danger", Shell.Bash));
        Assert.Null(Rules.Matches(Pattern, "Bash", @"find . -exec echo {} \; zzk7tool --danger"));
        Assert.NotNull(Rules.Matches(Pattern, "Bash", "{ zzk7tool --danger; }"));   // a group runs its commands
        Assert.Null(Rules.Matches(Pattern, "Bash", "ls # then zzk7tool --danger"));
        Assert.Null(Rules.Matches(Pattern, "PowerShell", "ls <# zzk7tool --danger #>"));
    }

    // ---- gap 2 closed: a repeat hidden in a script the command runs ------------------------------------------------------
    [Fact]
    public void Live_Gap2_ARepeatInsideAScriptTheCommandRunsIsDenied()
    {
        var dir = Directory.CreateTempSubdirectory("kor-k7-script-").FullName;
        Directory.CreateDirectory(Path.Combine(dir, "tools"));
        File.WriteAllText(Path.Combine(dir, "tools", "old-install.ps1"), "$x = 1\nWrite-Host 'installing'\n& zzk7tool --danger\n");
        File.WriteAllText(Path.Combine(dir, "tools", "mentions.ps1"), "Write-Host 'never zzk7tool --danger'\n# zzk7tool --danger\n");
        File.WriteAllText(Path.Combine(dir, "tools", "outer.sh"), "echo start\nbash tools/inner.sh\n");
        File.WriteAllText(Path.Combine(dir, "tools", "inner.sh"), "zzk7tool --danger\n");
        using var db = BrainDb.OpenRolledBack();
        using var board = BrainDb.OpenRolledBack(KorStandardsConnection.Login.Board);
        AddCheck(db, Lesson(db, "k7-script"), tool: "Bash,PowerShell");
        foreach (var (tool, cmd) in new[]
                 {
                     ("PowerShell", "pwsh tools/old-install.ps1"), ("PowerShell", "pwsh -NoProfile -File tools\\old-install.ps1"),
                     ("PowerShell", "& .\\tools\\old-install.ps1"), ("Bash", "pwsh tools/old-install.ps1"),
                     ("Bash", $"cd \"{dir.Replace('\\', '/')}\" && pwsh tools/old-install.ps1"), ("Bash", "bash tools/outer.sh"),
                 })
        {
            var o = Enforce(db, board, tool, cmd, cwd: cmd.StartsWith("cd ") ? RepoDir() : dir);
            Assert.True(Denied(o), $"{tool}: {cmd}");
            Assert.Contains("script ", o);
        }
        // the script only MENTIONS it; a script that is not RUN (git add) is not read
        Assert.False(Denied(Enforce(db, board, "PowerShell", "pwsh tools/mentions.ps1", cwd: dir)));
        Assert.False(Denied(Enforce(db, board, "Bash", "git add tools/old-install.ps1", cwd: dir)));
        // a sub-shell string and an encoded command are unwrapped
        Assert.True(Denied(Enforce(db, board, "Bash", "bash -c \"zzk7tool --danger\"", cwd: dir)));
        Assert.True(Denied(Enforce(db, board, "PowerShell", "pwsh -NoProfile -Command \"zzk7tool --danger\"", cwd: dir)));
        var b64 = Convert.ToBase64String(Encoding.Unicode.GetBytes("zzk7tool --danger"));
        Assert.True(Denied(Enforce(db, board, "PowerShell", $"powershell -EncodedCommand {b64}", cwd: dir)));
        Directory.Delete(dir, true);
    }
}
