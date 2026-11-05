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
/// keeps WARN 300k and STOP 850k; a ceiling too small to hold the STOP line takes 85% of itself (a 200k one 140k and 170k).</item>
/// <item>The reading: the four usage fields summed; a compaction resets it to postTokens; "Prompt is too long" puts it
/// past the cliff; a main transcript's sidechain is skipped; the hook's own WARN is read back from an attachment and not
/// from a tool result.</item>
/// <item>WARN said once per line (300k, then every 100k), telling the session to save its state with kor state and carry
/// on; no tool refused below STOP. Every work tool refused at STOP. Every handover step allowed, each lookalike that does
/// work refused.</item>
/// <item>The Stop block: once, only until the handover is written, and each block logs one oversight.GuardRefusal row.</item>
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
            Assert.Equal(300_000, ContextGuard.WarnAt(ContextGuard.CeilingOf("claude-opus-5-5", cs)));   // Ian's lines hold on this PC's 900k ceiling
            Assert.Equal(850_000, ContextGuard.StopAt(ContextGuard.CeilingOf("claude-opus-5-5", cs)));   // 850k, not 85% of 900k = 765k
            Assert.Equal(ContextLevel.Warn, ContextGuard.LevelOf(new ContextReading(849_999, "claude-opus-5-5", T0, 1), cs));
            Assert.Equal(ContextLevel.Stop, ContextGuard.LevelOf(new ContextReading(850_000, "claude-opus-5-5", T0, 1), cs));
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

    [Theory]
    [InlineData(1_000_000, 300_000, 850_000)]
    [InlineData(900_000, 300_000, 850_000)]     // this PC: auto-compact at 900k
    [InlineData(883_247, 300_000, 850_000)]     // the smallest ceiling whose compaction, by the measured margin, fires at or above the line
    [InlineData(883_246, 300_000, 750_759)]     // compaction would fire under 850k: 85% of the ceiling
    [InlineData(500_000, 300_000, 425_000)]     // the warn line still fits under the fraction
    [InlineData(400_000, 280_000, 340_000)]
    [InlineData(200_000, 140_000, 170_000)]     // a 200k model
    [InlineData(100_000, 70_000, 85_000)]
    public void TheLinesComeFromTheCeiling(long ceiling, long warn, long stop)
    {
        Assert.Equal(warn, ContextGuard.WarnAt(ceiling));
        Assert.Equal(stop, ContextGuard.StopAt(ceiling));
    }

    [Theory]
    // the big ceiling: 300k, then each 100k
    [InlineData(300_500, 900_000, null, true)]
    [InlineData(399_999, 900_000, 300_500L, false)]
    [InlineData(400_000, 900_000, 300_500L, true)]
    [InlineData(420_000, 900_000, 405_000L, false)]   // the reading jumped past 400k: 500k is the next line, not 505k
    [InlineData(500_000, 900_000, 405_000L, true)]
    [InlineData(805_000, 900_000, 450_000L, true)]    // several lines crossed at once: one warning
    [InlineData(849_999, 900_000, 800_000L, false)]   // no line between 800k and the STOP at 850k
    [InlineData(300_000, 900_000, 260_000L, true)]    // a warning from the old 250k line, read back off a live transcript
    [InlineData(310_000, 900_000, 700_000L, true)]    // the reading fell (a compaction)
    [InlineData(400_000, 1_000_000, 300_000L, true)]  // the same lines on the model's 1M window
    // a ceiling too small for them: 2.5% of it from the fraction
    [InlineData(144_999, 200_000, 140_000L, false)]
    [InlineData(145_000, 200_000, 140_000L, true)]
    [InlineData(1, 1, 0L, true)]                      // the smallest --ceiling divides by nothing
    public void TheWarningIsSaidOncePerLine(long tokens, long ceiling, long? last, bool warn) =>
        Assert.Equal(warn, ContextGuard.ShouldWarn(tokens, ceiling, last));

    [Fact]
    public void AWarningTellsTheSessionToSaveItsStateAndCarryOn()
    {
        var text = ContextGuard.WarnText(new ContextReading(400_123, "claude-opus-5-5", T0, 1), Off, "Operations");
        Assert.StartsWith(ContextGuard.WarnMarker + "400,123 tokens", text);
        Assert.Contains("Save your state now", text);
        Assert.Contains("`kor state --module Operations --file <file>`", text);
        Assert.Contains("carry on working", text);
        Assert.Contains("Nothing is refused", text);
        Assert.Contains("850,000", text);
        Assert.Contains("--module <module> --file", ContextGuard.WarnText(new ContextReading(400_123, "claude-opus-5-5", T0, 1), Off, null));
    }

    [Theory]
    [InlineData("Bash", "git push origin main")]
    [InlineData("Write", "C:/r/src/x.cs")]
    [InlineData("Agent", "{\"prompt\":\"read everything\"}")]
    public void NoWorkToolIsRefusedBelowTheStopLine(string tool, string text)
    {
        foreach (var tokens in new long[] { 299_999, 300_000, 500_000, 765_000, 849_999 })
            Assert.False(ContextGuard.Decide(new ContextReading(tokens, "claude-opus-5-5", T0, 1), tool, text, null, false, "M", Off).Deny, $"{tool} at {tokens}");
        Assert.True(ContextGuard.Decide(new ContextReading(850_000, "claude-opus-5-5", T0, 1), tool, text, null, false, "M", Off).Deny);
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
    public void TheWarningIsReadBackOnlyFromItsOwnAttachment()
    {
        var warn = ContextGuard.WarnText(new ContextReading(312_345, "claude-opus-5-5", T0, 1), Off, "M");
        Assert.StartsWith(ContextGuard.WarnMarker + "312,345 tokens", warn);

        var lines = new List<string>
        {
            Assistant(T0, "claude-opus-5-5", 1, 311_000, 1_000),
            JsonSerializer.Serialize(new { type = "attachment", attachment = new { type = "hook_additional_context", content = new[] { warn } } }),
            Assistant(T0.AddMinutes(1), "claude-opus-5-5", 1, 315_000, 500),
        };
        Assert.Equal(312_345, ContextTranscript.Read(lines, lines.Count, false, null, Off).LastWarned);
        // a tool result quoting the marker (a grep of this very file) is not the hook's warning
        lines[1] = Result("x", warn);
        Assert.Null(ContextTranscript.Read(lines, lines.Count, false, null, Off).LastWarned);
    }

    private static ContextDecision AtStop(string tool, string text, bool sub = false) =>
        ContextGuard.Decide(new ContextReading(851_321, "claude-opus-5-5", T0, 1), tool, text, "C:/Users/x/AppData/Local/Temp/claude/s/scratchpad", sub, "Operations", Off);

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
    [InlineData("Bash", "git commit \\:\\^docs/HANDOVER.md -m wip")]                                   // none of these carries a '!' (review row 496)
    [InlineData("Bash", "X=:; git commit \"$X^docs/HANDOVER.md\" -m wip")]
    [InlineData("PowerShell", "git commit `:^docs/HANDOVER.md -m wip")]
    [InlineData("Bash", "git commit docs/HANDOVER*.md -m wip")]
    [InlineData("Bash", "git commit docs/HANDOVER?.md -m wip")]
    [InlineData("Bash", "git commit d[o]cs/HANDOVER.md -m wip")]
    [InlineData("Bash", "git commit {:^,:^}docs/HANDOVER.md -m wip")]
    [InlineData("Bash", "git commit {src/PLAN,docs/HANDOVER}.md -m wip")]
    [InlineData("Bash", "git add \\:\\^docs/HANDOVER.md")]
    [InlineData("Bash", "git add docs/HANDOVER*.md")]
    [InlineData("Bash", "git commit docs/HANDOVER.md -m a\\\" src/x.cs -m \\\"")]                    // a backslash before a quote: words git gets are not the words judged (review row 497)
    [InlineData("Bash", "git commit x\\\" src/x.cs \\\"docs/HANDOVER.md")]
    [InlineData("Bash", "git commit x\\' src/x.cs \\'docs/HANDOVER.md")]
    [InlineData("Bash", "git add x\\\" src/x.cs \\\"/HANDOVER.md")]
    [InlineData("Bash", "git -c a.b=\\\" commit src/x.cs -m \\\" -m status")]                          // a verb hidden behind an escape (review row 499)
    [InlineData("Bash", "git -c a.b=\\\" push origin main -o \\\" -o status")]
    [InlineData("Bash", "git commit -m a\\ docs/HANDOVER.md")]                                          // an escaped space: git gets no path
    [InlineData("PowerShell", "git commit -m a` docs/HANDOVER.md")]
    [InlineData("PowerShell", "git commit docs/HANDOVER.md -m a`' src/x.cs -m `'")]                       // PowerShell's escape is the backtick (review row 498)
    [InlineData("PowerShell", "git commit docs/HANDOVER.md -m a`\" src/x.cs -m `\"")]
    [InlineData("PowerShell", "git add x`' src/x.cs `'/HANDOVER.md")]
    [InlineData("Bash", "git add {:^,:^}docs/HANDOVER.md")]
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
    [InlineData("Bash", "git add \"C:\\VIsual Studio Projects\\Framework\\docs\\HANDOVER.md\" && git commit \"C:/VIsual Studio Projects/Framework/docs/HANDOVER.md\" -m done")]   // plain paths, drive and spaces
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
        var ok = ContextGuard.Decide(new ContextReading(299_999, "claude-opus-5-5", T0, 1), "Bash", "dotnet build", null, false, "M", Off);
        Assert.Equal(ContextLevel.Ok, ok.Level);
        Assert.Null(ok.Say);
        var warn = ContextGuard.Decide(new ContextReading(300_000, "claude-opus-5-5", T0, 1), "Bash", "dotnet build", null, false, "M", Off);
        Assert.Equal(ContextLevel.Warn, warn.Level);
        Assert.False(warn.Deny);
        Assert.Contains("kor state --module M", warn.Say);
        var last = ContextGuard.Decide(new ContextReading(849_999, "claude-opus-5-5", T0, 1), "Bash", "dotnet build", null, false, "M", Off);
        Assert.False(last.Deny);
        var stop = ContextGuard.Decide(new ContextReading(850_000, "claude-opus-5-5", T0, 1), "Bash", "dotnet build", null, false, "M", Off);
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
            Assistant(T0, "claude-opus-5-5", 1, 840_000, 1_000),                                                       // under STOP
            Assistant(T0.AddMinutes(1), "claude-opus-5-5", 1, 851_000, 500, ("e1", "Edit", new { file_path = "C:/r/docs/HANDOVER.md" })),
            Result("e1", "The file C:/r/docs/HANDOVER.md has been updated successfully."),
        };
        var r = ContextTranscript.Read(lines, lines.Count, false, null, Off);
        Assert.False(r.HandoverWritten);   // the edit alone is not the handover: kor state too
        var d = ContextGuard.DecideStop(r, false, false, "Operations", Off);
        Assert.True(d.Deny);
        Assert.Contains("Write the handover now", d.Say);
        Assert.False(ContextGuard.DecideStop(r, false, stopHookActive: true, "Operations", Off).Deny);   // once, never a loop
        Assert.False(ContextGuard.DecideStop(r, true, false, "Operations", Off).Deny);                   // a subagent's report is its handover

        lines.Add(Assistant(T0.AddMinutes(2), "claude-opus-5-5", 1, 853_000, 300, ("k1", "Bash", new { command = "C:/KOR-Operations/kor/kor.exe state --module Operations --file s.md" })));
        lines.Add(Result("k1", "brain.LaneState 412: Operations, 1820 characters, session x"));
        r = ContextTranscript.Read(lines, lines.Count, false, null, Off);
        Assert.True(r.HandoverWritten);
        Assert.False(ContextGuard.DecideStop(r, false, false, "Operations", Off).Deny);
        Assert.Contains("The handover is written", ContextGuard.Decide(r, "Bash", "dotnet build", null, false, "Operations", Off).Say);

        // a failed kor state is no handover
        lines[^1] = Result("k1", "kor: state needs --module", error: true);
        Assert.False(ContextTranscript.Read(lines, lines.Count, false, null, Off).HandoverWritten);
    }

    [Fact]
    public void AStateSavedAtAWarningIsNotTheHandover()
    {
        var lines = new List<string>
        {
            Assistant(T0, "claude-opus-5-5", 1, 400_000, 300, ("k0", "Bash", new { command = "kor state --module Operations --file s.md" })),
            Result("k0", "brain.LaneState 411: Operations, 900 characters, session x"),
            Assistant(T0.AddMinutes(1), "claude-opus-5-5", 1, 851_000, 500, ("e1", "Edit", new { file_path = "C:/r/docs/HANDOVER.md" })),
            Result("e1", "The file C:/r/docs/HANDOVER.md has been updated successfully."),
        };
        var r = ContextTranscript.Read(lines, lines.Count, false, null, Off);
        Assert.False(r.HandoverWritten);
        Assert.True(ContextGuard.DecideStop(r, false, false, "Operations", Off).Deny);
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
    [InlineData("g5-seed-d0313f99.jsonl", "2026-11-03T20:50:02Z", 3.0, 70.0)]   // Framework lead, 3 Nov 13:50 (measured at the 850k line: 70.1 min)
    [InlineData("g5-seed-eb9075b2.jsonl", "2026-11-01T20:37:36Z", 1.0, 25.0)]   // Operations, 1 Nov 13:37
    public void EachRealCliffIsWarnedWellBeforeAndStoppedBeforeItWithItsHandoverAllowed(string fixture, string cliff, double warnHours, double stopMinutes)
    {
        var at = DateTimeOffset.Parse(cliff, System.Globalization.CultureInfo.InvariantCulture);
        var seg = SeedSegment(fixture);
        var warnAt = seg.GetProperty("warn").GetProperty("at").GetDateTimeOffset();
        var stopAt = seg.GetProperty("stop").GetProperty("at").GetDateTimeOffset();
        Assert.True((at - warnAt).TotalHours >= warnHours, $"WARN at {warnAt:O}, cliff {at:O}");
        Assert.True((at - stopAt).TotalMinutes >= stopMinutes, $"STOP at {stopAt:O}, cliff {at:O}");
        Assert.InRange(seg.GetProperty("stop").GetProperty("tokens").GetInt64(), 850_000, 890_000);
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

    [Fact]
    public void ATailWithNoUsageWidensAndDoesNotReadAsAFreshSession()
    {
        // one result bigger than the first 2 MB read: its partial first line is dropped, so that tail holds no usage (review row 514)
        var file = Path.Combine(Path.GetTempPath(), $"g5-{Guid.NewGuid():N}.jsonl");
        try
        {
            File.WriteAllText(file, Assistant(T0, "claude-opus-5-5", 860_000, 0, 100) + "\n" + Result("big", new string('a', 3 << 20)) + "\n");
            var r = ContextTranscript.ReadFile(file, Off);
            Assert.Equal(860_100, r.Tokens);
            Assert.Equal(ContextLevel.Stop, ContextGuard.LevelOf(r, Off));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void ACliffBelowTheTriggerCountsAsReachingTheCliff()
    {
        // the summary's "STOP came first in X of Y": a cliff at a peak under the trigger is one of the Y (review row 514)
        var file = Path.Combine(Path.GetTempPath(), $"g5-{Guid.NewGuid():N}.jsonl");
        try
        {
            var tooLong = JsonSerializer.Serialize(new
            {
                type = "assistant", isSidechain = false, timestamp = T0.AddMinutes(1).ToString("O"),
                message = new { model = "<synthetic>", content = new object[] { new { type = "text", text = "Prompt is too long" } } },
            });
            File.WriteAllText(file, Assistant(T0, "claude-opus-5-5", 50_000, 0, 100) + "\n" + tooLong + "\n");
            var output = new StringWriter();
            Assert.Equal(0, Program.Run(new[] { "context", "--replay", file }, output, new StringWriter()));
            Assert.Contains("STOP came first in 0 of 1", output.ToString());
        }
        finally { File.Delete(file); }
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
        var args = new[] { "context", "--module", "Operations", "--ceiling", "100000", "--spend-root", "off", "--hook" };
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
            int StopRows()   // a Stop block logs a row of its own (board row 1786), beside the refused tool's one
            {
                using var c = board.Command("SELECT COUNT(*) FROM oversight.GuardRefusal WHERE Guard = N'kor-context' AND [Session] = N'zz-test-context' AND [Rule] = N'G5 STOP 86,001/100,000 Stop block' AND [Matched] LIKE N'Stop:%'");
                return Convert.ToInt32(c.ExecuteScalar());
            }
            Assert.Equal(1, StopRows());
            output = new StringWriter();
            Program.Run(args, () => HookInput(file, "Stop", stopActive: true), output, new StringWriter(), log, db, board);
            Assert.Equal("", output.ToString());
            Assert.Equal(1, StopRows());   // the second attempt is let through and logs nothing
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
