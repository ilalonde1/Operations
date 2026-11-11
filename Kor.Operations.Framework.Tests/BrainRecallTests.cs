using System.Text;
using System.Text.Json;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Recall;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// Board row 1893 rolled out (BRAIN lane, 10 Nov): <see cref="BrainRecall"/>, the <c>kor recall --hook</c> moment-of-action hook and the
/// <see cref="PushBack"/> Stop check, tested against the REAL KorStandards (#1244), read-only.
///
/// THE TRAP TESTS ARE THE DONE-WHEN. Each replays one of 10 Nov's five misses as the moment it happened - the verbatim text the hook would
/// have seen, from the transcripts named on each - ranked against the brain AS OF that moment (records and board rows written later are
/// left out, BrainRecall.Build's asOfUtc), and asserts the record that covered it is in the top 3:
///   (a) OverLORD cde1e168 19:25:41Z Bash, re-running the week's token breakdown        -> #129845 (5 Nov: 97% of the week re-sent)
///   (b) OverLORD cde1e168 19:26:27Z reply proposing the context stop at 150k           -> #129933 (5 Nov: three reviews reject 150k)
///   (c) OverLORD cde1e168 19:33:46Z SendMessage briefing the 31218 lane                 -> board row 1647: not by words (TrapC); by the job fix list (row 2031, TrapC_TheJobFixList...)
///   (d) kor-drafter-14 fbcc2875 19:50:59Z Bash, "cover 3D from viewer ... a block"      -> board row 1647 then; #131897 now (recorded 10 Nov)
///   (e) OverLORD cde1e168 19:42:28Z reply, "the sheets you've seen"                     -> nothing to recall; the push-back's SEEN rule
/// AS OF IS APPROXIMATE (review row 1248): the as-of index leaves out rows CREATED after the moment, but it indexes today's text of older
/// rows, and leaves out rows retired, superseded or closed since; it is the brain of today minus what was added, not the brain as it was.
/// RED BEFORE: the installed kor (3743b04c) has no recall verb at all; the pilot's module recall on the same texts is stated in the
/// BRAIN lane's report. Same-class misses these do NOT catch: a record that shares no word with the moment (c is one), a moment whose words
/// change after today's records are written (the as-of pool cannot undo an edit to an older row), and a new record outranking the target
/// in the live pool (the hook's real case; only (d) is also asserted live).
/// </summary>
public class BrainRecallTests
{
    private static List<BrainRecall.Ranked> RankAsOf(string text, DateTime asOfUtc) =>
        BrainDb.ReadWithFallback(snapshot =>
        {
            using var b = BrainDb.OpenRead(KorStandardsConnection.Login.Brain, snapshot, 15);
            using var w = BrainDb.OpenRead(KorStandardsConnection.Login.Board, snapshot, 15);
            return BrainRecall.Rank(text, BrainRecall.Build(b, w, asOfUtc));
        }, BrainDb.IsSnapshotRefused, BrainDb.IsDeadlock, Thread.Sleep);

    private static string Top(List<BrainRecall.Ranked> r, int n) => string.Join(", ", r.Take(n).Select(x => $"{x.Doc.Ref} {x.Score:0.0}"));

    // ---- the five moments, verbatim ----

    private const string MomentA = """
        ~2 min: break this week's tokens down by session, kind and model
        S="C:/Users/ilalonde/AppData/Local/Temp/claude/5102428d3890278f/cde1e168-f8b2-4abf-9302-f0bd7ca70aef/scratchpad"; cd "$S"; sed -i 's/^proj_raw = collections.Counter()$/proj_raw = collections.Counter(); model_raw = collections.Counter(); model_new = collections.Counter()/; s/^            rows.append((ctx, raw, new))$/            rows.append((ctx, raw, new)); model_raw[m.get("model","?")] += raw; model_new[m.get("model","?")] += new/' where_week.py; printf '%s\n' 'print("\nBY MODEL: raw M, share, new M")' 'for k, v in model_raw.most_common():' '    print(f"  {k:30} {v/1e6:8,.0f}M {100*v/tot:5.1f}%  new {model_new[k]/1e6:7,.1f}M")' >> where_week.py; grep -c model_raw where_week.py; python -I where_week.py 2>&1
        """;

    private const string MomentB = """
        You're right. This was measured on 5 Nov (record #129845: 97% of the week's tokens were old context sent again), and nothing changed. Today's run says the same thing, so I'm not measuring again. The rerun added two numbers:

        - 97% of the 4,079M tokens this week were context sent again. Only 105M were new.
        - 59% were spent on turns where the session was already holding more than 300k of context. The worst lanes and overseers ran to 650k–790k. Every turn at 700k costs about seven times a turn at 100k.

        The cause isn't the app's size. Sessions run far too long before they restart. The only stop that exists is the context guard's (the G5 row) at 850k, a little under your auto-compact at 900k. That's the "lesson recorded, no gate" failure you called out this morning.

        The fix is to lower that stop to 150k. At 150k a session must write its handover and end, and the next one starts fresh from the record. That covers lanes, leads and OverLORD. The capped turns alone would have cost about 48% fewer tokens this week (2,137M instead of 4,079M). That figure doesn't include the cost of re-reading the start pages on each restart, so the real saving is lower. I haven't measured it.

        Lowering it changes the 900k decision you made on 3 Nov, so it's your call. Do you want the stop at 150k?
        """;

    private const string MomentC = """
        Build last two items before one rerun
        Pre-check green read at source (pre-1.log 12:28, 'REPLAY GENERATED: every stage to 4-finish'; 49ccac4b is the test-comment fix after it). DECISION: no rerun yet. A rerun without Omar's red dashed offset lines (L22, L23, roof) and A28 / assumptions on every sheet would fail the blind look on items we already know about, and then cost a second run. Build both now, re-run the same pre-check, and send me 'pre-check green' again with X of Y. That starts ONE drafting rerun and then the blind look. Revit stays up on 210 meanwhile. Halifax's 115 lines do not hold 31218 - I'm putting them on the KOR.Drafter board, not yours. Status lines go in status.txt; message me only for the result.
        """;

    private const string MomentD = """
        Append a status line
        cd "/c/VIsual Studio Projects/KOR.Drafter" && echo "10 Nov $(date +%H:%M) FIX-A2: items (c) dashed offset lines L22/L23/ROOF (3 lines, 9 of his dims, A32-A34) and (d) model_wide_sheets coded in drafted_set.py (uncommitted); replay of a patched config running (work/_one-tool/v3fix2/t1.log). Item 3 (cover 3D from viewer): pictures exist only after Revit + no bridge verb places an image - will report as a block." >> work/_one-tool/status.txt; tail -1 work/_one-tool/status.txt
        """;

    private const string MomentE = """
        Yes, once more, but only the drawing part. The model doesn't get rebuilt.

        **Why:** this morning's 08:19 run made the sheets you've seen, and its blind look found 20 problems that would mislead Omar. Everything fixed since then is in the code and checked offline. The 11:41 run rebuilt only the Revit model and drew no sheets. Those fixes only show up on Omar's sheets after a drafting run.
        """;

    private static readonly DateTime AtA = new(2026, 11, 10, 19, 25, 41, DateTimeKind.Utc);
    private static readonly DateTime AtB = new(2026, 11, 10, 19, 26, 27, DateTimeKind.Utc);
    private static readonly DateTime AtC = new(2026, 11, 10, 19, 33, 46, DateTimeKind.Utc);
    private static readonly DateTime AtD = new(2026, 11, 10, 19, 50, 59, DateTimeKind.Utc);

    [Fact]
    public void TrapA_ReRunningTheWeeksTokenBreakdownSurfacesTheFiveNovMeasurement()
    {
        var r = RankAsOf(MomentA, AtA);
        Assert.True(r.Take(3).Any(x => x.Doc.Ref == "#129845"), Top(r, 8));
    }

    [Fact]
    public void TrapB_ProposingTheContextStopAt150kSurfacesTheReviewsThatRejectedIt()
    {
        // the Stop moment: the push-back ranks the reply's proposing sentences (PushBack.ProposalText)
        var proposal = PushBack.ProposalText(MomentB);
        Assert.Contains("150k", proposal);
        var r = RankAsOf(proposal, AtB);
        Assert.True(r.Take(3).Any(x => x.Doc.Ref == "#129933"), Top(r, 8));
    }

    /// <summary>The miss, kept as a fact so it cannot be forgotten: the brief to the 31218 lane shares no word with row 1647 (Halifax and hotel
    /// covers, 3D, HTML viewer), and neither did any of 63 lane briefs naming 31218 (none names 1647; the job's own brief never says
    /// "cover"). Word ranking cannot bring it. What would: the job's fix list reading the open Ian asks on its board (record #131883).
    /// If this test goes red, recall now reaches it - move it to the asserted traps.</summary>
    [Fact]
    public void TrapC_TheBriefTo31218SharesNoWordWithRow1647_RecallCannotBringIt()
    {
        var r = RankAsOf(MomentC, AtC);
        Assert.False(r.Take(3).Any(x => x.Doc.Ref == "board row 1647"), Top(r, 8));
    }

    /// <summary>Trap c, the 5th trap, PASSED by job rather than by words (row 2031, <see cref="JobAsks"/>): at the moment of the 31218 brief,
    /// the brief names job 31218, the job's fix list holds a row quoting Ian that names 31218, and Ian's standing ask row 1647 on the same
    /// board. As of the moment (rows recorded later left out).</summary>
    [Fact]
    public void TrapC_TheJobFixListBringsRow1647ToThe31218Brief()
    {
        var ix = BrainDb.ReadWithFallback(snapshot =>
        {
            using var b = BrainDb.OpenRead(KorStandardsConnection.Login.Brain, snapshot, 15);
            using var w = BrainDb.OpenRead(KorStandardsConnection.Login.Board, snapshot, 15);
            return BrainRecall.Build(b, w, AtC);
        }, BrainDb.IsSnapshotRefused, BrainDb.IsDeadlock, Thread.Sleep);
        Assert.Equal(new[] { "31218" }, JobAsks.JobsIn(MomentC, ix));
        var f = JobAsks.For("31218", ix);
        var text = JobAsks.Text(f);
        Assert.Contains(f.Naming, d => d.Lead.Contains("31218") && d.Lead.Contains("Ian"));
        Assert.Contains(f.Standing, d => d.Ref == "board row 1647");
        Assert.Contains("board row 1647 [", text);
    }

    [Fact]
    public void TheHookBringsAJobsFixListOncePerSession()
    {
        var (first, session) = Hook("PreToolUse", new { tool_name = "SendMessage", tool_input = new { summary = "Build last two items before one rerun", message = MomentC } });
        Assert.Contains("JOB FIX LIST", first);
        Assert.Contains("job 31218", first);
        var (second, _) = Hook("PreToolUse", new { tool_name = "SendMessage", tool_input = new { summary = "Build last two items before one rerun", message = MomentC } }, session);
        Assert.DoesNotContain("JOB FIX LIST", second);
    }

    [Fact]
    public void ANumberNoOpenRowTreatsAsAJobBringsNoFixList()
    {
        var ix = new BrainRecall.Index { Docs = new() { new BrainRecall.Doc("board row 1", 1, true, "open", "Virtual Drafter", "", "Ian asked for 12345 once", "", null) },
                                         Len = new[] { 5 }, AvgLen = 5, Postings = new() { ["12345"] = new() { (0, 1) }, ["ian"] = new() { (0, 1) } } };
        Assert.Empty(JobAsks.JobsIn("a budget of 12345 tokens", ix));
        ix.Docs[0] = ix.Docs[0] with { Lead = "Ian asked for 12345-01 once" };
        Assert.Equal(new[] { "12345" }, JobAsks.JobsIn("a budget of 12345 tokens", ix));
        // another job's own row is not a standing ask; a product ask that cites a job as an example is
        Assert.True(JobAsks.TitleNamesAJob("[RERUN 01819 incremental] Ian 10 Nov 21:4x"));
        Assert.True(JobAsks.TitleNamesAJob("31170 (Griffin): do three Level 6 columns really turn"));
        Assert.False(JobAsks.TitleNamesAJob("[cover 3D pictures] Ian, 4 Nov 12:3x, on the hotel and Halifax covers ... 31180-01 call"));
    }

    [Fact]
    public void TrapD_CallingTheCoverPicturesBlockedSurfacesRow1647ThenAndTheViewerRenderRecordNow()
    {
        var then = RankAsOf(MomentD, AtD);
        Assert.True(then.Take(3).Any(x => x.Doc.Ref == "board row 1647"), Top(then, 8));
        var now = RankAsOf(MomentD, DateTime.UtcNow.AddMinutes(1));
        Assert.True(now.Take(3).Any(x => x.Doc.Ref == "#131897"), Top(now, 8));
    }

    [Fact]
    public void TrapE_TellingIanHeHasSeenTheSheetsIsPushedBack()
    {
        var v = PushBack.Decide(MomentE, new List<BrainRecall.Ranked>(), new HashSet<string>());
        Assert.True(v.Refuse, v.Reason);
        Assert.Contains("sheets you've seen", v.Reason);
        Assert.Contains("HAVEN'T SEEN", v.Reason);
        // once per session per claim: the same reply after the refusal goes through
        Assert.False(PushBack.Decide(MomentE, new List<BrainRecall.Ranked>(), new HashSet<string> { v.Key! }).Refuse);
    }

    // ---- the hook, end to end on the real database ----

    /// <summary>The hook's output. The hook fails open, so its error log is asserted empty, or a crash would read as "nothing to add" (review row 1246).</summary>
    private static (string Out, string Session) Hook(string ev, object payload, string? session = null)
    {
        var log = Path.Combine(Path.GetTempPath(), "kor-test-recall-" + Guid.NewGuid().ToString("N") + ".log");
        session ??= "test-brain-" + Guid.NewGuid().ToString("N");
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(payload))!.AsObject();
        node["hook_event_name"] = ev;
        node["session_id"] = session;
        var json = node.ToJsonString();
        var o = new StringWriter();
        // the database passed in: the hook builds its index from it, never touching the machine's index file or starting a rebuild (review row 1240)
        using var b = BrainDb.OpenRead(KorStandardsConnection.Login.Brain, true, 15);
        using var w = BrainDb.OpenRead(KorStandardsConnection.Login.Board, true, 15);
        Program.Run(new[] { "recall", "--hook", "--push-back" }, () => json, o, new StringWriter(), log, b, w);
        Assert.False(File.Exists(log), File.Exists(log) ? File.ReadAllText(log) : "");
        return (o.ToString(), session);
    }

    [Fact]
    public void TheHookAddsTheTopRecordsOnceAndUnderTwoKilobytes()
    {
        var (first, session) = Hook("PreToolUse", new { tool_name = "Bash", tool_input = new { command = "echo x", description = "cover 3D pictures from the HTML viewer snapshot for the cover sheet, no bridge verb" } });
        Assert.Contains("additionalContext", first);
        Assert.Contains(BrainRecall.HookHead, first);
        Assert.True(Encoding.UTF8.GetByteCount(first) < 2_048, $"{Encoding.UTF8.GetByteCount(first)} bytes: {first}");
        var refs = System.Text.RegularExpressions.Regex.Matches(first, @"(record #\d+|board row \d+) \[").Select(m => m.Groups[1].Value).ToList();
        Assert.InRange(refs.Count, 1, 3);
        // the same moment again in the same session: what was shown is not shown again
        var (second, _) = Hook("PreToolUse", new { tool_name = "Bash", tool_input = new { command = "echo x", description = "cover 3D pictures from the HTML viewer snapshot for the cover sheet, no bridge verb" } }, session);
        foreach (var r in refs) Assert.DoesNotContain(r + " [", second);
    }

    [Fact]
    public void TheHookAddsNothingForACommandWithNoTopic()
    {
        var (o, _) = Hook("PreToolUse", new { tool_name = "Bash", tool_input = new { command = "ls -la", description = "List files" } });
        Assert.Equal("", o.Trim());
    }

    [Fact]
    public void ASeenQuestionOrConditionIsNotAClaim()
    {
        var none = new List<BrainRecall.Ranked>();
        Assert.Null(PushBack.SeenClaim("Have you seen the sheets from the 08:19 run yet, or should I open them on your screen?"));
        Assert.Null(PushBack.SeenClaim("Once you've looked at the cover pictures, the package can go to Omar for his markup."));
        Assert.Null(PushBack.SeenClaim("The run made \"the sheets you've seen\" according to my earlier reply, which was wrong."));
        Assert.Null(PushBack.SeenClaim("These are the sheets you've seen in record #131821 from the blind look this morning."));
        Assert.NotNull(PushBack.SeenClaim("This morning's run made the sheets you've seen, and the blind look found 20 problems."));
        Assert.NotNull(PushBack.SeenClaim("You've seen the sheets from this morning, right?"));
        Assert.NotNull(PushBack.SeenClaim("The sheets you've seen look fine, should I ship them to Omar?"));
        Assert.False(PushBack.Decide("Have you seen the sheets from the 08:19 run yet? I can open them on your screen now.", none, new HashSet<string>()).Refuse);
    }

    [Fact]
    public void AQuestionThatOpensAsOneIsNotASeenClaim()
    {
        Assert.Null(PushBack.SeenClaim("Is this what you've seen on the cover sheet of the package?"));
        Assert.Null(PushBack.SeenClaim("Can you tell me what you saw on the cover sheet this morning?"));
    }

    /// <summary>The rebuild lock and the no-index fallback, on a temporary index path (KOR_RECALL_INDEX) and the real database
    /// (review row 1250: neither was tested).</summary>
    [Fact]
    public void ALiveLockStopsARebuildADeadOneIsTakenOverAndAHookWithNoIndexStillAnswers()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kor-recall-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var index = Path.Combine(dir, "index.txt");
        var before = Environment.GetEnvironmentVariable("KOR_RECALL_INDEX");
        Environment.SetEnvironmentVariable("KOR_RECALL_INDEX", index);
        try
        {
            var lockPath = index + ".lock";
            File.WriteAllText(lockPath, "1");   // a live rebuild's lock
            var log = Path.Combine(dir, "errors.log");
            var o = new StringWriter();
            Assert.Equal(0, Program.Run(new[] { "recall", "--rebuild" }, () => null, o, new StringWriter(), log));
            Assert.False(File.Exists(index), "a live lock must stop the rebuild");

            // a hook with no index while the lock is held: built in memory for the call, nothing written, no crash
            var hook = new StringWriter();
            var json = "{\"hook_event_name\":\"PreToolUse\",\"session_id\":\"test-brain-" + Guid.NewGuid().ToString("N") +
                       "\",\"tool_name\":\"Bash\",\"tool_input\":{\"command\":\"echo\",\"description\":\"cover 3D pictures from the HTML viewer for the cover sheet\"}}";
            Program.Run(new[] { "recall", "--hook" }, () => json, hook, new StringWriter(), log);
            Assert.False(File.Exists(log), File.Exists(log) ? File.ReadAllText(log) : "");
            Assert.Contains(BrainRecall.HookHead, hook.ToString());
            Assert.False(File.Exists(index));

            File.SetLastWriteTimeUtc(lockPath, DateTime.UtcNow.AddMinutes(-3));   // a dead rebuild's lock
            Assert.Equal(0, Program.Run(new[] { "recall", "--rebuild" }, () => null, new StringWriter(), new StringWriter(), log));
            Assert.True(File.Exists(index), "a dead lock must be taken over");
            Assert.False(File.Exists(lockPath), "the rebuild removes its lock");
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
            Assert.NotNull(BrainRecall.Load(index, new HashSet<string> { "cover" }));
        }
        finally
        {
            Environment.SetEnvironmentVariable("KOR_RECALL_INDEX", before);
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void TheHookIgnoresAToolItDoesNotRead()
    {
        var (o, _) = Hook("PreToolUse", new { tool_name = "Read", tool_input = new { file_path = "C:/x/cover 3D pictures viewer.md" } });
        Assert.Equal("", o.Trim());
    }

    [Fact]
    public void TheStopHookBlocksTheSeenClaimOnceAndNeverWhenAStopHookIsActive()
    {
        var (first, session) = Hook("Stop", new { last_assistant_message = MomentE, stop_hook_active = false });
        Assert.Contains("\"decision\":\"block\"", first);
        var (again, _) = Hook("Stop", new { last_assistant_message = MomentE, stop_hook_active = false }, session);
        Assert.Equal("", again.Trim());
        var (active, _) = Hook("Stop", new { last_assistant_message = MomentE, stop_hook_active = true });
        Assert.Equal("", active.Trim());
    }

    // ---- the pieces ----

    [Theory]
    [InlineData("measure", "measur")]
    [InlineData("measured", "measur")]
    [InlineData("measuring", "measur")]
    [InlineData("pictures", "pictur")]
    [InlineData("tokens", "token")]
    [InlineData("150k", "150k")]
    [InlineData("status", "status")]
    public void StemsMeetOnOneForm(string word, string stem) => Assert.Equal(stem, BrainRecall.Stem(word));

    [Fact]
    public void WordsDropPathsHashesStopWordsAndShortNumbers()
    {
        var w = BrainRecall.Words("cd \"C:/Users/ilalonde/AppData/Local/Temp/claude/5102428d3890278f/x\" && python the 12 tokens 150k 31218 /c/VIsual Studio Projects/KOR.Drafter");
        Assert.Equal(new[] { "token", "150k", "31218", "drafter" }, w.Where(x => x != "kor").ToArray());
        Assert.DoesNotContain("5102428d3890278f", w);
        Assert.DoesNotContain("ilalonde", w);
    }

    [Fact]
    public void AFirstSentenceSkipsAnEarlyDateAndCutsLongText()
    {
        Assert.Equal("Ian 5 Nov. The board row stays open until the check is red then green.",
                     BrainRecall.FirstSentence("Ian 5 Nov. The board row stays open until the check is red then green. Second.", 300));
        Assert.EndsWith("...", BrainRecall.FirstSentence(new string('w', 500), 100));
        Assert.Equal(100, BrainRecall.FirstSentence(new string('w', 500), 100).Length);
    }

    [Theory]
    [InlineData("Ian, 4 Nov 18:4x: 'Forget about the hotelrun'. Done.", "Forget about the hotelrun")]
    [InlineData("Ian 10 Nov 12:2x at 88% weekly: 'DO NOT go back to using Fable. I have 6 more days'", "DO NOT go back to using Fable. I have 6 more days")]
    [InlineData("Ian, 4 Nov 12:3x, on the covers: the 3D views \"look silly\" and more", "look silly")]
    public void IansWordsAreHisFirstQuote(string text, string words) => Assert.Equal(words, PushBack.IanWords(text));

    [Fact]
    public void ARecordWithoutIansWordsHasNone() => Assert.Null(PushBack.IanWords("OverLORD decided the order of the runs; Ian's board is unchanged."));

    [Fact]
    public void TheDecidedRuleRefusesAProposalNearIansDecisionUnlessItCitesIt()
    {
        var d = new BrainRecall.Doc("#1", 1, false, "decision", "Framework", "ian-auto-compact-on-at-900k", "lead",
                                    "Ian, 3 Nov: 'keep the context stop at 850k under the 900k auto-compact'", null);
        var near = new List<BrainRecall.Ranked> { new(d, 20, new[] { "context", "stop", "850k", "900k" }) };
        var reply = "The fix is to lower the context stop from 850k to 150k, under the 900k auto-compact. Do you want it?";
        var v = PushBack.Decide(reply, near, new HashSet<string>());
        Assert.True(v.Refuse, v.Reason);
        Assert.Contains("keep the context stop at 850k", v.Reason);
        Assert.False(PushBack.Decide(reply + " (against #1)", near, new HashSet<string>()).Refuse);
        Assert.False(PushBack.Decide("I lowered nothing; the context stop stays at 850k under the 900k auto-compact, as recorded.", near, new HashSet<string>()).Refuse);
    }

    [Fact]
    public void TheDecidedRuleIgnoresFindingsBoardRowsAndWeakMatches()
    {
        var reply = "The fix is to lower the context stop from 850k to 150k. Do you want it?";
        BrainRecall.Ranked R(string kind, bool board, double score, int hits) =>
            new(new BrainRecall.Doc("#2", 2, board, kind, "Framework", "n", "lead", "Ian, 3 Nov: 'keep the stop where it is please'", null),
                score, Enumerable.Range(0, hits).Select(i => "w" + i).ToList());
        Assert.False(PushBack.Decide(reply, new[] { R("finding", false, 50, 6) }, new HashSet<string>()).Refuse);
        Assert.False(PushBack.Decide(reply, new[] { R("open", true, 50, 6) }, new HashSet<string>()).Refuse);
        Assert.False(PushBack.Decide(reply, new[] { R("decision", false, PushBack.MinScore - 1, 6) }, new HashSet<string>()).Refuse);
        Assert.False(PushBack.Decide(reply, new[] { R("decision", false, 50, PushBack.MinTermsHit - 1) }, new HashSet<string>()).Refuse);
        Assert.True(PushBack.Decide(reply, new[] { R("decision", false, 50, 6) }, new HashSet<string>()).Refuse);
    }
}
