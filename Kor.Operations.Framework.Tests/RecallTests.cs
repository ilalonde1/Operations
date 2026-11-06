using System.Text.RegularExpressions;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Capture;
using Kor.Operations.Framework.Recall;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// Board row 1893: <c>kor recall</c> against the real KorStandards Full-Text index (migration 178), never a stand-in (#1244).
/// Compares: the printed head (module, terms found nowhere), each result's id, score, source and as-of date, the token budget, that
/// each delivered section is a verbatim piece of its row's body, that no other lane's row comes back, and the sectioning's lossless join.
/// Does NOT cover: whether ranking finds the right record for a real task (that is the trap test in Framework
/// docs/memory-design-review-2026-11-05/pilot/), nor a record written in the last seconds (the crawl is asynchronous, so a rolled-back
/// insert can never be found: these tests read committed rows). Same-class miss: a verbatim section of a row that is simply the wrong row.
/// </summary>
public class RecallTests
{
    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var code = Program.Run(args, o, e);
        return (code, o.ToString(), e.ToString());
    }

    // a real committed record: the lesson the brief cites for a grader
    private const string Lane = "Framework";
    private const string KnownTask = "a grader must fail a known bad run";
    private const int KnownId = 114267;

    [Fact]
    public void ARecallFindsAKnownRecordAndNamesItsIdScoreSourceAndAsOf()
    {
        var (code, text, err) = Run("recall", "--module", Lane, KnownTask);
        Assert.True(code == 0, err);
        var line = text.Split('\n').FirstOrDefault(l => l.StartsWith($"=== record #{KnownId} ", StringComparison.Ordinal));
        Assert.True(line is not null, text.Length > 3000 ? text[..3000] : text);
        Assert.Matches(@" - score \d+ - ", line!);
        Assert.Matches(@" - source .+ - ", line!);
        Assert.Matches(@" - as of \d{4}-\d{2}-\d{2}", line!);
    }

    [Fact]
    public void ARecallSaysWhichModuleItSearchedAndWhichTermsAreFoundNowhere()
    {
        var (code, text, err) = Run("recall", "--module", Lane, "grader zzqqxxnotaword");
        Assert.True(code == 0, err);
        Assert.Contains($"module '{Lane}'", text);
        var line = text.Split('\n').Single(l => l.StartsWith("FOUND NOWHERE IN THIS MODULE:", StringComparison.Ordinal));
        Assert.Contains("zzqqxxnotaword", line);
        Assert.DoesNotContain("grader", line);
    }

    [Fact]
    public void ARecallWithNoTermAnywhereSaysItFoundNothingAndDeliversNoRecord()
    {
        var (code, text, err) = Run("recall", "--module", Lane, "zzqqxxnotaword qqzzyynotaword");
        Assert.True(code == 0, err);
        Assert.Contains("NOTHING FOUND", text);
        Assert.DoesNotContain("=== record #", text);
    }

    [Fact]
    public void ARecallStatesStopWordsAndNeverCallsThemFoundNowhere()
    {
        var (_, text, _) = Run("recall", "--module", Lane, "the grader");
        var nowhere = text.Split('\n').Single(l => l.StartsWith("FOUND NOWHERE IN THIS MODULE:", StringComparison.Ordinal));
        Assert.DoesNotContain(" the", nowhere);
        Assert.Contains("IGNORED AS STOP WORDS: the", text);
    }

    [Fact]
    public void ARecallOnAModuleWithNoRecordsRefusesPlainly()
    {
        var (code, text, err) = Run("recall", "--module", "zz-no-such-lane", "grader");
        Assert.NotEqual(0, code);
        Assert.DoesNotContain("=== record #", text);
        Assert.Contains("zz-no-such-lane", err + text);
    }

    [Fact]
    public void ARecallNeedsAModuleAndATask()
    {
        Assert.NotEqual(0, Run("recall", "grader").Code);
        Assert.NotEqual(0, Run("recall", "--module", Lane).Code);
    }

    [Fact]
    public void ARecallRefusesATaskItCouldOnlySearchPartOf()
    {
        var (code, _, err) = Run("recall", "--module", Lane, new string('w', MemoryRecall.MostTaskChars + 1));
        Assert.NotEqual(0, code);
        Assert.Contains("will not search part of it", err);
    }

    [Fact]
    public void ARecallHoldsItsBudgetDeliversOnlyVerbatimPiecesOfItsOwnLaneAndNamesWhatItLeftOut()
    {
        const int budget = 1_500;
        var (code, text, err) = Run("recall", "--module", "KOR.Drafter", "--budget", budget.ToString(), "wall thickness read from the plan drawing");
        Assert.True(code == 0, err);
        Assert.True(MemoryRecall.Tokens(text.Length) <= budget, $"{text.Length} chars is over the budget");
        Assert.Contains("LEFT OUT BY THE BUDGET:", text);
        var pieces = Regex.Matches(text, @"=== record #(\d+) .*?\n(.*?)\n=== end #\1\n", RegexOptions.Singleline);
        Assert.NotEmpty(pieces);
        using var db = BrainDb.OpenRead(KorStandardsConnection.Login.Brain, snapshot: false);
        foreach (Match m in pieces)
        {
            using var c = db.Command("SELECT Lane, Body FROM brain.Memory WHERE Id = @i");
            c.Parameters.AddWithValue("@i", int.Parse(m.Groups[1].Value));
            using var r = c.ExecuteReader();
            Assert.True(r.Read());
            Assert.Equal("KOR.Drafter", r.GetString(0));
            Assert.Contains(m.Groups[2].Value, SecretGuard.Mask(r.GetString(1))!, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("short body")]
    [InlineData("# one\n\nparagraph one\n\n# two\nline\n\n\nparagraph three\n")]
    public void SectionsJoinBackToTheBodyByteForByte(string body) =>
        Assert.Equal(body, string.Concat(MemoryRecall.Sections(body, 10)));

    [Fact]
    public void ALongBodySplitsAtBoundariesNotInsideALine()
    {
        var para = string.Join("\n", Enumerable.Range(1, 40).Select(i => $"line {i} of the first paragraph"));
        var body = para + "\n\n" + para + "\n\n" + new string('x', 5000) + "\n\ntail";
        var parts = MemoryRecall.Sections(body, 300);
        Assert.Equal(body, string.Concat(parts));
        Assert.True(parts.Count > 2);
        Assert.Contains(parts, p => p.Contains(new string('x', 5000), StringComparison.Ordinal)); // one line over the size stays whole, never cut
        Assert.All(parts.Where(p => !p.Contains('x')), p => Assert.True(p.Length <= 300, p));
    }

    [Fact]
    public void ALongLineSplitsAtSentenceEnds()
    {
        var body = string.Concat(Enumerable.Range(1, 60).Select(i => $"Sentence {i} says one thing. "));
        var parts = MemoryRecall.Sections(body, 300);
        Assert.Equal(body, string.Concat(parts));
        Assert.All(parts, p => Assert.True(p.Length <= 300, p));
        Assert.All(parts, p => Assert.EndsWith(". ", p));
    }

    [Fact]
    public void TermsSplitOnEveryNonWordAndDropSingleCharacters() =>
        Assert.Equal(new[] { "dont", "use", "the", "pdf", "in2" }, MemoryRecall.Terms("Don't-use the_PDF: a in2 PDF!".Replace("Don't", "Dont")));

    [Fact]
    public void TheBestSectionIsTheOneHoldingTheMostTaskTerms()
    {
        var sections = new[] { "nothing here\n\n", "the wall is here\n\n", "the wall thickness is here" };
        Assert.Equal(new[] { 2, 1 }, MemoryRecall.BestSections(sections, new[] { "wall", "thickness" }, 2));
    }
}
