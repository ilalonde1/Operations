using System.Text.Json;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Context;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// G5, the context cliff (board row 1474). Covers:
/// <list type="bullet">
/// <item>The window from the model id (1M for the documented ids and "[1m]"; 200k for the 4.6 and older ids and for an
/// unknown id).</item>
/// <item>The auto-compact ceiling from settings in Claude Code's documented order: env, then project local, project and
/// user; within a file a model's entry beats the top level; "auto" and "off" give the model's window. A 900k ceiling
/// keeps WARN 250k and STOP 300k; a 200k one gets 140k and 170k.</item>
/// <item>The reading: the four usage fields summed; a compaction resets it to postTokens; "Prompt is too long" puts it
/// past the cliff; a main transcript's sidechain is skipped; the hook's own WARN is read back from an attachment and not
/// from a tool result.</item>
/// <item>WARN said once per step. Every work tool refused at STOP. Every handover step allowed, each lookalike that does
/// work refused.</item>
/// <item>The Stop block: once, only until the handover is written.</item>
/// <item>The three real cliffs (usage-only extracts in Fixtures/g5-seed-*) WARNED and STOPPED before the cliff, with
/// their handover edits allowed.</item>
/// <item>The hook verb's JSON for PreToolUse (deny and additionalContext), UserPromptSubmit and Stop. The refusal logs one
/// oversight.GuardRefusal row on the live KorStandards, rolled back.</item>
/// </list>
/// Does NOT cover: the 30-day corpus (kor context --replay does); the hook's wiring (EnrollCheckTests); what the model
/// does with the text (the live hook run on the board row's Evidence).
/// </summary>
[Collection(LiveDb.Name)]
public class ContextTests
{
    private static readonly CompactSettings Off = CompactSettings.Off;

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static string Assistant(DateTimeOffset at, string model, long input, long cacheRead, long out_, params (string Id, string Name, object Input)[] tools) =>
        JsonSerializer.Serialize(new
        {
            type = "assistant", isSidechain = false, timestamp = at.ToString("O"),
            message = new
            {
                id = Guid.NewGuid().ToString("N"), model,
                content = tools.Select(t => (object)new { type = "tool_use", id = t.Id, name = t.Name, input = t.Input }).ToArray(),
                usage = new { input_tokens = input, cache_read_input_tokens = cacheRead, cache_creation_input_tokens = 0, output_tokens = out_ },
            },
        });

    private static string Result(string id, string text, bool error = false) =>
        JsonSerializer.Serialize(new { type = "user", message = new { role = "user", content = new object[] { new { type = "tool_result", tool_use_id = id, content = text, is_error = error } } } });

    private static readonly DateTimeOffset T0 = new(2026, 11, 3, 23, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("claude-opus-5-5", 1_000_000)]
    [InlineData("claude-opus-5", 1_000_000)]
    [InlineData("claude-fable-5-1", 1_000_000)]
    [InlineData("claude-opus-4-8", 1_000_000)]
    [InlineData("claude-haiku-5-5", 1_000_000)]
    [InlineData("claude-sonnet-5-5", 1_000_000)]
    [InlineData("claude-opus-4-6[1m]", 1_000_000)]
    [InlineData("claude-opus-4-6", 200_000)]
    [InlineData("claude-sonnet-4-6", 200_000)]
    [InlineData("claude-haiku-4-5-20251001", 200_000)]
    [InlineData("claude-opus-6", 200_000)]          // unknown: the smaller window, warn early
    [InlineData(null, 200_000)]
    public void TheWindowComesFromTheModelIdAndAnUnknownIdTakesTheSmaller(string? model, long window) =>
        Assert.Equal(window, ContextGuard.WindowOf(model));

    [Fact]
    public void TheAutoCompactWindowIsReadInClaudeCodesOrder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "g5-cs-" + Guid.NewGuid().ToString("N")[..8]);
        var user = Path.Combine(dir, "home");
        var cwd = Path.Combine(dir, "repo");
        Directory.CreateDirectory(user); Directory.CreateDirectory(Path.Combine(cwd, ".claude"));
        try
        {
            // the user file as Ian set it, 3 Nov 21:26
            File.WriteAllText(Path.Combine(user, "settings.json"),
                "{\"autoCompactEnabled\": true, \"modelSettings\": {\"claude-opus-5-5\": {\"autoCompactWindow\": 900000}}}");
            var cs = CompactSettings.Read(cwd, user, envWindow: "");
            Assert.Equal(900_000, cs.WindowFor("claude-opus-5-5"));
            Assert.Equal(900_000, cs.WindowFor("claude-opus-5-5[1m]"));     // matched as Claude Code does
            Assert.Null(cs.WindowFor("claude-fable-5-1"));                  // no entry: the model's own window
            Assert.Equal(900_000, ContextGuard.CeilingOf("claude-opus-5-5", cs));
            Assert.Equal(250_000, ContextGuard.WarnAt(ContextGuard.CeilingOf("claude-opus-5-5", cs)));   // Ian's lines hold on a 900k ceiling
            Assert.Equal(300_000, ContextGuard.StopAt(ContextGuard.CeilingOf("claude-opus-5-5", cs)));
            Assert.Equal(140_000, ContextGuard.WarnAt(200_000));                                          // a ceiling too small for them: 70% and 85%
            Assert.Equal(170_000, ContextGuard.StopAt(200_000));
            Assert.Equal(1_000_000, ContextGuard.CeilingOf("claude-fable-5-1", cs));

            // the shared project file's top-level window beats the user file's model entry (a higher file decides)
            File.WriteAllText(Path.Combine(cwd, ".claude", "settings.json"), "{\"autoCompactWindow\": 700000}");
            Assert.Equal(700_000, CompactSettings.Read(cwd, user, "").WindowFor("claude-opus-5-5"));
            // project local beats the shared project; within it the model's entry beats its top level
            File.WriteAllText(Path.Combine(cwd, ".claude", "settings.local.json"),
                "{\"autoCompactWindow\": 600000, \"modelSettings\": {\"claude-opus-5-5\": {\"autoCompactWindow\": 500000}}}");
            Assert.Equal(500_000, CompactSettings.Read(cwd, user, "").WindowFor("claude-opus-5-5"));
            Assert.Equal(600_000, CompactSettings.Read(cwd, user, "").WindowFor("claude-fable-5-1"));
            // the environment beats every file
            Assert.Equal(400_000, CompactSettings.Read(cwd, user, "400000").WindowFor("claude-opus-5-5"));
            // "auto" is the model's own window; auto-compact off in the highest file that says: no window at all
            File.WriteAllText(Path.Combine(cwd, ".claude", "settings.local.json"), "{\"modelSettings\": {\"claude-opus-5-5\": {\"autoCompactWindow\": \"auto\"}}}");
            Assert.Null(CompactSettings.Read(cwd, user, "").WindowFor("claude-opus-5-5"));
            File.WriteAllText(Path.Combine(cwd, ".claude", "settings.local.json"), "{\"autoCompactEnabled\": false}");
            Assert.Null(CompactSettings.Read(cwd, user, "").WindowFor("claude-opus-5-5"));
            Assert.Equal(1_000_000, ContextGuard.CeilingOf("claude-opus-5-5", CompactSettings.Read(cwd, user, "")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void TheReadingSumsTheUsageAndACompactionOrTheCliffChangesIt()
    {
        var lines = new List<string>
        {
            Assistant(T0, "claude-opus-5-5", 10, 190_000, 2_000),
            JsonSerializer.Serialize(new { type = "assistant", isSidechain = true, timestamp = T0.AddMinutes(1).ToString("O"),
                                           message = new { model = "claude-haiku-5-5", content = Array.Empty<object>(), usage = new { input_tokens = 5, cache_read_input_tokens = 0, cache_creation_input_tokens = 0, output_tokens = 1 } } }),
        };
        var r = ContextTranscript.Read(lines, lines.Count, false, null, Off);
        Assert.Equal(192_010, r.Tokens);                 // in + cache read + cache creation + out; the sidechain skipped
        Assert.Equal("claude-opus-5-5", r.Model);
        Assert.Equal(ContextLevel.Ok, ContextGuard.LevelOf(r, Off));

        lines.Add(JsonSerializer.Serialize(new { type = "assistant", timestamp = T0.AddMinutes(2).ToString("O"), isApiErrorMessage = true,
                                                 message = new { model = "<synthetic>", content = new[] { new { type = "text", text = "Prompt is too long" } }, usage = new { input_tokens = 0, output_tokens = 0 } } }));
        r = ContextTranscript.Read(lines, lines.Count, false, null, Off);
        Assert.True(r.OverCliff);
        Assert.Equal(ContextLevel.Stop, ContextGuard.LevelOf(r, Off));   // past the cliff is STOP whatever the number

        lines.Add(JsonSerializer.Serialize(new { type = "system", subtype = "compact_boundary", timestamp = T0.AddMinutes(3).ToString("O"),
                                                 compactMetadata = new { trigger = "manual", preTokens = 977_931, postTokens = 39_999 } }));
        r = ContextTranscript.Read(lines, lines.Count, false, null, Off);
        Assert.Equal(39_999, r.Tokens);
        Assert.False(r.OverCliff);
        Assert.Equal("compact", r.Basis);
    }

    [Fact]
    public void TheWarningIsSaidOncePerStepAndReadBackOnlyFromItsOwnAttachment()
    {
        var w = 1_000_000L;
        Assert.True(ContextGuard.ShouldWarn(250_500, w, null));
        Assert.False(ContextGuard.ShouldWarn(270_000, w, 250_500));
        Assert.True(ContextGuard.ShouldWarn(275_500, w, 250_500));
        var warn = ContextGuard.WarnText(new ContextReading(262_345, "claude-opus-5-5", T0, 1), Off);
        Assert.StartsWith(ContextGuard.WarnMarker + "262,345 tokens", warn);

        var lines = new List<string>
        {
            Assistant(T0, "claude-opus-5-5", 1, 261_000, 1_000),
            JsonSerializer.Serialize(new { type = "attachment", attachment = new { type = "hook_additional_context", content = new[] { warn } } }),
            Assistant(T0.AddMinutes(1), "claude-opus-5-5", 1, 265_000, 500),
        };
        Assert.Equal(262_345, ContextTranscript.Read(lines, lines.Count, false, null, Off).LastWarned);
        // a tool result quoting the marker (a grep of this very file) is not the hook's warning
        lines[1] = Result("x", warn);
        Assert.Null(ContextTranscript.Read(lines, lines.Count, false, null, Off).LastWarned);
    }

    private static ContextDecision AtStop(string tool, string text, bool sub = false) =>
        ContextGuard.Decide(new ContextReading(301_321, "claude-opus-5-5", T0, 1), tool, text, "C:/Users/x/AppData/Local/Temp/claude/s/scratchpad", sub, "Operations", Off);

    [Theory]
    [InlineData("Bash", "dotnet test Kor.Operations.Framework.Tests")]
    [InlineData("Bash", "git push origin main")]
    [InlineData("Bash", "git add . && git commit -m wip")]
    [InlineData("Bash", "git add docs/HANDOVER.md src/x.cs")]
    [InlineData("Bash", "git commit -am \"handover\"")]
    [InlineData("Bash", "git commit --amend --no-edit")]
    [InlineData("Bash", "date > docs/HANDOVER.md")]
    [InlineData("Bash", "echo x | tee notes.txt")]
    [InlineData("Bash", "kor state --module Operations --file s.md && python -I tools/SqlQuery/oq.py -q \"UPDATE x\"")]
    [InlineData("Bash", "bash -c \"kor state --module Operations --text x\"")]
    [InlineData("Bash", "git commit -m \"$(cat <<'EOF'\nhandover\nEOF\n)\" && git push")]          // a heredoc never hides what follows it
    [InlineData("Bash", "git commit -F - <<'EOF'\nhandover\nEOF\ngit push origin main")]
    [InlineData("Bash", "cat <<'EOF' > src/x.cs\nclass X {}\nEOF")]                                  // nor what sits on its opener's line (review row 489)
    [InlineData("Bash", "cat <<'EOF' | bash\ndotnet build\nEOF")]
    [InlineData("Bash", "git commit -F - <<'EOF' && git push\nhandover\nEOF")]
    [InlineData("Bash", "git commit -m wip")]                                                         // no path: the whole index (review row 493)
    [InlineData("Bash", "git commit -F -")]
    [InlineData("Bash", "git commit")]
    [InlineData("Bash", "git commit \":(exclude)docs/HANDOVER.md\" -m wip")]
    [InlineData("Bash", "git commit \":!docs/HANDOVER.md\" -m wip")]
    [InlineData("Bash", "git add \":!docs/HANDOVER.md\"")]
    [InlineData("Bash", "git commit \\:\\!docs/HANDOVER.md -m wip")]                                   // magic, escaped or expanded (review row 495)
    [InlineData("Bash", "X=:; git commit \"$X!docs/HANDOVER.md\" -m wip")]
    [InlineData("PowerShell", "git commit `:!docs/HANDOVER.md -m wip")]
    [InlineData("Bash", "echo x >&2x")]
    [InlineData("Bash", "date >&1.cs")]
    [InlineData("Bash", "git commit . -m wip")]                                                       // a commit is the handover or nothing (review row 491)
    [InlineData("Bash", "git commit src/x.cs -m wip")]
    [InlineData("Bash", "git commit -i src/x.cs -m wip")]
    [InlineData("Bash", "git commit --al -m wip")]
    [InlineData("Bash", "git commit --amen --no-edit")]
    [InlineData("Bash", "git diff --output=src/x.cs")]
    [InlineData("Bash", "git log --outp=f")]
    [InlineData("Bash", "date > nul.txt")]
    [InlineData("Bash", "date > /dev/nullx")]
    [InlineData("PowerShell", "echo x > $nullfile")]
    [InlineData("Bash", "cat docs/PLAN.md")]
    [InlineData("Bash", "git commit -m \"$(date > stamp.txt)\"")]                                    // a redirect inside $( ) is still a write
    [InlineData("Bash", "echo \"a>b\" > notes.txt")]
    [InlineData("PowerShell", "Get-Content big.log | Out-File x.txt")]
    [InlineData("Read", "C:/VIsual Studio Projects/Operations/Kor.Operations.Framework/Context/ContextGuard.cs")]
    [InlineData("Read", "C:/Users/x/AppData/Local/Temp/claude/s/scratchpad/out.txt")]
    [InlineData("Write", "C:/Users/x/AppData/Local/Temp/claude/s/scratchpad/script.py")]
    [InlineData("Write", "C:/VIsual Studio Projects/Operations/docs/PLAN.md")]
    [InlineData("Grep", "{\"pattern\":\"x\"}")]
    [InlineData("Glob", "{\"pattern\":\"**/*.md\"}")]
    [InlineData("Agent", "{\"prompt\":\"read everything\"}")]
    [InlineData("WebFetch", "https://code.claude.com/docs")]
    [InlineData("SendMessage", "{\"to\":\"Virtual Drafter\",\"message\":\"do the next step\"}")]   // a lead dispatching work
    [InlineData("ToolSearch", "{\"query\":\"select:WebSearch\"}")]
    [InlineData("mcp__networkops__networkops_run", "{\"host\":\"KOR-210\"}")]
    public void AtStopEveryWorkToolIsRefused(string tool, string text)
    {
        var d = AtStop(tool, text);
        Assert.True(d.Deny, d.Why);
        Assert.Contains(ContextGuard.StopMarker, d.Say);
        Assert.Contains("kor state --module Operations", d.Say);
        Assert.Contains("Tell Ian in one plain line", d.Say);
    }

    [Theory]
    [InlineData("Read", "C:/VIsual Studio Projects/Framework/docs/HANDOVER.md")]
    [InlineData("Edit", "C:\\VIsual Studio Projects\\KOR.Drafter\\docs\\LEAD-HANDOVER-2026-11-02.md")]
    [InlineData("Write", "C:/Users/x/AppData/Local/Temp/claude/s/scratchpad/STATE-NOW.md")]
    [InlineData("Bash", "date \"+%Y-%m-%d %H:%M %Z\"")]
    [InlineData("Bash", "git commit docs/HANDOVER.md -F - <<'EOF'\nhandover: done\nEOF")]
    [InlineData("Bash", "git add docs/HANDOVER.md && git commit docs/HANDOVER.md -m \"handover: done\"")]   // the STOP text's own step
    [InlineData("Bash", "git log --oneline -3")]
    [InlineData("Bash", "date > /dev/null")]
    [InlineData("Read", "C:/Users/x/AppData/Local/Temp/claude/s/scratchpad/STATE-NOW.md")]
    [InlineData("Bash", "date && git -C \"C:/r\" status --short && git log --oneline -3 && ls \"C:/r/docs\"")]   // the live probe's chain, 4 Nov: refused whole for its ls, and the session gave up on the commit
    [InlineData("Bash", "C:/KOR-Operations/kor/kor.exe state --module Operations --file \"C:/Users/x/AppData/Local/Temp/claude/s/scratchpad/STATE-NOW.md\"")]
    [InlineData("Bash", "kor record --module Operations --kind finding --name x --says y --source query:SELECT 1")]
    [InlineData("Bash", "cd \"/c/VIsual Studio Projects/Operations\" && git status --short && git diff --stat")]
    [InlineData("Bash", "S=\"C:/x/scratchpad\"; git add docs/HANDOVER.md && git commit docs/HANDOVER.md -m \"handover: STATE NOW 21:40\"")]
    [InlineData("Bash", "git commit docs/HANDOVER.md -m \"$(cat <<'EOF'\nhandover: STATE NOW\nEOF\n)\"")]
    [InlineData("Bash", "git -C \"C:/VIsual Studio Projects/Framework\" log --oneline -3 2>&1")]
    // the live run's refused commits (3 Nov 21:50, probe 2ae2e5fc lines 52, 66, 71): a '>' inside a quoted message is no redirect
    [InlineData("Bash", "git add docs/HANDOVER.md && git commit docs/HANDOVER.md -m \"handover: part1 read\n\nCo-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>\" && git log --oneline -2 && git status --short")]
    [InlineData("Bash", "git commit docs/HANDOVER.md -m \"handover: part1 read\" -m \"Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>\"")]
    [InlineData("PowerShell", "& \"C:/KOR-Operations/kor/kor.exe\" state --module Operations --file s.md; Get-Date")]
    [InlineData("TaskStop", "{\"task_id\":\"b4onajjem\"}")]
    [InlineData("ToolSearch", "{\"query\":\"select:TaskStop\",\"max_results\":1}")]
    public void AtStopEveryHandoverStepIsAllowed(string tool, string text)
    {
        var d = AtStop(tool, text);
        Assert.False(d.Deny, d.Why);
        Assert.Equal(ContextLevel.Stop, d.Level);
    }

    [Fact]
    public void ASubagentAtStopReportsToItsLeadAndAMainSessionTellsIan()
    {
        var sub = AtStop("SendMessage", "{\"to\":\"main\",\"message\":\"done: x; not done: y\"}", sub: true);
        Assert.False(sub.Deny, sub.Why);
        var work = AtStop("Bash", "dotnet build", sub: true);
        Assert.True(work.Deny);
        Assert.Contains("Report to your lead now with SendMessage", work.Say);
        Assert.DoesNotContain("Tell Ian", work.Say);
        Assert.Contains("Tell Ian in one plain line that this session has stopped for a fresh start", AtStop("Bash", "dotnet build").Say);
    }

    [Fact]
    public void BelowWarnNothingIsSaidAndInWarnEveryToolStillRuns()
    {
        var ok = ContextGuard.Decide(new ContextReading(249_999, "claude-opus-5-5", T0, 1), "Bash", "dotnet build", null, false, "M", Off);
        Assert.Equal(ContextLevel.Ok, ok.Level);
        Assert.Null(ok.Say);
        var warn = ContextGuard.Decide(new ContextReading(250_000, "claude-opus-5-5", T0, 1), "Bash", "dotnet build", null, false, "M", Off);
        Assert.Equal(ContextLevel.Warn, warn.Level);
        Assert.False(warn.Deny);
        Assert.Contains("300,000", warn.Say);
        var last = ContextGuard.Decide(new ContextReading(299_999, "claude-opus-5-5", T0, 1), "Bash", "dotnet build", null, false, "M", Off);
        Assert.False(last.Deny);
        var stop = ContextGuard.Decide(new ContextReading(300_000, "claude-opus-5-5", T0, 1), "Bash", "dotnet build", null, false, "M", Off);
        Assert.True(stop.Deny);
        Assert.Contains("LIVE", stop.Say);   // what a stopped session leaves running is named first: the decision on the 2 HARM cases
        // a 200k model: WARN 140k, STOP 170k
        Assert.True(ContextGuard.Decide(new ContextReading(170_000, "claude-sonnet-4-6", T0, 1), "Bash", "x", null, false, "M", Off).Deny);
    }

    [Fact]
    public void TheStopBlockAsksOnceAndOnlyUntilTheHandoverIsWritten()
    {
        var lines = new List<string>
        {
            Assistant(T0, "claude-opus-5-5", 1, 290_000, 1_000),                                                       // under STOP
            Assistant(T0.AddMinutes(1), "claude-opus-5-5", 1, 301_000, 500, ("e1", "Edit", new { file_path = "C:/r/docs/HANDOVER.md" })),
            Result("e1", "The file C:/r/docs/HANDOVER.md has been updated successfully."),
        };
        var r = ContextTranscript.Read(lines, lines.Count, false, null, Off);
        Assert.False(r.HandoverWritten);   // the edit alone is not the handover: kor state too
        var d = ContextGuard.DecideStop(r, false, false, "Operations", Off);
        Assert.True(d.Deny);
        Assert.Contains("Write the handover now", d.Say);
        Assert.False(ContextGuard.DecideStop(r, false, stopHookActive: true, "Operations", Off).Deny);   // once, never a loop
        Assert.False(ContextGuard.DecideStop(r, true, false, "Operations", Off).Deny);                   // a subagent's report is its handover

        lines.Add(Assistant(T0.AddMinutes(2), "claude-opus-5-5", 1, 303_000, 300, ("k1", "Bash", new { command = "C:/KOR-Operations/kor/kor.exe state --module Operations --file s.md" })));
        lines.Add(Result("k1", "brain.LaneState 412: Operations, 1820 characters, session x"));
        r = ContextTranscript.Read(lines, lines.Count, false, null, Off);
        Assert.True(r.HandoverWritten);
        Assert.False(ContextGuard.DecideStop(r, false, false, "Operations", Off).Deny);
        Assert.Contains("The handover is written", ContextGuard.Decide(r, "Bash", "dotnet build", null, false, "Operations", Off).Say);

        // a failed kor state is no handover
        lines[^1] = Result("k1", "kor: state needs --module", error: true);
        Assert.False(ContextTranscript.Read(lines, lines.Count, false, null, Off).HandoverWritten);
    }

    /// <summary>The replay's line for the segment that ends at the cliff, from a seed fixture.</summary>
    private static JsonElement SeedSegment(string fixture, string? acWindow = null)
    {
        var output = new StringWriter();
        var args = new List<string> { "context", "--replay", Fixture(fixture) };
        if (acWindow is not null) { args.Add("--auto-compact-window"); args.Add(acWindow); }
        Assert.Equal(0, Program.Run(args.ToArray(), output, new StringWriter()));
        var seg = output.ToString().Split('\n').Where(l => l.StartsWith("{\"transcript\"", StringComparison.Ordinal))
                        .Select(l => JsonDocument.Parse(l).RootElement)
                        .Single(e => e.GetProperty("end").GetProperty("kind").GetString()!.StartsWith("cliff", StringComparison.Ordinal));
        return seg;
    }

    [Theory]
    // fixture, the last good request's time (the cliff follows it), the hours of warning at least, the minutes of STOP at least
    [InlineData("g5-seed-006abbce.jsonl", "2026-11-04T01:56:46Z", 2.0, 25.0)]   // KOR.Drafter, 3 Nov 18:56:46 local (THE seed)
    [InlineData("g5-seed-d0313f99.jsonl", "2026-11-03T20:50:02Z", 3.0, 75.0)]   // Framework lead, 3 Nov 13:50
    [InlineData("g5-seed-eb9075b2.jsonl", "2026-11-01T20:37:36Z", 1.0, 25.0)]   // Operations, 1 Nov 13:37
    public void EachRealCliffIsWarnedWellBeforeAndStoppedBeforeItWithItsHandoverAllowed(string fixture, string cliff, double warnHours, double stopMinutes)
    {
        var at = DateTimeOffset.Parse(cliff, System.Globalization.CultureInfo.InvariantCulture);
        var seg = SeedSegment(fixture);
        var warnAt = seg.GetProperty("warn").GetProperty("at").GetDateTimeOffset();
        var stopAt = seg.GetProperty("stop").GetProperty("at").GetDateTimeOffset();
        Assert.True((at - warnAt).TotalHours >= warnHours, $"WARN at {warnAt:O}, cliff {at:O}");
        Assert.True((at - stopAt).TotalMinutes >= stopMinutes, $"STOP at {stopAt:O}, cliff {at:O}");
        Assert.InRange(seg.GetProperty("stop").GetProperty("tokens").GetInt64(), 300_000, 340_000);
        Assert.True(seg.GetProperty("refused").GetInt32() > 0);
        // Ian's line does not move with the auto-compact window: on 900k STOP is at the same call
        var s900 = SeedSegment(fixture, "900000").GetProperty("stop");
        Assert.Equal(stopAt, s900.GetProperty("at").GetDateTimeOffset());
    }

    [Fact]
    public void TheSeedsHandoverEditAtStopIsAllowed()
    {
        // 006abbce edited its LEAD-HANDOVER after the line: at STOP that edit is still allowed
        var output = new StringWriter();
        Program.Run(new[] { "context", "--replay", Fixture("g5-seed-006abbce.jsonl") }, output, new StringWriter());
        var allowed = output.ToString().Split('\n').Where(l => l.Contains("\"allowed-at-stop\"")).ToList();
        Assert.Contains(allowed, l => l.Contains("LEAD-HANDOVER-2026-11-02.md"));
    }

    // ---- the hook verb, end to end ---------------------------------------------------------------------------------

    private static string HookInput(string file, string ev, string? tool = null, object? input = null, bool stopActive = false) =>
        JsonSerializer.Serialize(new
        {
            session_id = "zz-test-context", transcript_path = file, cwd = Path.GetTempPath(), hook_event_name = ev,
            tool_name = tool, tool_input = input, stop_hook_active = stopActive,
        });

    [Fact]
    public void TheHookSaysWarnDeniesAtStopAndLogsTheRefusal()
    {
        var file = Path.Combine(Path.GetTempPath(), $"g5-{Guid.NewGuid():N}.jsonl");
        var log = file + ".log";
        // --ceiling 100000 is the smallest window the docs allow (autoCompactWindow 100000-1000000): no settings file can lower
        // it further, so the test does not depend on the machine's settings
        var args = new[] { "context", "--module", "Operations", "--ceiling", "100000", "--hook" };
        try
        {
            using var db = BrainDb.OpenRolledBack();
            using var board = BrainDb.OpenRolledBack(KorStandardsConnection.Login.Board);
            // WARN: 75k of 100k
            File.WriteAllLines(file, new[] { Assistant(T0, "claude-opus-5-5", 1, 74_000, 1_000) });
            var output = new StringWriter();
            Program.Run(args, () => HookInput(file, "PreToolUse", "Bash", new { command = "dotnet build" }), output, new StringWriter(), log, db, board);
            using (var j = JsonDocument.Parse(output.ToString()))
            {
                var h = j.RootElement.GetProperty("hookSpecificOutput");
                Assert.False(h.TryGetProperty("permissionDecision", out _));
                Assert.StartsWith(ContextGuard.WarnMarker + "75,001", h.GetProperty("additionalContext").GetString());
            }
            output = new StringWriter();
            Program.Run(args, () => HookInput(file, "UserPromptSubmit"), output, new StringWriter(), log, db, board);
            Assert.Contains("\"UserPromptSubmit\"", output.ToString());

            // STOP: 86k of 100k
            File.WriteAllLines(file, new[] { Assistant(T0, "claude-opus-5-5", 1, 85_000, 1_000) });
            output = new StringWriter();
            Program.Run(args, () => HookInput(file, "PreToolUse", "Bash", new { command = "dotnet build" }), output, new StringWriter(), log, db, board);
            using (var j = JsonDocument.Parse(output.ToString()))
            {
                var h = j.RootElement.GetProperty("hookSpecificOutput");
                Assert.Equal("deny", h.GetProperty("permissionDecision").GetString());
                Assert.Contains("Write the handover now", h.GetProperty("permissionDecisionReason").GetString());
            }
            using (var c = board.Command("SELECT COUNT(*) FROM oversight.GuardRefusal WHERE Guard = N'kor-context' AND [Session] = N'zz-test-context' AND [Rule] LIKE N'G5 STOP 86,001/100,000%'"))
                Assert.Equal(1, Convert.ToInt32(c.ExecuteScalar()));

            // a handover step is allowed: nothing printed
            output = new StringWriter();
            Program.Run(args, () => HookInput(file, "PreToolUse", "Bash", new { command = "date" }), output, new StringWriter(), log, db, board);
            Assert.Equal("", output.ToString());
            // Stop: blocked once, not when already continuing from a Stop block
            output = new StringWriter();
            Program.Run(args, () => HookInput(file, "Stop"), output, new StringWriter(), log, db, board);
            using (var j = JsonDocument.Parse(output.ToString()))
                Assert.Equal("block", j.RootElement.GetProperty("decision").GetString());
            output = new StringWriter();
            Program.Run(args, () => HookInput(file, "Stop", stopActive: true), output, new StringWriter(), log, db, board);
            Assert.Equal("", output.ToString());
        }
        finally { File.Delete(file); if (File.Exists(log)) File.Delete(log); }
    }

    [Fact]
    public void TheCeilingOptionCanOnlyLowerTheCeiling()
    {
        var cs = CompactSettings.Off with { Cap = 2_000_000 };
        Assert.Equal(1_000_000, ContextGuard.CeilingOf("claude-opus-5-5", cs));
        Assert.Equal(100_000, ContextGuard.CeilingOf("claude-opus-5-5", CompactSettings.Off with { Cap = 100_000 }));
    }

    [Fact]
    public void NoTranscriptOrBadInputIsSilent()
    {
        var output = new StringWriter();
        Assert.Equal(0, Program.Run(new[] { "context", "--hook" }, () => "not json", output, new StringWriter(), Path.GetTempFileName()));
        Assert.Equal(0, Program.Run(new[] { "context", "--hook" }, () => HookInput("C:/nowhere/x.jsonl", "PreToolUse", "Bash", new { command = "x" }), output, new StringWriter(), Path.GetTempFileName()));
        Assert.Equal("", output.ToString());
    }
}
