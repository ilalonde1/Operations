using System.Text.Json;
using System.Text.RegularExpressions;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Cli;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// Board row 1393 - the brain is written automatically: pipeline runs are events (part 1), a turn that produced something
/// is asked for its record at Stop (part 2), Ian's decisions are asked for with his words (part 3).
/// Pure tests read transcripts written here (the shapes are the real ones: origin.kind human / peer / task-notification,
/// isMeta Stop-hook feedback, entrypoint cli / sdk-cli, tool_result with toolUseResult.backgroundTaskId); the Live tests run
/// the whole verb (kor capture-check, a real transcript file, the live KorStandards inside a rolled-back transaction).
/// Covers: the pipeline classifier (virtual_drafter verbs, *_check.py, outcomes, gate lines, "0 FAIL", grep / py_compile
/// not runs); a background launch is not an event at PostToolUse; turn boundaries (Stop-hook feedback continues the turn,
/// peers and notifications start one, batched notifications); a `claude -p` turn is never asked; Ian's decisions and the
/// false ones the dry run found; diagnosis and hypotheticals; a failed test cleared by a later pass; a failure READ in
/// output, and its re-read in a later turn not asked; a background run recorded once from its notification and output
/// file; once per turn (the second Stop allows, the loop is impossible); a record in the turn satisfies the ask; the ask
/// quotes Ian and names this ask's event as the decision's source; a credential in his words never reaches the brain;
/// commit and turn asks are ONE ask.
/// Does NOT cover: Claude Code's own Stop continuation (the hook's JSON is asserted, not a live session); the 30-day dry
/// run (kor capture-check --replay, run by hand: board row 1393's evidence); a SubagentStop (not wired).
/// </summary>
public class TurnCaptureTests
{
    // ---- transcript lines, in the real shapes ----------------------------------------------------------------------------
    internal static string At(int minutesAgo) => DateTimeOffset.UtcNow.AddMinutes(-minutesAgo).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

    internal static string User(string text, string? origin = "human", string entry = "cli", int minutesAgo = 1, bool meta = false, string? peer = null) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["type"] = "user", ["isSidechain"] = false, ["isMeta"] = meta ? true : null, ["timestamp"] = At(minutesAgo), ["entrypoint"] = entry,
            ["origin"] = origin is null ? null : new Dictionary<string, object?> { ["kind"] = origin, ["name"] = peer },
            ["message"] = new { role = "user", content = text },
        }.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value));

    internal static string Say(string text, int minutesAgo = 1) => JsonSerializer.Serialize(new
    {
        type = "assistant", isSidechain = false, timestamp = At(minutesAgo), entrypoint = "cli",
        message = new { role = "assistant", content = new object[] { new { type = "text", text } } },
    });

    internal static string Tool(string id, string command, bool background = false, string cwd = @"C:\x", string name = "Bash") => JsonSerializer.Serialize(new
    {
        type = "assistant", isSidechain = false, timestamp = At(1), entrypoint = "cli", cwd,
        message = new { role = "assistant", content = new object[] { new { type = "tool_use", id, name, input = new { command, run_in_background = background } } } },
    });

    internal static string Result(string id, string output, bool error = false, string? backgroundTask = null) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["type"] = "user", ["isSidechain"] = false, ["timestamp"] = At(1),
        ["message"] = new { role = "user", content = new object[] { new { type = "tool_result", tool_use_id = id, content = output, is_error = error } } },
        ["toolUseResult"] = backgroundTask is null ? null : new { stdout = "", backgroundTaskId = backgroundTask },
    }.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value));

    internal static string NoteBlock(string task, string toolUseId, int exit, string? outputFile = null) =>
        $"<task-notification>\n<task-id>{task}</task-id>\n<tool-use-id>{toolUseId}</tool-use-id>\n" +
        $"<output-file>{outputFile ?? @"C:\none\x.output"}</output-file>\n<status>{(exit == 0 ? "completed" : "failed")}</status>\n" +
        $"<summary>Background command \"run\" completed (exit code {exit})</summary>\n</task-notification>";

    internal static string Feedback() => User("Stop hook feedback:\nkor capture-check: ...", origin: null, meta: true);

    internal static string Write(params string[] lines)
    {
        var p = Path.Combine(Path.GetTempPath(), $"kor-1393-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(p, string.Join("\n", lines) + "\n");
        return p;
    }

    private static List<TurnCapture.Reason> Reasons(params string[] lines)
    {
        var turns = TurnCapture.Turns(Write(lines));
        var t = turns[^1];
        var launches = turns.SelectMany(x => x.Tools).ToDictionary(x => x.Id, x => x);
        return TurnCapture.Evaluate(t, TurnCapture.ForegroundRuns(t).Concat(TurnCapture.BackgroundRuns(t, launches, _ => null)).ToList());
    }

    // ---- part 1: pipeline runs -----------------------------------------------------------------------------------------
    [Theory]
    [InlineData("cd \"/c/VIsual Studio Projects/KOR.Drafter\" && python -I tools/virtual_drafter.py run jobs/x/job.json --from read",
                "== read\n  PASS read gate: ok\n  FAIL plan text: 3 tags\nGATE FAILED: plan text", null, "virtual_drafter run jobs/x/job.json --from read", "fail")]
    [InlineData("python tools/virtual_drafter.py check-plot jobs/x/job.json", "  PASS cover: ok\n  PASS dims: ok", null, "virtual_drafter check-plot jobs/x/job.json", "pass")]
    [InlineData("python tools/virtual_drafter.py run jobs/x/job.json", "run refused before writing anything - 1 reason(s):\n  promoted", 1, "virtual_drafter run jobs/x/job.json", "refused")]
    [InlineData("python -I tools/plot_dim_text_check.py a.pdf", "DIMTEXT FAIL: 1 finding(s) over 702 dimension values", 1, "plot_dim_text_check.py a.pdf", "fail")]
    [InlineData("python tools/copies_check.py", "copies: 12 PASS, 0 FAIL", null, "copies_check.py", "pass")]
    public void APipelineRunIsAGateEventWithItsOutcomeAndGateLines(string command, string output, int? exit, string reference, string outcome)
    {
        var e = ToolClassifier.Classify(command, output, exit)!;
        Assert.Equal(("gate", reference, outcome), (e.Kind, e.Ref, e.Outcome));
        using var d = JsonDocument.Parse(e.Detail);
        Assert.True(d.RootElement.TryGetProperty("pipeline", out _));
        var gates = d.RootElement.GetProperty("gates").EnumerateArray().Select(g => g.GetString()!).ToList();
        if (outcome == "fail") Assert.Matches("FAIL", gates[0]);   // the failing lines come first
    }

    [Theory]
    [InlineData("grep -n \"virtual_drafter.py run\" docs/notes.md")]
    [InlineData("python -m py_compile tools/plan_text_check.py")]
    [InlineData("sed -n 1,20p tools/plan_text_check.py")]
    [InlineData("git commit -m \"python tools/virtual_drafter.py run is faster\"")]
    public void MentioningAPipelineIsNotRunningIt(string command) => Assert.Null(ToolClassifier.Classify(command, "  FAIL x: y", 1));

    [Fact]
    public void ABackgroundLaunchIsNotAnEventAtPostToolUse()
    {
        var launch = ClaudeCapture.Parse(JsonSerializer.Serialize(new
        {
            session_id = "s", cwd = @"C:\x", hook_event_name = "PostToolUse", tool_name = "Bash",
            tool_input = new { command = "python tools/virtual_drafter.py run jobs/x/job.json", run_in_background = true },
            tool_response = new { stdout = "", stderr = "", backgroundTaskId = "b123" },
        }));
        Assert.Equal("b123", launch.BackgroundTaskId);
        Assert.Null(ClaudeCapture.Tool(launch, "zz"));
    }

    [Fact]
    public void ABackgroundRunIsReadFromItsNotificationAndOutputFile()
    {
        var turns = TurnCapture.Turns(Write(
            User("Run it."), Tool("t1", "python tools/virtual_drafter.py run jobs/x/job.json", background: true), Result("t1", "Command running in background with ID: b77", backgroundTask: "b77"),
            Say("Started."),
            User(NoteBlock("b77", "t1", 1, @"C:\out\b77.output"), origin: "task-notification"), Say("It stopped.")));
        var launches = turns.SelectMany(x => x.Tools).ToDictionary(x => x.Id, x => x);
        var runs = TurnCapture.BackgroundRuns(turns[^1], launches, f => f == @"C:\out\b77.output" ? "== drafting\nSTEP FAILED: drafted set (exit 1) - see run.json" : null);
        var r = Assert.Single(runs);
        Assert.Equal(("gate", "fail", "b77"), (r.Event.Kind, r.Event.Outcome, r.Note!.TaskId));
        Assert.Empty(TurnCapture.ForegroundRuns(turns[0]));   // the launch turn ran nothing yet
    }

    // review row 364: no exit code is invented - a run stopped from outside (no code in its summary) is no outcome
    [Fact]
    public void ABackgroundRunStoppedWithNoExitCodeIsNoOutcome()
    {
        var killed = "<task-notification>\n<task-id>bk1</task-id>\n<tool-use-id>t1</tool-use-id>\n<output-file>C:\\o\\bk1.output</output-file>\n" +
                     "<status>killed</status>\n<summary>Background command \"run\" was stopped</summary>\n</task-notification>";
        var turns = TurnCapture.Turns(Write(
            User("Run it."), Tool("t1", "python tools/virtual_drafter.py run jobs/x/job.json", background: true), Result("t1", "started", backgroundTask: "bk1"),
            Say("Started."), User(killed, origin: "task-notification"), Say("Stopped.")));
        var launches = turns.SelectMany(x => x.Tools).ToDictionary(x => x.Id, x => x);
        Assert.Empty(TurnCapture.BackgroundRuns(turns[^1], launches, _ => "GATE FAILED: x"));
        Assert.Null(Assert.Single(turns[^1].Notifications).ExitCode);
    }

    [Fact]
    public void ARunWritingToAFileIsReadFromThatFile()
    {
        Assert.Equal(Path.GetFullPath(@"C:\repo\work\v5.log"),
                     TurnCapture.RedirectTarget("cd \"/c/repo\" && python -I tools/stage_gate.py run g.json > work/v5.log 2>&1", @"C:\elsewhere"));
        Assert.Null(TurnCapture.RedirectTarget("python x.py 2>/dev/null", @"C:\x"));
    }

    // ---- turns ----------------------------------------------------------------------------------------------------------
    [Fact]
    public void StopHookFeedbackContinuesTheTurnAndPeersAndNotificationsStartOne()
    {
        var turns = TurnCapture.Turns(Write(
            User("Go."), Say("Done."), Feedback(), Say("Recorded nothing - nothing new."),
            User("Another Claude session sent a message:\n<cross-session-message from=\"x\">hi</cross-session-message>", origin: "peer", meta: true, peer: "OverLORD"),
            Say("ok"),
            User(NoteBlock("a1", "t9", 0) + "\n" + NoteBlock("a2", "t8", 2), origin: "task-notification"), Say("two finished")));
        Assert.Equal(new[] { "human", "peer", "task-notification" }, turns.Select(t => t.Start.Origin));
        Assert.Equal("Recorded nothing - nothing new.", turns[0].FinalReply);
        Assert.Equal("OverLORD", turns[1].Start.PeerName);
        Assert.Equal(new[] { ("a1", 0), ("a2", 2) }, turns[2].Notifications.Select(n => (n.TaskId, n.ExitCode ?? -1)));
    }

    [Fact]
    public void AClaudeDashPTurnIsNeverAsked() =>
        Assert.Empty(Reasons(User("You are an independent code reviewer. Don't approve unless ... Never ...", origin: null, entry: "sdk-cli"),
                             Tool("t1", "dotnet test x.csproj"), Result("t1", "Failed!  - Failed: 2, Passed: 1", error: true),
                             Say("The root cause was a static.")));

    // ---- part 2: what a turn produced ------------------------------------------------------------------------------------
    [Fact]
    public void AFailedTestClearedByALaterPassIsNotAsked()
    {
        Assert.Empty(Reasons(User("fix it"), Tool("a", "dotnet test x.csproj"), Result("a", "Failed!  - Failed: 1", error: true),
                             Tool("b", "dotnet test x.csproj"), Result("b", "Passed!  - Failed: 0, Passed: 3"), Say("Fixed a typo.")));
        var r = Assert.Single(Reasons(User("fix it"), Tool("a", "dotnet build x.csproj"), Result("a", "x.cs(1,1): error CS0103: nope", error: true), Say("It does not build.")));
        Assert.Equal("failed-run", r.Kind);
    }

    [Fact]
    public void ACommandAHookRefusedIsNoRun()
    {
        const string refused = "PreToolUse:Bash hook error: A dotnet build/test/publish piped without pipefail";
        Assert.True(ToolClassifier.IsHookRefusal(refused));
        Assert.Empty(Reasons(User("build it"), Tool("a", "dotnet build x.csproj | tail -3"), Result("a", refused, error: true), Say("Re-running.")));
        // and capture-tool records nothing for it (it was recorded as a failed build before row 1393)
        Assert.Null(ClaudeCapture.Tool(ClaudeCapture.Parse(JsonSerializer.Serialize(new
        {
            session_id = "s", cwd = @"C:\x", hook_event_name = "PostToolUseFailure", tool_name = "Bash",
            tool_input = new { command = "dotnet build x.csproj | tail -3" }, error = refused,
        })), "zz"));
        // a quiet build that says 0 Error(s) passed, whatever a later step's exit code
        Assert.Equal("pass", ToolClassifier.Classify("dotnet build x.csproj -v q; grep foo bar", "    0 Warning(s)\n    0 Error(s)", 1)!.Outcome);
    }

    [Fact]
    public void AFailureReadInOutputIsAskedOnceNotOnItsReRead()
    {
        const string log = "== drafting\nstage_gate: STOPPED at stage 3 of 3 'Revit run'\nDIMTEXT FAIL: 1 finding(s) over 702 dimension values";
        var turns = TurnCapture.Turns(Write(
            User("Status?"), Tool("a", "tail -20 work/v5.log"), Result("a", log), Say("It stopped on one finding."),
            User("and now?"), Tool("b", "grep -n FAIL work/v5.log"), Result("b", log), Say("Same as before.")));
        var first = TurnCapture.Evaluate(turns[0], TurnCapture.ForegroundRuns(turns[0]));
        Assert.Equal("failed-gate-seen", Assert.Single(first).Kind);
        Assert.Empty(TurnCapture.Evaluate(turns[1], TurnCapture.ForegroundRuns(turns[1])));   // history
        // a grep of the SOURCE is not a failure read
        Assert.Empty(Reasons(User("where?"), Tool("c", "grep -n GATE tools/v.py"), Result("c", "86:            raise SystemExit(f\"GATE FAILED: {name}\")"), Say("Line 86.")));
    }

    [Theory]
    [InlineData("The root cause was the agent targeted net48.", true)]
    [InlineData("It stopped because the 5'-0\" text touches the tick, which is why the gate failed.", true)]
    [InlineData("I'll delete them all if they turn out to be old build leftovers.", false)]
    [InlineData("Clean means the bug is in the reload.", false)]
    [InlineData("Done: 3 files changed.", false)]
    // judged false in the dry run (3 Nov): a design or the session's own method, a hedged or absent cause
    // a design explained with "which is why" IS asked - words cannot tell it from a cause (pass 4 tried: lost ~85 real
    // diagnoses to save ~40 of these); the session answers it in one line
    [InlineData("That code runs live for the first time here, which is why it dry-runs before deleting anything.", true)]
    [InlineData("That's why I ran the hashes.", false)]
    [InlineData("The root cause is still unknown.", false)]
    [InlineData("So Kor's APC account now signs in as Islam, which is why bdalerts@ was rejected.", true)]
    [InlineData("You've hit your monthly spend limit. The root cause was X.", false)]
    public void ADiagnosisIsACauseNotAHypothetical(string reply, bool asked) => Assert.Equal(asked, TurnCapture.Diagnosis(reply) is not null);

    // ---- part 3: Ian's decisions -----------------------------------------------------------------------------------------
    [Theory]
    // from the dry run (3 Nov), the real messages
    [InlineData("Holy fuck - if this thing is hung or is going to take another hour - maybe? - then let's use v4 as the base from which to compare the fixed app.", null, true)]
    [InlineData("FIXED MEANS IT\"S ALL FUCKING FIXED AND WE DON\"T NEED TO KEEP FUCKING DOING THIS", null, true)]
    [InlineData("Don't write the dossier until all this is immaculate.", null, true)]
    [InlineData("yes add the hook", null, true)]
    [InlineData("Go", null, true)]
    [InlineData("OK", "Shall I start the rerun?", true)]
    [InlineData("OK - it's running", "Started.", false)]
    [InlineData("Other session still running. I don't remember another clusterfuck this bad.", null, false)]
    [InlineData("Seems like a never ending path to get there", null, false)]
    [InlineData("Status?", null, false)]
    [InlineData("What are you asking? I want this done intelligently for once?", null, false)]
    [InlineData("<pasted_content id=\"1\">Never ship it. Use v2 as the base.</pasted_content>", null, false)]
    [InlineData("<system-reminder>Don't do X.</system-reminder>", null, false)]
    [InlineData("Stop patronizing me", null, false)]
    [InlineData("Don't get off track. What's next", null, false)]
    [InlineData("NO GUESSING", null, false)]
    [InlineData("No", "Should I push it now?", true)]
    [InlineData("Fix the Financials engine faults, then please go ahead", null, true)]
    [InlineData("We'll use 302N", null, true)]
    [InlineData("Make the colors KOR palette. Go", null, true)]
    public void IansDecisionIsWhatHeTypedAndChooses(string message, string? previousReply, bool decided) =>
        Assert.Equal(decided, TurnCapture.IsDecision(message, previousReply) is not null);

    [Fact]
    public void APeerRelayingIanIsNotIansDecisionInTheReceiver() =>
        Assert.DoesNotContain(Reasons(User("Another Claude session sent a message:\n<cross-session-message from=\"x\">OverLORD, from Ian: \"don't run v6 yet\"</cross-session-message>",
                                           origin: "peer", meta: true, peer: "OverLORD"), Say("Holding v6.")),
                              r => r.Kind == "ian-decision");
}

/// <summary>Row 1393, live: the whole Stop verb on a real transcript file, against KorStandards in a rolled-back transaction.</summary>
[Collection(LiveDb.Name)]
public class TurnAskLiveTests
{
    private static readonly string Module = $"zz-test-1393-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..4]}";
    private const string Planted = "Vt7#kLm2pQ";   // synthetic, as SecretGuardTests

    private static string NewSession() => "zz-1393-" + Guid.NewGuid().ToString("N");

    private static (string Out, string Err) Stop(BrainDb db, string session, string transcript, bool active = false, string? last = null)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var json = JsonSerializer.Serialize(new
        {
            session_id = session, transcript_path = transcript, cwd = Path.GetTempPath(), hook_event_name = "Stop", stop_hook_active = active,
            last_assistant_message = last,
        });
        Program.Run(new[] { "capture-check", "--module", Module, "--hook" }, () => json, o, e, Path.Combine(Path.GetTempPath(), $"kor-1393-{Guid.NewGuid():N}.log"), db);
        return (o.ToString(), e.ToString());
    }

    private static string? Block(string output)
    {
        if (output.Trim().Length == 0) return null;
        using var j = JsonDocument.Parse(output);
        Assert.Equal("block", j.RootElement.GetProperty("decision").GetString());
        return j.RootElement.GetProperty("reason").GetString();
    }

    private static string FailedRunTurn() => TurnCaptureTests.Write(
        TurnCaptureTests.User("Run the check.", minutesAgo: 1),
        TurnCaptureTests.Tool("t1", "python -I tools/plot_dim_text_check.py a.pdf"),
        TurnCaptureTests.Result("t1", "DIMTEXT FAIL: 1 finding(s) over 702 dimension values", error: true),
        TurnCaptureTests.Say("One dimension touches linework on S2.01."));

    [Fact]
    public void LiveATurnWithAFailedRunIsAskedOnceAndTheSecondStopAllows()
    {
        using var db = BrainDb.OpenRolledBack();
        var s = NewSession();
        var t = FailedRunTurn();
        var reason = Block(Stop(db, s, t).Out)!;
        Assert.Contains("record what it found (kor record ...), or say in one line why there is nothing", reason);
        Assert.Contains("- a failed run: gate fail: plot_dim_text_check.py a.pdf", reason);
        var ask = Assert.Single(BrainStore.SessionEventsSince(s, new[] { "capture-ask" }, DateTimeOffset.UtcNow.AddMinutes(-5), db));
        Assert.Contains("\"failed-run\"", ask.Detail);
        Assert.Equal("", Stop(db, s, t).Out);                 // asked already in this turn: allowed, whatever else holds
        Assert.Equal("", Stop(db, s, t, active: true).Out);   // and a continuation Stop is allowed by the same marker
        Assert.Single(BrainStore.SessionEventsSince(s, new[] { "capture-ask" }, DateTimeOffset.UtcNow.AddMinutes(-5), db));
    }

    /// <summary>A continuation Stop (another Stop hook blocked first) is judged: the work after that block was never asked
    /// before row 1393 (the dry run found failed runs, a migration among them, only in such continuations).</summary>
    [Fact]
    public void LiveAContinuationStopIsAskedWhenTheTurnWasNotYet()
    {
        using var db = BrainDb.OpenRolledBack();
        var s = NewSession();
        Assert.Contains("- a failed run:", Block(Stop(db, s, FailedRunTurn(), active: true).Out)!);
        Assert.Equal("", Stop(db, s, FailedRunTurn(), active: true).Out);   // and once only
    }

    /// <summary>The loop guard that does not depend on reading the turn right: on a continuation, an ask of this session in
    /// the last 10 minutes allows, even when the turn read looks new (a prompt written after that ask).</summary>
    [Fact]
    public void LiveOnAContinuationARecentAskAllowsWhateverTurnWasRead()
    {
        using var db = BrainDb.OpenRolledBack();
        var s = NewSession();
        BrainStore.Insert(new BrainEvent(Module, "capture-ask", null, null, "turn: failed-run", "asked", "{}", "test", s), db);
        var newer = TurnCaptureTests.Write(
            TurnCaptureTests.User("Run the check.", minutesAgo: -1),   // a prompt timed after that ask: read as a new turn
            TurnCaptureTests.Tool("t1", "python -I tools/plot_dim_text_check.py a.pdf"),
            TurnCaptureTests.Result("t1", "DIMTEXT FAIL: 1 finding(s) over 702 dimension values", error: true),
            TurnCaptureTests.Say("One dimension touches linework."));
        Assert.Equal("", Stop(db, s, newer, active: true).Out);
        Assert.Contains("- a failed run:", Block(Stop(db, s, newer, active: false).Out)!);   // not a continuation: a new turn is asked
    }

    // review row 364: a repeated tool_use id in a turn with a notification must not throw (it would lose the whole Stop check)
    [Fact]
    public void LiveARepeatedToolIdDoesNotLoseTheCheck()
    {
        using var db = BrainDb.OpenRolledBack();
        var s = NewSession();
        var t = TurnCaptureTests.Write(
            TurnCaptureTests.User("Run the check.", minutesAgo: 2),
            TurnCaptureTests.Tool("dup", "python -I tools/plot_dim_text_check.py a.pdf"),
            TurnCaptureTests.Result("dup", "DIMTEXT FAIL: 1 finding(s) over 702 dimension values", error: true),
            TurnCaptureTests.Tool("dup", "git status"), TurnCaptureTests.Result("dup", "clean"),
            TurnCaptureTests.User(TurnCaptureTests.NoteBlock("bz9", "elsewhere", 0), origin: "task-notification", minutesAgo: 1),
            TurnCaptureTests.Say("done"));
        var (o, e) = Stop(db, s, t);
        Assert.Equal("", e);
        Assert.Equal("", o);   // the notification's turn: nothing to ask, and no exception swallowed into silence
    }

    [Fact]
    public void LiveARecordInTheTurnSatisfiesTheAsk()
    {
        using var db = BrainDb.OpenRolledBack();
        var s = NewSession();
        BrainStore.Insert(new BrainEvent(Module, "record", null, null, "brain.Memory #1 finding x", "recorded", "{}", "test", s), db);
        Assert.Equal("", Stop(db, s, FailedRunTurn()).Out);
        Assert.Empty(BrainStore.SessionEventsSince(s, new[] { "capture-ask" }, DateTimeOffset.UtcNow.AddMinutes(-5), db));
    }

    [Fact]
    public void LiveATurnThatProducedNothingIsNotAsked()
    {
        using var db = BrainDb.OpenRolledBack();
        var s = NewSession();
        var t = TurnCaptureTests.Write(TurnCaptureTests.User("Status?"), TurnCaptureTests.Tool("a", "git status"), TurnCaptureTests.Result("a", "clean"),
                                       TurnCaptureTests.Say("Clean tree, nothing running."));
        Assert.Equal("", Stop(db, s, t).Out);
        Assert.Empty(BrainStore.SessionEventsSince(s, new[] { "capture-ask" }, DateTimeOffset.UtcNow.AddMinutes(-5), db));
    }

    [Fact]
    public void LiveIansDecisionIsAskedWithHisWordsAndTheAskIsItsSourceAndNoCredentialIsKept()
    {
        using var db = BrainDb.OpenRolledBack();
        var s = NewSession();
        var t = TurnCaptureTests.Write(TurnCaptureTests.Say("Should I rerun v5, or patch it?", 3),
                                       TurnCaptureTests.User($"No - let's use v4 as the base. The demo box password is {Planted}"),
                                       TurnCaptureTests.Say("Using v4 as the base."));
        var (o, _) = Stop(db, s, t);
        var reason = Block(o)!;
        var ask = Assert.Single(BrainStore.SessionEventsSince(s, new[] { "capture-ask" }, DateTimeOffset.UtcNow.AddMinutes(-5), db));
        Assert.Contains("- Ian decided: \"No - let's use v4 as the base.", reason);
        Assert.Contains($"--kind decision --name <short-slug> --says \"Ian: <his words>\" --source run:{ask.Id}", reason);
        Assert.Contains("let's use v4 as the base", ask.Detail);
        Assert.DoesNotContain(Planted, ask.Detail);
        Assert.DoesNotContain(Planted, o);
    }

    [Fact]
    public void LiveABackgroundRunIsRecordedOnceWhenItsResultArrives()
    {
        using var db = BrainDb.OpenRolledBack();
        var s = NewSession();
        var outFile = Path.Combine(Path.GetTempPath(), $"kor-1393-{Guid.NewGuid():N}.output");
        File.WriteAllText(outFile, "== drafting\n  PASS cover: ok\n  FAIL plan text: 3 tags\nGATE FAILED: plan text\n");
        var t = TurnCaptureTests.Write(
            TurnCaptureTests.User("Run v5.", minutesAgo: 30),
            TurnCaptureTests.Tool("t1", "python tools/virtual_drafter.py run jobs/x/job.json", background: true),
            TurnCaptureTests.Result("t1", "Command running in background with ID: bq1", backgroundTask: "bq1"),
            TurnCaptureTests.Say("Started.", 30),
            TurnCaptureTests.User(TurnCaptureTests.NoteBlock("bq1", "t1", 1, outFile), origin: "task-notification"),
            TurnCaptureTests.Say("v5 stopped on plan text."));
        var reason = Block(Stop(db, s, t).Out)!;
        Assert.Contains("- a failed run: gate fail: virtual_drafter run jobs/x/job.json (background, its result arrived in this turn)", reason);
        var run = Assert.Single(BrainStore.SessionEventsSince(s, new[] { "gate" }, DateTimeOffset.UtcNow.AddMinutes(-5), db));
        Assert.Equal(("fail", "virtual_drafter run jobs/x/job.json"), (run.Outcome, run.Ref));
        Assert.Contains("\"task\":\"bq1\"", run.Detail);
        Assert.Contains("GATE FAILED: plan text", run.Detail);
        Assert.Contains($"#{run.Id} gate fail", reason);   // the ask names the run event to cite
        Stop(db, s, t);                                        // a second Stop in the same turn
        Assert.Single(BrainStore.SessionEventsSince(s, new[] { "gate" }, DateTimeOffset.UtcNow.AddMinutes(-5), db));
    }

    [Fact]
    public void LiveACommitAndATurnFindingAreOneAsk()
    {
        using var db = BrainDb.OpenRolledBack();
        var s = NewSession();
        var start = BrainStore.Insert(new BrainEvent(Module, "session-start", null, null, "startup", null, "{}", "test", s), db);
        BrainStore.Insert(new BrainEvent(Module, "commit", "Operations", new string('a', 40), "x.cs", null,
                                         "{\"message\":\"a fix\",\"files\":[{\"path\":\"x.cs\"}]}", "test", s), db);
        var reason = Block(Stop(db, s, FailedRunTurn()).Out)!;
        Assert.Contains("- a failed run:", reason);
        Assert.Contains("1 commit(s) since you last recorded", reason);
        Assert.Single(BrainStore.SessionEventsSince(s, new[] { "capture-ask" }, DateTimeOffset.UtcNow.AddMinutes(-5), db));
        Assert.True(start > 0);
    }
}
