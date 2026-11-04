using System.Text.Json;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Records;
using Kor.Operations.Framework.Stall;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// G1, the stall guard (board row 1308). Covers: the seed's shape (NetworkOps 7b3e2b20 - a mailbox refused through Graph
/// twice, the first with exit 0, then the SAME mailbox through Outlook MAPI) is refused, naming both failures, and the
/// mailbox's other routes and other mailboxes are not; a diagnosis naming the target after the second failure clears it
/// (and one made before it, or a decision, does not); a success in between re-opens; failures older than the window do
/// not count; a result not yet on disk counts for nothing (the lag case allows); a bare 403 in data, a grep line, a
/// phrase the command searched for and "moved to the background" are not failures; the exit class needs the same error
/// line, is re-opened by an edit, and a changed python -c script is another family; the empty class is the identical
/// query; an MCP tool's error twice on one host is refused on that host; the hook verb (live KorStandards, rolled back)
/// denies with the JSON Claude Code reads and logs one oversight.GuardRefusal row, and allows once a finding naming the
/// target is in brain.Memory.
/// Does NOT cover: a real transcript (the 30-day replay does, kor stall --replay); the hook's wiring (EnrollCheckTests).
/// </summary>
[Collection(LiveDb.Name)]
public class StallTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 3, 19, 25, 0, TimeSpan.Zero);
    private int _line;

    private StallCall Call(string tool, string text, string? output, int minutes, bool error = false) =>
        new(++_line, T0.AddMinutes(minutes), $"id{_line}", tool, text, output, error, output is null ? null : T0.AddMinutes(minutes).AddSeconds(20));

    private const string Forbidden = "jmarkulin [marrokal] ERROR Response status code does not indicate success: Forbidden (Forbidden).\n" +
                                     "jdesroches [marrokal] ERROR Response status code does not indicate success: Forbidden (Forbidden).";

    private static string? NoDiagnosis(StallTarget t, DateTimeOffset a) => null;

    /// <summary>The seed's shape: Graph on four mailboxes (two refused, exit 0), a read, Graph on the two again (exit 5).</summary>
    private List<StallCall> Seed() => new()
    {
        Call("PowerShell", "& pwsh -NoProfile -File \"C:/t/g.ps1\" -BoxList \"contact,carmstrong,jmarkulin,jdesroches\"",
             "== contact [marrokal]: 0 total\n== carmstrong [30113]: 2 total\n" + Forbidden, 0),
        Call("PowerShell", "Get-Content \"C:/t/tasks/x.output\"; date", "November 3, 2026 12:25:56 PM", 1),
        Call("PowerShell", "date; & pwsh -NoProfile -File \"C:/t/g.ps1\" -BoxList \"jmarkulin,jdesroches\"",
             "Exit code 5\nNovember 3, 2026 12:34:00 PM\n" + Forbidden, 9, error: true),
    };

    private const string Outlook = "$o=New-Object -ComObject Outlook.Application; $ns=$o.GetNamespace(\"MAPI\")\n" +
                                   "foreach($u in 'jmarkulin','jdesroches'){ $rc=$ns.CreateRecipient(\"$u@korstructural.com\") }";

    [Fact]
    public void TheSameMailboxByAnotherRouteIsRefusedAtTheThirdAttempt()
    {
        var h = Seed();
        var d = StallGuard.Decide(Call("PowerShell", Outlook, null, 9), h, NoDiagnosis, T0.AddMinutes(9.2));
        Assert.True(d.Refuse);
        Assert.Equal("jmarkulin", d.Target!.Name);
        Assert.Equal("refused", d.Second!.Class);
        Assert.Contains("line 1", d.Reason);   // both failures named
        Assert.Contains("line 3", d.Reason);
        Assert.Contains("Forbidden", d.Reason);
        // the first failure carried exit code 0: it was read from the output
        Assert.NotNull(StallGuard.Classify(h[0]));
    }

    [Fact]
    public void AnotherMailboxAndTheSecondAttemptAreAllowed()
    {
        var h = Seed();
        Assert.False(StallGuard.Decide(Call("PowerShell", "& pwsh -File \"C:/t/g.ps1\" -BoxList \"contact\"", null, 10), h, NoDiagnosis, T0.AddMinutes(10)).Refuse);
        // after ONE failure the retry is allowed (seed :305 - the first Forbidden had not happened yet)
        Assert.False(StallGuard.Decide(Call("PowerShell", Outlook, null, 2), h.Take(2).ToList(), NoDiagnosis, T0.AddMinutes(2)).Refuse);
    }

    [Fact]
    public void TheRecordAndTextAboutTheTargetAreNotAttemptsOnIt()
    {
        var h = Seed();
        Assert.False(StallGuard.Decide(Call("PowerShell", "C:/KOR-Operations/kor/kor.exe record --module NetworkOps --kind finding --name x --says \"Graph refuses jmarkulin\" --source query:SELECT 1", null, 10),
                                       h, NoDiagnosis, T0.AddMinutes(10)).Refuse);
        Assert.False(StallGuard.Decide(Call("Bash", "python -I \"C:/f/tools/kor_brain.py\" note --lane NetworkOps --name x --kind feedback --description \"jmarkulin is refused\"", null, 10),
                                       h, NoDiagnosis, T0.AddMinutes(10)).Refuse);
        Assert.False(StallGuard.Decide(Call("Bash", "git commit -m \"g.ps1: jmarkulin needs the grant\" && grep -n jmarkulin notes.md", null, 10),
                                       h, NoDiagnosis, T0.AddMinutes(10)).Refuse);
    }

    [Fact]
    public void ADiagnosisNamingTheTargetAfterTheSecondFailureClearsIt()
    {
        var h = Seed();
        var second = h[2].ResultAt!.Value;
        // a finding naming jmarkulin made one minute after the second failure (so after the first, too)
        string? Diag(StallTarget t, DateTimeOffset after) => t.Name == "jmarkulin" && second.AddMinutes(1) > after ? "brain.Memory #1 (finding)" : null;
        var d = StallGuard.Decide(Call("PowerShell", "Graph again on jmarkulin@korstructural.com", null, 20), h, Diag, T0.AddMinutes(20));
        Assert.False(d.Refuse);
        Assert.Contains("brain.Memory #1", d.ClearedBy);
        // only jdesroches named: still refused on that mailbox
        Assert.True(StallGuard.Decide(Call("PowerShell", "Outlook on 'jdesroches'", null, 20), h, Diag, T0.AddMinutes(20)).Refuse);
    }

    [Fact]
    public void TheReplaysDiagnosisNeedsAKindThatDiagnosesAndTheName()
    {
        var h = Seed();
        h.Add(Call("Write", "C:/t/lesson.md", "File created", 10) with { Content = "Graph refused jmarkulin until the grant propagates." });
        h.Add(Call("PowerShell", "python -I \"C:/f/tools/kor_brain.py\" note --lane NetworkOps --name x --kind decision --body-file \"C:/t/lesson.md\"", "('1', 'NULL')", 11));
        var next = Call("PowerShell", Outlook, null, 12);
        // a decision is not a diagnosis
        Assert.True(StallGuard.Decide(next, h, (t, a) => StallGuard.DiagnosedInTranscript(h, t, a), next.At!.Value).Refuse);
        h.Add(Call("PowerShell", "python -I \"C:/f/tools/kor_brain.py\" note --lane NetworkOps --name y --kind feedback --body-file \"C:/t/lesson.md\"", "('2', 'NULL')", 12));
        var later = Call("PowerShell", "Outlook on 'jmarkulin'", null, 13);
        var d = StallGuard.Decide(later, h, (t, a) => StallGuard.DiagnosedInTranscript(h, t, a), later.At!.Value);
        Assert.False(d.Refuse);
        Assert.Contains("--kind feedback", d.ClearedBy);
    }

    [Fact]
    public void ASuccessInBetweenReopensAndOldFailuresExpire()
    {
        var h = Seed();
        h.Add(Call("PowerShell", "& pwsh -File \"C:/t/g.ps1\" -BoxList \"jmarkulin\"", "== jmarkulin [marrokal]: 3 total", 30));
        // jmarkulin worked; jdesroches has not: the Outlook call names both, so it is still refused - on jdesroches
        var d = StallGuard.Decide(Call("PowerShell", Outlook, null, 31), h, NoDiagnosis, T0.AddMinutes(31));
        Assert.True(d.Refuse);
        Assert.Equal("jdesroches", d.Target!.Name);
        Assert.False(StallGuard.Decide(Call("PowerShell", "Outlook on 'jmarkulin'", null, 31), h, NoDiagnosis, T0.AddMinutes(31)).Refuse);
        var old = Seed();
        Assert.False(StallGuard.Decide(Call("PowerShell", Outlook, null, 200), old, NoDiagnosis, T0.AddMinutes(200)).Refuse);
    }

    [Fact]
    public void AMessageFromIanOrAPeerSinceTheSecondFailureReopens()
    {
        var h = Seed();
        h.Add(new StallCall(++_line, T0.AddMinutes(12), "p", StallTranscript.Prompt, "human", "", false, T0.AddMinutes(12)));
        Assert.False(StallGuard.Decide(Call("PowerShell", Outlook, null, 13), h, NoDiagnosis, T0.AddMinutes(13)).Refuse);
        // a prompt BEFORE the second failure does not
        var s = Seed();
        var before = new List<StallCall>
        {
            s[0], s[1],
            new(s[1].Line + 1, T0.AddMinutes(8), "p", StallTranscript.Prompt, "human", "", false, T0.AddMinutes(8)),   // "check now"
            s[2] with { Line = s[1].Line + 2 },
        };
        Assert.True(StallGuard.Decide(Call("PowerShell", Outlook, null, 13), before, NoDiagnosis, T0.AddMinutes(13)).Refuse);
    }

    [Fact]
    public void AResultNotYetOnDiskCountsForNothing()
    {
        var h = Seed();
        h[2] = h[2] with { Output = null, ResultAt = null, IsError = false };   // the transcript lags: the second result is missing
        Assert.False(StallGuard.Decide(Call("PowerShell", Outlook, null, 9), h, NoDiagnosis, T0.AddMinutes(9.2)).Refuse);
    }

    [Theory]
    [InlineData("grep -n Forbidden src/Client.cs", "12:    case HttpStatusCode.Forbidden: // 403 Forbidden")]          // searched for
    [InlineData("sed -n 1,40p src/Client.cs", "  // a 403 (Forbidden) from Graph means the grant is not live yet")] // a reader's data
    [InlineData("python -I report.py", "rows: 401, 403, 404 in 3 sheets")]                                           // bare numbers
    [InlineData("dotnet test x", "Command did not complete within its 240s timeout and was moved to the background (ID: b1).")]
    public void DataIsNotAFailure(string command, string output) =>
        Assert.Null(StallGuard.Classify(Call("Bash", command, output, 0)));

    [Theory]
    [InlineData("curl -s https://x.example.com/api", "HTTP/1.1 401 Unauthorized", "refused")]
    [InlineData("Test-Path '\\\\192.168.1.18\\C$'", "System error 5 has occurred.\nAccess is denied.", "refused")]
    [InlineData("ssh KOR-FS01 hostname", "ssh: connect to host KOR-FS01 port 22: Connection timed out", "timeout")]
    public void RefusalsAndTimeoutsAreReadFromTheOutput(string command, string output, string cls) =>
        Assert.Equal(cls, StallGuard.Classify(Call("Bash", command, output, 0))!.Class);

    [Fact]
    public void TheExitClassNeedsTheSameErrorAndIsReopenedByAnEdit()
    {
        const string fail = "Exit code 1\nTraceback (most recent call last):\n  File \"x.py\", line 4\nKeyError: 'gates'";
        var h = new List<StallCall>
        {
            Call("Bash", "python -I tools/check_x.py run.json", fail, 0, true),
            Call("Bash", "python -I tools/check_x.py run.json --verbose", fail, 1, true),
        };
        var next = Call("Bash", "python -I tools/check_x.py run.json", null, 2);
        var d = StallGuard.Decide(next, h, NoDiagnosis, T0.AddMinutes(2));
        Assert.True(d.Refuse);
        Assert.Equal("family", d.Target!.Kind);
        Assert.Contains("KeyError", d.Second!.Evidence);
        // an edit after the second failure: the fix loop is work
        var edited = h.Append(Call("Edit", "C:/r/tools/check_x.py", "updated", 2)).ToList();
        Assert.False(StallGuard.Decide(next, edited, NoDiagnosis, T0.AddMinutes(3)).Refuse);
        // a different error the second time: progress
        var moved = new List<StallCall> { h[0], h[1] with { Output = "Exit code 1\nTraceback (most recent call last):\nValueError: x" } };
        Assert.False(StallGuard.Decide(next, moved, NoDiagnosis, T0.AddMinutes(2)).Refuse);
        // grep exit 1 is a report, not a failure
        Assert.Null(StallGuard.Classify(Call("Bash", "grep -c foo a.txt", "Exit code 1\n0", 0, true)));
    }

    [Fact]
    public void AChangedInlineScriptIsAnotherFamily()
    {
        const string fail = "Exit code 1\nTraceback (most recent call last):\nUnicodeEncodeError: 'charmap' codec can't encode character";
        var h = new List<StallCall>
        {
            Call("Bash", "python -I -c \"print(t)\" a.pdf", fail, 0, true),
            Call("Bash", "PYTHONIOENCODING=utf-8 python -I -c \"print(t)\" a.pdf", fail, 1, true),
        };
        Assert.True(StallGuard.Decide(Call("Bash", "python -I -c \"print(t)\" a.pdf", null, 2), h, NoDiagnosis, T0.AddMinutes(2)).Refuse);
        Assert.False(StallGuard.Decide(Call("Bash", "python -I -c \"import sys; sys.stdout.reconfigure(encoding='utf-8'); print(t)\" a.pdf", null, 2),
                                       h, NoDiagnosis, T0.AddMinutes(2)).Refuse);
    }

    [Fact]
    public void TheEmptyClassIsTheIdenticalQuery()
    {
        var h = new List<StallCall>
        {
            Call("Bash", "grep -rn \"zzz\" docs/", "(Bash completed with no output)", 0),
            Call("Bash", "grep -rn \"zzz\" docs/", "(Bash completed with no output)", 1),
        };
        Assert.True(StallGuard.Decide(Call("Bash", "grep -rn \"zzz\" docs/", null, 2), h, NoDiagnosis, T0.AddMinutes(2)).Refuse);
        Assert.False(StallGuard.Decide(Call("Bash", "grep -rn \"yyy\" docs/", null, 2), h, NoDiagnosis, T0.AddMinutes(2)).Refuse);
        // a mutating command's silence is not an empty result
        Assert.Null(StallGuard.Classify(Call("Bash", "git add a.txt", "(Bash completed with no output)", 0)));
    }

    [Fact]
    public void AnMcpToolsErrorTwiceOnOneHostIsRefusedOnThatHost()
    {
        var h = new List<StallCall>
        {
            Call("mcp__networkops__networkops_run_fix", "{\"host\":\"KOR-302N\",\"fixId\":\"run-command\",\"param\":\"a\"}", "An error occurred invoking 'networkops_run_fix'.", 0, true),
            Call("mcp__networkops__networkops_run_fix", "{\"host\":\"KOR-302N\",\"fixId\":\"run-command\",\"param\":\"b\"}", "An error occurred invoking 'networkops_run_fix'.", 1, true),
        };
        var d = StallGuard.Decide(Call("mcp__networkops__networkops_device", "{\"host\":\"KOR-302N\"}", null, 2), h, NoDiagnosis, T0.AddMinutes(2));
        Assert.True(d.Refuse);
        Assert.Equal("mcp__networkops|kor-302n", d.Target!.Key);
        Assert.False(StallGuard.Decide(Call("mcp__networkops__networkops_device", "{\"host\":\"KOR-210\"}", null, 2), h, NoDiagnosis, T0.AddMinutes(2)).Refuse);
        // the same host by another route (an admin-share copy) is not the failing route
        Assert.False(StallGuard.Decide(Call("PowerShell", "Copy-Item x '\\\\KOR-302N\\C$\\KOR.Drafter\\tasks'", null, 2), h, NoDiagnosis, T0.AddMinutes(2)).Refuse);
        // a host named only in the purpose text is not what the call acts on (c6cb3844:23614 named KOR-210 that way)
        var other = h.Select(x => x with { Text = x.Text.Replace("KOR-302N", "KOR-999").Replace("\"param\"", "\"purpose\":\"so KOR-302N can be given\",\"param\"") }).ToList();
        Assert.False(StallGuard.Decide(Call("mcp__networkops__networkops_device", "{\"host\":\"KOR-302N\"}", null, 2), other, NoDiagnosis, T0.AddMinutes(2)).Refuse);
    }

    [Fact]
    public void ATimeoutIsTheCommandsAndAReportedFieldIsNotATimeout()
    {
        var h = new List<StallCall>
        {
            Call("Bash", "netops run --script a.ps1 --hosts KOR-BK01", "=== KOR-BK01 [Failed] -- Timeout: no result after 90 s\nOperation timed out", 0),
            Call("Bash", "netops run --script a.ps1 --hosts KOR-BK01", "=== KOR-BK01 [Failed] -- Timeout: no result after 60 s\nOperation timed out", 1),
        };
        Assert.True(StallGuard.Decide(Call("Bash", "netops run --script a.ps1 --hosts KOR-BK01", null, 2), h, NoDiagnosis, T0.AddMinutes(2)).Refuse);
        // the script with the hanging step removed is a fix, not a retry (570463ed:8404)
        Assert.False(StallGuard.Decide(Call("Bash", "netops run --script b.ps1 --hosts KOR-BK01", null, 2), h, NoDiagnosis, T0.AddMinutes(2)).Refuse);
        // a search's own summary field (2b3d2fa7:361)
        Assert.Null(StallGuard.Classify(Call("Bash", "netops run --script s.ps1 --hosts KOR-FS01", "\"directories read: 64928; timed out: True; hits: 1\",", 0)));
    }

    [Fact]
    public void ASubagentsCallsAreReadFromItsOwnTranscript()
    {
        var dir = Path.Combine(Path.GetTempPath(), "stall-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(dir, "s1", "subagents"));
        try
        {
            var main = Path.Combine(dir, "s1.jsonl");
            var sub = Path.Combine(dir, "s1", "subagents", "agent-a1.jsonl");
            File.WriteAllText(main, "");
            File.WriteAllText(sub, "");
            Assert.Equal(main, StallTranscript.PathFor(main, "s1", null));
            Assert.Equal(sub, StallTranscript.PathFor(main, "s1", "a1"));
            Assert.Null(StallTranscript.PathFor(main, "s1", "a2"));
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- the hook, against the live KorStandards inside rolled-back transactions ------------------------------------------
    private static string Line(object o) => JsonSerializer.Serialize(o);

    private static string Use(string id, DateTimeOffset at, string command) => Line(new
    {
        type = "assistant", timestamp = at.ToString("O"), isSidechain = false,
        message = new { role = "assistant", content = new object[] { new { type = "tool_use", id, name = "PowerShell", input = new { command } } } },
    });

    private static string Result(string id, DateTimeOffset at, string text, bool error) => Line(new
    {
        type = "user", timestamp = at.ToString("O"), isSidechain = false,
        message = new { role = "user", content = new object[] { new { type = "tool_result", tool_use_id = id, content = text, is_error = error } } },
    });

    [Fact]
    public void LiveTheHookDeniesLogsAndAllowsAfterAFinding()
    {
        var box = "zzstall" + Guid.NewGuid().ToString("N")[..8];
        var now = DateTimeOffset.UtcNow;
        var refusal = $"{box} [x] ERROR Response status code does not indicate success: Forbidden (Forbidden).";
        var file = Path.Combine(Path.GetTempPath(), $"stall-{box}.jsonl");
        File.WriteAllLines(file, new[]
        {
            Use("t1", now.AddMinutes(-6), $"& pwsh -File \"C:/t/g.ps1\" -BoxList \"{box}\""), Result("t1", now.AddMinutes(-6), refusal, false),
            Use("t2", now.AddMinutes(-3), $"& pwsh -File \"C:/t/g.ps1\" -BoxList \"{box}\""), Result("t2", now.AddMinutes(-3), "Exit code 5\n" + refusal, true),
        });
        var input = Line(new
        {
            session_id = "zz-test-stall", transcript_path = file, cwd = Path.GetTempPath(), hook_event_name = "PreToolUse",
            tool_name = "PowerShell", tool_use_id = "t3", tool_input = new { command = $"$rc=$ns.CreateRecipient('{box}@korstructural.com')" },
        });
        var log = Path.Combine(Path.GetTempPath(), $"stall-{box}.log");
        try
        {
            using var db = BrainDb.OpenRolledBack();
            using var board = BrainDb.OpenRolledBack(KorStandardsConnection.Login.Board);
            var output = new StringWriter();
            Assert.Equal(0, Program.Run(new[] { "stall", "--hook" }, () => input, output, new StringWriter(), log, db, board));
            using (var deny = JsonDocument.Parse(output.ToString()))
            {
                var h = deny.RootElement.GetProperty("hookSpecificOutput");
                Assert.Equal("deny", h.GetProperty("permissionDecision").GetString());
                Assert.Contains(box, h.GetProperty("permissionDecisionReason").GetString());
            }
            using (var c = board.Command("SELECT COUNT(*) FROM oversight.GuardRefusal WHERE Guard = N'kor-stall' AND [Session] = N'zz-test-stall' AND [Rule] LIKE @r"))
            {
                c.Parameters.AddWithValue("@r", $"G1 refused {box}%");
                Assert.Equal(1, Convert.ToInt32(c.ExecuteScalar()));
            }
            // a decision naming it does not clear it; a finding does
            RecordStore.Insert(db, new NewRecord("Operations", $"zz-stall-decision-{box}", "decision", $"use OWA for {box}", "x", "zz-test-stall",
                                                 SourceKind: "query", SourceRef: "SELECT 1"));
            output = new StringWriter();
            Program.Run(new[] { "stall", "--hook" }, () => input, output, new StringWriter(), log, db, board);
            Assert.Contains("\"deny\"", output.ToString());
            RecordStore.Insert(db, new NewRecord("Operations", $"zz-stall-finding-{box}", "finding", $"Graph refuses {box} until the grant propagates",
                                                 "Forbidden twice: the FullAccess grant was 10 minutes old.", "zz-test-stall", SourceKind: "query", SourceRef: "SELECT 1"));
            output = new StringWriter();
            Program.Run(new[] { "stall" }, () => input, output, new StringWriter(), log, db, board);
            Assert.StartsWith("allow:", output.ToString());
            Assert.Contains("brain.Memory #", output.ToString());
        }
        finally { File.Delete(file); if (File.Exists(log)) File.Delete(log); }
    }
}
