using System.Text.Json;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Context;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// Board row 1936: a main session holds findings, not raw material. The sizes are ReadGuard's; the hook is driven with real
/// hook JSON and a transcript on disk; the refusal row is on live KorStandards in a rolled-back transaction.
/// Covers: what is refused, what passes, a subagent, the row, the fail-open; the output warning, once per crossing; the replay.
/// Does NOT cover: Claude Code applying the deny (the live proof pipes JSON through the built kor), or the look guard's credit
/// for a helper's look, which is tested in tools/claude_look_guard_test.py beside the guard.
/// </summary>
[Collection(LiveDb.Name)]
public sealed class ReadGuardTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"zz-read-{Guid.NewGuid():N}");
    private readonly string _transcript;

    public ReadGuardTests()
    {
        Directory.CreateDirectory(_dir);
        _transcript = Path.Combine(_dir, "main.jsonl");
        File.WriteAllLines(_transcript, new[] { Assistant("m0", 60_000) });
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static string Assistant(string id, long tokens, string? toolId = null, string tool = "Bash") => JsonSerializer.Serialize(new
    {
        type = "assistant", isSidechain = false, timestamp = "2026-11-06T20:00:00Z",
        message = new
        {
            id, model = "claude-opus-5-5", usage = new { input_tokens = tokens, output_tokens = 10 },
            content = toolId is null ? new object[] { new { type = "text", text = "x" } } : new object[] { new { type = "tool_use", id = toolId, name = tool, input = new { command = "x" } } },
        },
    });

    private static string Result(string toolId, int chars) => JsonSerializer.Serialize(new
    {
        type = "user", isSidechain = false, message = new { role = "user", content = new object[] { new { type = "tool_result", tool_use_id = toolId, content = new string('x', chars) } } },
    });

    private string File_(string name, long bytes)
    {
        var p = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, new byte[bytes]);
        return p.Replace('\\', '/');
    }

    [Theory]
    [InlineData("a.png", ReadGuard.MaxImageBytes + 1, null, null, null, true)]
    [InlineData("a.JPG", ReadGuard.MaxImageBytes + 1, null, null, null, true)]
    [InlineData("a.png", ReadGuard.MaxImageBytes, null, null, null, false)]                     // a small crop
    [InlineData("a.png", ReadGuard.MaxImageBytes + 1, 1, 10, null, true)]                       // a range means nothing to an image
    [InlineData("a.cs", ReadGuard.MaxFileBytes + 1, null, null, null, true)]
    [InlineData("a.cs", ReadGuard.MaxFileBytes, null, null, null, false)]
    [InlineData("a.cs", ReadGuard.MaxFileBytes * 10, 100, ReadGuard.MaxRangeLines, null, false)] // a line range under the size
    [InlineData("a.cs", ReadGuard.MaxFileBytes * 10, 100, ReadGuard.MaxRangeLines + 1, null, true)]
    [InlineData("a.cs", ReadGuard.MaxFileBytes * 10, 100, null, null, true)]                    // an offset alone reads on to the tool's own limit
    [InlineData("a.pdf", ReadGuard.MaxFileBytes * 10, null, null, null, true)]
    [InlineData("a.pdf", ReadGuard.MaxFileBytes * 10, null, null, "3", false)]
    [InlineData("a.pdf", ReadGuard.MaxFileBytes * 10, null, null, "3-4", false)]
    [InlineData("a.pdf", ReadGuard.MaxFileBytes * 10, null, null, "3-5", true)]
    [InlineData("a.pdf", ReadGuard.MaxFileBytes * 10, null, null, "100-1,1-100", true)]         // review row 616: a reversed range is no count
    [InlineData("docs/HANDOVER.md", ReadGuard.MaxFileBytes * 10, null, null, null, false)]      // every handover step
    [InlineData("docs/LEAD-HANDOVER-2026.md", ReadGuard.MaxFileBytes * 10, null, null, null, false)]
    [InlineData("RESUME-HERE.md", ReadGuard.MaxFileBytes * 10, null, null, null, false)]
    [InlineData("CLAUDE.md", ReadGuard.MaxFileBytes * 10, null, null, null, false)]
    [InlineData("docs/HOW-WE-BUILD.md", ReadGuard.MaxFileBytes * 10, null, null, null, false)]
    public void TheRule(string path, long size, int? offset, int? limit, string? pages, bool refused) =>
        Assert.Equal(refused, ReadGuard.Refuses(new ReadCall(path, offset, limit, pages), size, null) is not null);

    [Fact]
    public void AStateFileInTheScratchpadPassesAndAnUnreadableSizePasses()
    {
        Assert.Null(ReadGuard.Refuses(new ReadCall("C:/s/STATE-NOW.md", null, null, null), ReadGuard.MaxFileBytes * 10, "C:/s"));
        Assert.NotNull(ReadGuard.Refuses(new ReadCall("C:/s/big.txt", null, null, null), ReadGuard.MaxFileBytes * 10, "C:/s"));
        Assert.Null(ReadGuard.Refuses(new ReadCall("C:/nowhere/a.png", null, null, null), null, null));
        Assert.Null(ReadGuard.SizeOf(new ReadCall("C:/nowhere/a.png", null, null, null), _dir));
        Assert.Null(ReadGuard.SizeOf(new ReadCall("C:/bad/\0.png", null, null, null), _dir));
        Assert.Equal(7, ReadGuard.SizeOf(new ReadCall("rel.bin", null, null, null), Path.GetDirectoryName(File_("rel.bin", 7))));
    }

    private string HookJson(string ev, string? tool, object? input, string session, string? agent = null) => JsonSerializer.Serialize(new
    {
        session_id = session, transcript_path = _transcript, cwd = _dir, hook_event_name = ev, tool_name = tool, tool_input = input, agent_id = agent,
    });

    private static string Run(string stdin, BrainDb? board = null)
    {
        var o = new StringWriter();
        var log = Path.GetTempFileName();
        Assert.Equal(0, Program.Run(new[] { "context", "--module", "Operations", "--spend-root", "off", "--hook" }, () => stdin, o, new StringWriter(), log, null, board));
        return o.ToString();
    }

    private static int Rows(BrainDb board, string session)
    {
        using var c = board.Command("SELECT COUNT(*) FROM oversight.GuardRefusal WHERE Guard = @g AND [Session] = @s");
        c.Parameters.AddWithValue("@g", ReadGuard.GuardName);
        c.Parameters.AddWithValue("@s", session);
        return Convert.ToInt32(c.ExecuteScalar());
    }

    [Fact]
    public void AMainSessionsBigReadIsRefusedNamesTheRouteAndLogsOneRow()
    {
        using var board = BrainDb.OpenRolledBack(KorStandardsConnection.Login.Board);
        var session = $"zz-read-{Guid.NewGuid():N}";
        var png = File_("sheet.png", ReadGuard.MaxImageBytes + 1);
        var denied = Run(HookJson("PreToolUse", "Read", new { file_path = png }, session), board);
        using (var j = JsonDocument.Parse(denied))
        {
            var h = j.RootElement.GetProperty("hookSpecificOutput");
            Assert.Equal("deny", h.GetProperty("permissionDecision").GetString());
            var why = h.GetProperty("permissionDecisionReason").GetString()!;
            Assert.Contains("helper", why);
            Assert.Contains("sheet.png", why);
            Assert.Contains("look guard", why);
        }
        Assert.Equal(1, Rows(board, session));

        var big = File_("big.txt", ReadGuard.MaxFileBytes + 1);
        Assert.Contains("deny", Run(HookJson("PreToolUse", "Read", new { file_path = big }, session), board));
        Assert.Equal(2, Rows(board, session));
        Assert.DoesNotContain("deny", Run(HookJson("PreToolUse", "Read", new { file_path = big, offset = 1, limit = 50 }, session), board));
        Assert.Equal(2, Rows(board, session));
    }

    [Fact]
    public void ASubagentsReadPasses()
    {
        using var board = BrainDb.OpenRolledBack(KorStandardsConnection.Login.Board);
        var session = $"zz-read-{Guid.NewGuid():N}";
        var sub = Path.Combine(_dir, session, "subagents");
        Directory.CreateDirectory(sub);
        File.WriteAllLines(Path.Combine(sub, "agent-abc.jsonl"), new[] { Assistant("s0", 40_000) });
        var png = File_("sheet.png", ReadGuard.MaxImageBytes + 1);
        Assert.DoesNotContain("deny", Run(HookJson("PreToolUse", "Read", new { file_path = png }, session, agent: "abc"), board));
        Assert.Equal(0, Rows(board, session));
    }

    [Fact]
    public void AGuardFaultAllows()
    {
        Assert.DoesNotContain("deny", Run(HookJson("PreToolUse", "Read", new { file_path = "C:/nowhere/a.png" }, "zz-read-fault")));
        Assert.DoesNotContain("deny", Run(HookJson("PreToolUse", "Read", new { limit = 3 }, "zz-read-fault")));
        Assert.DoesNotContain("deny", Run(HookJson("PreToolUse", "Read", "not an object", "zz-read-fault")));
    }

    [Fact]
    public void ABigCommandOutputWarnsOncePerCrossing()
    {
        File.WriteAllLines(_transcript, new[] { Assistant("m1", 60_000, "t1"), Result("t1", ReadGuard.OutputWarnChars + 1) });
        var said = Run(HookJson("PreToolUse", "Bash", new { command = "date" }, "zz-read-warn"));
        Assert.Contains(ReadGuard.OutputMarker, said);
        Assert.DoesNotContain("\"deny\"", said);

        // the warning comes back as an attachment: nothing more until the next big output
        var attachment = JsonSerializer.Serialize(new { type = "attachment", attachment = new { type = "hook_additional_context", content = ReadGuard.OutputMarker + " 1 characters" } });
        File.AppendAllLines(_transcript, new[] { attachment });
        Assert.DoesNotContain(ReadGuard.OutputMarker, Run(HookJson("PreToolUse", "Bash", new { command = "date" }, "zz-read-warn")));

        File.AppendAllLines(_transcript, new[] { Assistant("m2", 62_000, "t2"), Result("t2", ReadGuard.OutputWarnChars + 1) });
        Assert.Contains(ReadGuard.OutputMarker, Run(HookJson("UserPromptSubmit", null, null, "zz-read-warn")));
    }

    [Fact]
    public void WhatTheOutputWarningLeavesAlone()
    {
        // under the size; a Read's result (the rule above holds Reads); an output from turns ago, seen only because the guard is new
        File.WriteAllLines(_transcript, new[] { Assistant("m1", 60_000, "t1"), Result("t1", ReadGuard.OutputWarnChars) });
        Assert.DoesNotContain(ReadGuard.OutputMarker, Run(HookJson("PreToolUse", "Bash", new { command = "date" }, "zz-read-warn")));
        File.WriteAllLines(_transcript, new[] { Assistant("m1", 60_000, "t1", "Read"), Result("t1", ReadGuard.OutputWarnChars + 1) });
        Assert.DoesNotContain(ReadGuard.OutputMarker, Run(HookJson("PreToolUse", "Bash", new { command = "date" }, "zz-read-warn")));
        File.WriteAllLines(_transcript, new[]
        {
            Assistant("m1", 60_000, "t1"), Result("t1", ReadGuard.OutputWarnChars + 1),
            Assistant("m2", 60_000), Assistant("m3", 60_000), Assistant("m4", 60_000),
        });
        Assert.DoesNotContain(ReadGuard.OutputMarker, Run(HookJson("PreToolUse", "Bash", new { command = "date" }, "zz-read-warn")));

        // a helper's own output is its own to read
        var session = "zz-read-sub";
        var sub = Path.Combine(_dir, session, "subagents");
        Directory.CreateDirectory(sub);
        File.WriteAllLines(Path.Combine(sub, "agent-abc.jsonl"), new[] { Assistant("m1", 60_000, "t1"), Result("t1", ReadGuard.OutputWarnChars + 1) });
        Assert.DoesNotContain(ReadGuard.OutputMarker, Run(HookJson("PreToolUse", "Bash", new { command = "date" }, session, agent: "abc")));
    }

    [Fact]
    public void TheReplayJudgesEveryReadByTheRule()
    {
        var line = (string id, string path, object input) => JsonSerializer.Serialize(new
        {
            type = "assistant", isSidechain = false, timestamp = "2026-11-06T20:00:00Z",
            message = new { id, model = "claude-opus-5-5", usage = new { input_tokens = 60_000, output_tokens = 10 }, content = new object[] { new { type = "tool_use", id = "u" + id, name = "Read", input } } },
        });
        File.WriteAllLines(_transcript, new[]
        {
            line("a", "x", new { file_path = "C:/x/big.txt" }), Result("ua", (int)ReadGuard.MaxFileBytes + 1),
            line("b", "x", new { file_path = "C:/x/small.txt" }), Result("ub", 10),
            line("c", "x", new { file_path = "C:/x/big.txt", offset = 1, limit = 50 }), Result("uc", (int)ReadGuard.MaxFileBytes + 1),
        });
        var o = new StringWriter();
        Assert.Equal(0, Program.Run(new[] { "context", "--read-replay", _transcript }, o, new StringWriter()));
        var rows = o.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => l.StartsWith('{')).Select(l => JsonDocument.Parse(l).RootElement).ToList();
        var reads = rows.Where(r => r.GetProperty("kind").GetString() == "read").ToList();
        Assert.Equal(3, reads.Count);
        Assert.Equal("file", reads[0].GetProperty("refused").GetString());
        Assert.Equal(JsonValueKind.Null, reads[1].GetProperty("refused").ValueKind);
        Assert.Equal(JsonValueKind.Null, reads[2].GetProperty("refused").ValueKind);
        Assert.Contains("1 of 3 Reads refused", o.ToString());
    }
}
