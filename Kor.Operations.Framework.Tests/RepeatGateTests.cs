using System.Text.Json;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Recall;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// Board row 1998 (RESET-7, 10 Nov): <c>kor repeat --hook</c> refuses the start of a measurement or analysis whose topic already has a
/// recorded result, until the call says "inputs changed since #N: what". LIVE: the index is built from the live KorStandards (read), and
/// the GuardRefusal rows go to a rolled-back Board unit of work.
/// Covers: (A) 10 Nov 19:25:41Z, OverLORD framework-84 (transcript cde1e168) re-ran the week-spend breakdown with record #129845 (5 Nov)
/// in its own start context - the real description and command, refused, naming #129845, one kor-repeat row; the same call with
/// "inputs changed since #129845: ..." goes ahead and its reason is logged (kor-repeat-reason). (C) mistake 145's class after tonight:
/// "Run Codex audit: virtual drafting" is refused naming #132096, the prior-audit map. Looking for earlier work (search, read, record) is
/// never looked at; a Write over an existing file is not; a duration prefix and "break X down" are read.
/// RED BEFORE: the installed kor.exe (before this commit) has no repeat verb - the same hook JSON allows (shown in row 1998's evidence).
/// Does NOT cover: a repeat whose result lives only in files (mistake 145 AT ITS MOMENT: the prior audits were docs/codex files, no
/// finding named them; the content-search gate for docs/codex briefs is the OWED lane's prior_art_guard); a repeat phrased without a
/// work word ("rerun where_week.py"); a record outranked by a newer one (the assertion is "named among the three", not "first").
/// </summary>
[Collection(LiveDb.Name)]
public class RepeatGateTests
{
    private const string TrapADescription = "~2 min: break this week's tokens down by session, kind and model";

    // the command as framework-84 ran it, read off transcript cde1e168 at 2026-11-10T19:25:41.525Z
    private const string TrapACommand = """
        S="C:/Users/ilalonde/AppData/Local/Temp/claude/5102428d3890278f/cde1e168-f8b2-4abf-9302-f0bd7ca70aef/scratchpad"; cd "$S"; sed -i 's/^proj_raw = collections.Counter()$/proj_raw = collections.Counter(); model_raw = collections.Counter(); model_new = collections.Counter()/; s/^            rows.append((ctx, raw, new))$/            rows.append((ctx, raw, new)); model_raw[m.get("model","?")] += raw; model_new[m.get("model","?")] += new/' where_week.py; printf '%s\n' 'print("\nBY MODEL: raw M, share, new M")' 'for k, v in model_raw.most_common():' '    print(f"  {k:30} {v/1e6:8,.0f}M {100*v/tot:5.1f}%  new {model_new[k]/1e6:7,.1f}M")' >> where_week.py; grep -c model_raw where_week.py; python -I where_week.py 2>&1
        """;

    private static string Hook(BrainDb brain, BrainDb board, string session, string tool, object input)
    {
        var log = Path.Combine(Path.GetTempPath(), "kor-test-repeat-" + Guid.NewGuid().ToString("N") + ".log");
        var json = JsonSerializer.Serialize(new { hook_event_name = "PreToolUse", session_id = session, cwd = Path.GetTempPath(), tool_name = tool, tool_input = input });
        var o = new StringWriter();
        Assert.Equal(0, Program.Run(new[] { "repeat", "--hook" }, () => json, o, new StringWriter(), log, brain, board));
        Assert.False(File.Exists(log), File.Exists(log) ? File.ReadAllText(log) : "");   // it fails open: a crash would read as "allowed"
        return o.ToString();
    }

    private static List<(string Guard, string Rule, string Matched)> Logged(BrainDb board, string session)
    {
        using var c = board.Command("SELECT Guard, [Rule], Matched FROM oversight.GuardRefusal WHERE [Session] = @s ORDER BY Id", 10);
        c.Parameters.AddWithValue("@s", session);
        using var r = c.ExecuteReader();
        var rows = new List<(string, string, string)>();
        while (r.Read()) rows.Add((r.GetString(0), r.GetString(1), r.GetString(2)));
        return rows;
    }

    private static string Deny(string output)
    {
        Assert.False(string.IsNullOrWhiteSpace(output), "the hook allowed it");
        using var d = JsonDocument.Parse(output);
        var h = d.RootElement.GetProperty("hookSpecificOutput");
        Assert.Equal("deny", h.GetProperty("permissionDecision").GetString());
        return h.GetProperty("permissionDecisionReason").GetString()!;
    }

    [Fact]
    public void LiveTrapATheWeekSpendRerunIsRefusedNamingTheRecord()
    {
        using var brain = BrainDb.OpenRead(KorStandardsConnection.Login.Brain, true, 15);
        using var board = BrainDb.OpenRolledBack(KorStandardsConnection.Login.Board);
        var session = "zz-test-repeat-" + Guid.NewGuid().ToString("N");
        var reason = Deny(Hook(brain, board, session, "Bash", new { command = TrapACommand, description = TrapADescription }));
        Assert.Contains("record #129845", reason);
        Assert.Contains("inputs changed since #", reason);
        var rows = Logged(board, session);
        Assert.Single(rows);
        Assert.Equal(RepeatGate.GuardName, rows[0].Guard);
        Assert.StartsWith("row 1998: repeat of #", rows[0].Rule);
    }

    [Fact]
    public void LiveTrapAWithItsChangedInputGoesAheadAndTheReasonIsLogged()
    {
        using var brain = BrainDb.OpenRead(KorStandardsConnection.Login.Brain, true, 15);
        using var board = BrainDb.OpenRolledBack(KorStandardsConnection.Login.Board);
        var session = "zz-test-repeat-" + Guid.NewGuid().ToString("N");
        var output = Hook(brain, board, session, "Bash", new
        {
            command = TrapACommand,
            description = TrapADescription + " (inputs changed since #129845: the week of 9 Nov and a per-model split, not 2-5 Nov)",
        });
        Assert.True(string.IsNullOrWhiteSpace(output), output);
        var rows = Logged(board, session);
        Assert.Single(rows);
        Assert.Equal((RepeatGate.ReasonGuardName, "row 1998: inputs changed since #129845"), (rows[0].Guard, rows[0].Rule));
        Assert.Contains("the week of 9 Nov and a per-model split", rows[0].Matched);
    }

    [Fact]
    public void LiveMistake145ClassARerunOfAMappedAuditIsRefused()
    {
        using var brain = BrainDb.OpenRead(KorStandardsConnection.Login.Brain, true, 15);
        using var board = BrainDb.OpenRolledBack(KorStandardsConnection.Login.Board);
        var session = "zz-test-repeat-" + Guid.NewGuid().ToString("N");
        var reason = Deny(Hook(brain, board, session, "Bash", new
        {
            command = "bash lanes/run-codex-audit.sh VIRTUAL-DRAFTING",
            description = "Run Codex audit: virtual drafting",
        }));
        Assert.Contains("record #132096", reason);
    }

    [Fact]
    public void LiveLookingForEarlierWorkIsNeverRefused()
    {
        using var brain = BrainDb.OpenRead(KorStandardsConnection.Login.Brain, true, 15);
        using var board = BrainDb.OpenRolledBack(KorStandardsConnection.Login.Board);
        var session = "zz-test-repeat-" + Guid.NewGuid().ToString("N");
        foreach (var d in new[] { "Search for existing spend measurement tools", "Read the where_week.py spend breakdown script header",
                                  "Record today's measured cost for planning" })
            Assert.True(string.IsNullOrWhiteSpace(Hook(brain, board, session, "Bash", new { command = "grep -rl where_week docs", description = d })), d);
        Assert.Empty(Logged(board, session));
    }

    [Fact]
    public void TheIntentIsTheStartOfWorkOnly()
    {
        JsonElement J(object o) => JsonSerializer.SerializeToElement(o);
        Assert.Equal("break this week's tokens down by session, kind and model",
                     RepeatGate.Intent("Bash", J(new { command = "x", description = TrapADescription }), _ => false));
        Assert.Null(RepeatGate.Intent("Bash", J(new { command = "ls", description = "List files in the folder" }), _ => false));
        Assert.Null(RepeatGate.Intent("Bash", J(new { command = "x", description = "Search prior Codex audit briefs across repos" }), _ => false));
        // a Write: only a NEW file, read by its name and markdown title - never its first prose line
        var write = J(new { file_path = "C:/x/docs/WEEK-SPEND-BREAKDOWN.md", content = "# Where the week went\nYou are a lane ..." });
        Assert.Null(RepeatGate.Intent("Write", write, _ => true));
        Assert.Equal("WEEK SPEND BREAKDOWN\nWhere the week went", RepeatGate.Intent("Write", write, _ => false));
        Assert.Null(RepeatGate.Intent("Write", J(new { file_path = "C:/x/tb.txt", content = "You're right. This was measured on 5 Nov." }), _ => false));
        Assert.True(RepeatGate.HasWork("Measure session-start page sizes per module"));
        Assert.False(RepeatGate.HasWork("Rerun the publish script"));
    }
}
