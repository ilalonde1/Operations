using System.Text.Json;
using Kor.Operations.Framework.Cli;
using Kor.Operations.Framework.Recall;
using Xunit;

namespace Kor.Operations.Framework.Tests;

/// <summary>
/// Failure class: shared context is not evidence that a record answers this measurement's question.
/// Checks all 24 supplied moments (verbatim input, UTC call time, read-only repeat dry run), plus constructed
/// quantity, scope, document and lift boundaries. Existing RepeatGateTests are deliberately untouched.
/// Covers verdict and the retained token result, not truth of an author's changed-input claim or completeness
/// of brain.Memory. As-of reads today's version of records created before the call, not historical versions.
/// Same-class miss: a repeated quantity described entirely with synonyms can still escape lexical recall.
/// Source hashes/lines live with the fixture; source commands are data and are NEVER executed by these tests.
/// </summary>
[Collection(LiveDb.Name)]
public class RepeatGateNarrowTests
{
    public static IEnumerable<object[]> Moments()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "repeat-narrow-moments.json")));
        foreach (var row in json.RootElement.EnumerateArray())
            yield return new object[] { row.GetProperty("id").GetInt32(), row.GetProperty("refuse").GetBoolean(), row.GetRawText() };
    }

    [Theory]
    [MemberData(nameof(Moments))]
    public void LiveEvidenceAtItsOwnMoment(int id, bool refuse, string fixture)
    {
        using var json = JsonDocument.Parse(fixture);
        var row = json.RootElement;
        var input = row.GetProperty("input");
        var tool = row.GetProperty("tool").GetString();
        var intent = RepeatGate.Intent(tool, input, _ => false);
        // An intent excluded by the hook cannot be refused. Still exercise the dry-run for every fixture,
        // which accepts an intent directly; Write uses exactly its filename/title, not its whole content.
        var description = tool == "Write"
            ? Path.GetFileNameWithoutExtension(input.GetProperty("file_path").GetString()!).Replace('-', ' ').Replace('_', ' ') + "\n" +
              input.GetProperty("content").GetString()!.Split('\n').First(l => l.StartsWith('#')).TrimStart('#').Trim()
            : input.GetProperty("description").GetString()!;
        var args = new List<string> { "repeat", description, "--as-of", row.GetProperty("timestamp").GetString()! };
        if (input.TryGetProperty("command", out var command)) args.AddRange(new[] { "--command", command.GetString()! });
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(0, Program.Run(args.ToArray(), output, error));
        Assert.True(error.ToString().Length == 0, error.ToString());
        Assert.True(output.ToString().StartsWith(refuse ? "REFUSE " : "ALLOW"), $"{id}: {output}");
        if (refuse)
        {
            Assert.NotNull(intent);
            Assert.Contains("record #129845", output.ToString());
        }
    }

    // Small corpus with real BM25 postings, rather than invented scores or hits. Unrelated documents
    // provide document frequency; record text and indexed body can intentionally differ, as in production.
    private static BrainRecall.Index Index(params (int Id, string Name, string Lead, string Body)[] records)
    {
        var docs = new List<BrainRecall.Doc>();
        var texts = new List<string>();
        foreach (var r in records)
        {
            docs.Add(new BrainRecall.Doc($"#{r.Id}", r.Id, false, "finding", "test", r.Name, r.Lead, "", null));
            texts.Add(r.Name + " " + r.Lead + " " + r.Body);
        }
        for (var i = 0; i < 200; i++)
        {
            docs.Add(new BrainRecall.Doc($"#{1000 + i}", 1000 + i, false, "finding", "test", "unrelated", "gardening roses sunshine", "", null));
            texts.Add("gardening roses sunshine");
        }
        var postings = new Dictionary<string, List<(int, int)>>();
        var lengths = new int[docs.Count];
        for (var i = 0; i < docs.Count; i++)
        {
            var words = BrainRecall.Words(texts[i]);
            lengths[i] = words.Count;
            foreach (var g in words.GroupBy(w => w))
            {
                if (!postings.TryGetValue(g.Key, out var list)) postings[g.Key] = list = new();
                list.Add((i, g.Count()));
            }
        }
        return new BrainRecall.Index { Docs = docs, Len = lengths, AvgLen = lengths.Average(), Postings = postings };
    }

    private static readonly (int, string, string, string) Thickness =
        (10, "31097-b1-l02-slab-thickness", "Measured slab thickness on job 31097 B1 L02: 8 inches throughout.", "");

    [Theory]
    [InlineData("Measure slab thickness again on job 31097 B1 L02")]
    [InlineData("For 31097 B1 L02, measure the thickness of the slab")]
    [InlineData("Survey the slab thickness on 31097 B1 L02")]
    public void SameQuantityRewordedMustRefuse(string intent)
    {
        Assert.True(RepeatGate.Decide(intent, intent, Index(Thickness)).Refuse);
    }

    [Theory]
    [InlineData("Analysis of slab thickness on job 31097 B1 L02")]
    [InlineData("Measurement of slab thickness on job 31097 B1 L02")]
    [InlineData("Slab thickness measurement on job 31097 B1 L02")]
    public void NounFormsOfTheSameMeasurementStillRefuse(string intent)
    {
        Assert.True(RepeatGate.Decide(intent, intent, Index(Thickness)).Refuse);
    }

    [Theory]
    [InlineData("Investigation of slab thickness on job 31097 B1 L02")]
    [InlineData("Characterisation of slab thickness on job 31097 B1 L02")]
    [InlineData("From the plans, read slab thickness off the sheets on job 31097 B1 L02")]
    public void OtherExistingWorkFormsRetainTheSameQuestion(string intent)
    {
        var input = JsonSerializer.SerializeToElement(new { description = intent, command = "true" });
        Assert.NotNull(RepeatGate.Intent("Bash", input, _ => false));
        Assert.True(RepeatGate.Decide(intent, intent, Index(Thickness)).Refuse);
    }

    [Theory]
    [InlineData("Measure slab deflection on job 31097 B1 L02")]
    [InlineData("Measure concrete strength for the slab on 31097 B1 L02")]
    [InlineData("Characterise overlapping pad groups on 31097 B1 L02")]
    public void SameJobDifferentQuestionMustPass(string intent)
    {
        Assert.False(RepeatGate.Decide(intent, intent, Index(Thickness)).Refuse);
    }

    [Theory]
    [InlineData("Measure slab thickness on job 41027 B1 L02")]
    [InlineData("Measure slab thickness on job 31097 B1 L03")]
    public void SameQuantityOnAnExplicitlyDifferentInputMustPass(string intent)
    {
        Assert.False(RepeatGate.Decide(intent, intent, Index(Thickness)).Refuse);
    }

    [Fact]
    public void BodyMentionsAreNotAnAnswerToFileSizes()
    {
        var intent = "Measure file sizes in the triage folder";
        var ix = Index((11, "yardstick-source-folder", "The engineer's yardstick files are exports from another machine.",
            "file sizes folder triage; this is a list of requests, not measured sizes"));
        Assert.False(RepeatGate.Decide(intent, intent, ix).Refuse);
    }

    [Theory]
    [InlineData("inputs changed since #11: the windows were replaced")]
    [InlineData("inputs changed since #999999: new drawings")]
    [InlineData("inputs changed since #10:")]
    public void UnrelatedMissingOrEmptyLiftMustNotPermitRepeat(string lift)
    {
        var intent = "Measure slab thickness on job 31097 B1 L02";
        var ix = Index(Thickness, (11, "31097-b1-l02-window-count", "Window count on job 31097 B1 L02 is 18.", ""));
        Assert.True(RepeatGate.Decide(intent, intent + "\n" + lift, ix).Refuse);
    }

    [Fact]
    public void RelevantLiftSurvivesRankingBelowTheThreeDisplayedResults()
    {
        var intent = "Measure slab thickness on job 31097 B1 L02";
        var ix = Index(Thickness,
            (20, "slab-thickness-31097-b1-l02", "slab thickness 31097 B1 L02", ""),
            (21, "slab-thickness-31097-b1-l02", "slab thickness 31097 B1 L02", ""),
            (22, "slab-thickness-31097-b1-l02", "slab thickness 31097 B1 L02", ""));
        Assert.DoesNotContain(BrainRecall.Rank(intent, ix).Take(3), r => r.Doc.Id == 10);
        var v = RepeatGate.Decide(intent, intent + "\ninputs changed since #10: revised slab schedule issued today", ix);
        Assert.False(v.Refuse);
        Assert.Equal(10, v.AllowedFor);
    }

    [Fact]
    public void NewDocumentDoesNotInheritAnUnrelatedResultForItsJob()
    {
        var input = JsonSerializer.SerializeToElement(new { file_path = "C:/new/bundle/README.md",
            content = "# 31097 B1 L02 SAFE analysis model\nNew bundle; documenting the acceptance inputs." });
        var intent = RepeatGate.Intent("Write", input, _ => false);
        if (intent is not null)
            Assert.False(RepeatGate.Decide(intent, BrainRecall.MomentText("Write", input)!, Index(Thickness),
                RepeatGate.RankText("Write", intent, null)).Refuse);
    }

    [Fact]
    public void NewDocumentThatActuallyRepeatsTheQuantityStillRefuses()
    {
        var input = JsonSerializer.SerializeToElement(new { file_path = "C:/new/SLAB-THICKNESS-ANALYSIS.md",
            content = "# Slab thickness on 31097 B1 L02\nThe same schedule and inputs." });
        var intent = RepeatGate.Intent("Write", input, _ => false);
        Assert.NotNull(intent);
        Assert.True(RepeatGate.Decide(intent, BrainRecall.MomentText("Write", input)!, Index(Thickness),
            RepeatGate.RankText("Write", intent, null)).Refuse);
    }

    [Fact]
    public void AnUnrelatedLiftInTheDescriptionCannotReplaceTheQuestion()
    {
        var intent = "Measure slab thickness on job 31097 B1 L02; inputs changed since #11: the windows were replaced";
        var ix = Index(Thickness, (11, "31097-b1-l02-window-count", "Window count on job 31097 B1 L02 is 18.", ""));
        Assert.True(RepeatGate.Decide(intent, intent, ix).Refuse);
    }

    [Fact]
    public void RelevantLiftSurvivesAnUnrelatedRerank()
    {
        var intent = "Measure slab thickness on job 31097 B1 L02";
        var ix = Index((Thickness.Item1, Thickness.Item2, Thickness.Item3,
                string.Join(" ", Enumerable.Repeat("gardening roses sunshine", 20))),
            (20, "31097-b1-l02", "31097 B1 L02 window count 18", "31097 B1 L02"),
            (21, "31097-b1-l02", "31097 B1 L02 beam length 20", "31097 B1 L02"),
            (22, "31097-b1-l02", "31097 B1 L02 column count 10", "31097 B1 L02"));
        // Make the command rank the unrelated findings above the referenced measurement, as the real retries did.
        var rank = intent + "\nwindow count beam length column count 31097 B1 L02";
        Assert.DoesNotContain(BrainRecall.Rank(rank, ix).Take(3), r => r.Doc.Id == 10);
        var v = RepeatGate.Decide(intent, intent + "\ninputs changed since #10: revised slab schedule", ix, rank);
        Assert.False(v.Refuse);
        Assert.Equal(10, v.AllowedFor);
    }

    [Fact]
    public void AuditOfTheSameModelBundleWithAnAuditOutcomeStillRefuses()
    {
        var intent = "Audit the 41027 model bundle";
        var ix = Index((10, "41027-model-bundle-audit", "Model bundle audit for 41027: 14 checks passed.", ""));
        Assert.True(RepeatGate.Decide(intent, intent, ix).Refuse);
    }

    [Fact]
    public void InflectedAuditStillRefusesTheSameAuditOutcome()
    {
        var intent = "Auditing the 41027 model bundle";
        var ix = Index((10, "41027-model-bundle-audit", "Model bundle audit for 41027: 14 checks passed.", ""));
        Assert.True(RepeatGate.Decide(intent, intent, ix).Refuse);
    }

    [Fact]
    public void BodyOnlyQuestionAboveTheScoreFloorStillIsNotAnAnswer()
    {
        var intent = "Measure file sizes in the triage folder";
        var rank = intent + "\n" + intent;
        var ix = Index((11, "file-folder", "File folder location.", "file sizes triage folder"));
        Assert.True(BrainRecall.Rank(rank, ix).Single(r => r.Doc.Id == 11).Score >= RepeatGate.Floor);
        Assert.False(RepeatGate.Decide(intent, intent, ix, rank).Refuse);
    }

    [Fact]
    public void LiveDryRunReadsTheLiftAfterTheRankingCutoff()
    {
        var intent = "break this week's tokens down by session, kind and model (raw and new per transcript)";
        var command = new string('x', RepeatGate.MostRankChars + 20) + "\ninputs changed since #129845: revised transcript period";
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(0, Program.Run(new[] { "repeat", intent, "--command", command, "--as-of", "2026-11-11T06:36:03.791Z" }, output, error));
        Assert.StartsWith("ALLOW: inputs changed since #129845:", output.ToString());
    }
}
