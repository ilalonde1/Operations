using System;
using Kor.Opportunities.Data.Crm;
using Xunit;

namespace Kor.Opportunities.Data.Tests;

// The pure parts of Claude's RFP review (migration 377): what a session's report must hold, what the prompt tells the session,
// the pursuit's status line in each state, and which pursuits open a review by themselves. Compares text and SQL only: it does
// not open Claude Code, store a review or read a file; a change in what the CLI verbs print would not be caught here.
public sealed class PursuitRfpReviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 21, 0, 0, TimeSpan.Zero);

    private const string Report = """
        {
          "outcome": "answered", "recommendation": "decide",
          "summary": "An MSA for structural and civil engineering.", "reasons": "Decide: civil is a minimum qualification.\nPricing is 40 points.",
          "questions": [ { "question": "Can a structural firm propose with a civil sub-consultant?", "why": "KOR is structural only.", "ref": "Appendix E A.3" } ],
          "questionsDeadline": "Fri 23 Oct 2026, 2:00 pm MST", "questionsTo": "buyer@example.ca",
          "actions": [ { "action": "Decide go or no-go", "owner": "Ian", "due": "2026-10-22", "why": "Questions close 23 Oct." },
                       { "action": "Send the questions", "owner": "owner", "due": "2026-10-23", "why": "Last day for questions." } ],
          "checklist": [ { "requirement": "Proposals should not exceed 20 pages", "sourceRef": "s. 3.1.2", "checkKind": "PageLimit", "checkParameter": "20" } ]
        }
        """;

    [Fact]
    public void A_complete_report_is_kept_whole_and_a_bad_one_is_refused_with_every_reason()
    {
        var (report, errors) = PursuitRfpReview.ParseReport(Report);
        Assert.Empty(errors);
        Assert.Equal("decide", report!.Recommendation);
        Assert.Single(report.Questions);
        Assert.Equal("Appendix E A.3", report.Questions[0].Ref);
        Assert.Single(report.Checklist);

        var (bad, why) = PursuitRfpReview.ParseReport("""
            { "outcome": "done", "recommendation": "maybe", "questions": [ { "question": "Q?" } ],
              "checklist": [ { "requirement": "x", "sourceRef": "s", "checkKind": "PageLimit", "checkParameter": "twenty" } ] }
            """);
        Assert.Null(bad);
        Assert.Contains(why, e => e.StartsWith("outcome", StringComparison.Ordinal));
        Assert.Contains(why, e => e.StartsWith("recommendation", StringComparison.Ordinal));
        Assert.Contains(why, e => e.StartsWith("summary", StringComparison.Ordinal));
        Assert.Contains(why, e => e.StartsWith("reasons", StringComparison.Ordinal));
        Assert.Contains(why, e => e.Contains("why is required", StringComparison.Ordinal));
        Assert.Contains(why, e => e.Contains("page limit", StringComparison.Ordinal));
        Assert.Contains(why, e => e.StartsWith("actions: give 1 to 12", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_action_has_an_owner_who_exists_and_a_date_and_lands_on_a_mailbox()
    {
        var (report, _) = PursuitRfpReview.ParseReport(Report);
        Assert.Equal(2, report!.Actions.Count);
        Assert.Equal(new DateOnly(2026, 10, 22), report.Actions[0].Due);
        Assert.Equal("ilalonde@korstructural.com", PursuitRfpReview.ResolveOwner(report.Actions[0].Owner, "ishabana@korstructural.com"));
        Assert.Equal("ishabana@korstructural.com", PursuitRfpReview.ResolveOwner(report.Actions[1].Owner, "ishabana@korstructural.com"));
        Assert.Equal("rwong@korstructural.com", PursuitRfpReview.ResolveOwner("RWong@korstructural.com", "x@y.ca"));
        Assert.Equal(report.Actions, PursuitRfpReview.ReadActions(PursuitRfpReview.ActionsJson(report.Actions)));

        var (bad, why) = PursuitRfpReview.ParseReport(Report.Replace("\"owner\": \"owner\"", "\"owner\": \"the team\"").Replace("2026-10-22", "Thursday"));
        Assert.Null(bad);
        Assert.Contains(why, e => e.StartsWith("action 1: due", StringComparison.Ordinal));
        Assert.Contains(why, e => e.StartsWith("action 2: owner", StringComparison.Ordinal));
    }

    [Fact]
    public void Questions_survive_the_round_trip_through_the_stored_json()
    {
        var (report, _) = PursuitRfpReview.ParseReport(Report);
        var back = PursuitRfpReview.ReadQuestions(PursuitRfpReview.QuestionsJson(report!.Questions));
        Assert.Equal(report.Questions, back);
        Assert.Empty(PursuitRfpReview.ReadQuestions("not json"));
    }

    [Fact]
    public void The_prompt_reads_the_files_from_the_database_and_reports_back_on_its_own_run_without_the_api()
    {
        var input = new RfpReviewInput(1142, "Structural and Civil Engineering PSA", "Banff Centre", "Amaya Kingston", "a@b.ca", Now.AddDays(12),
            null, "Drafting", "https://purchasing.alberta.ca/posting/AB-2026-06968",
            new[] { new RfpReviewFile(27, "RFP# PF-1026-02.pdf", 1_193_698, "apc", Now) }, 0, "ilalonde@korstructural.com", Now);
        var prompt = PursuitRfpReview.Prompt(input, new BdPromptHandle(88, "tok-abcdefghijklmnopqrstuvwxyz"));
        Assert.Contains("pursuit files text 1142", prompt);
        Assert.Contains("pursuit review report 88 <report.json> --token tok-abcdefghijklmnopqrstuvwxyz", prompt);
        Assert.Contains("file 27: RFP# PF-1026-02.pdf", prompt);
        Assert.Contains("Never call the Anthropic API", prompt);
        Assert.Contains("structural engineering consultancy", prompt);
        Assert.Contains("Never cite tracker win rates", prompt);
    }

    [Fact]
    public void The_status_line_says_where_the_documents_and_the_review_stand_in_each_state()
    {
        var waiting = new PursuitReviewStatus(1142, "APC", 0, null, null, null, null, null, null, null, null, null, 0);
        Assert.Equal("Waiting for the documents from APC, checked every 10 minutes.", PursuitRfpReview.Describe(waiting, Now));
        Assert.StartsWith("APC sign-in needed", PursuitRfpReview.Describe(waiting with { PortalSessionError = "refused" }, Now));
        Assert.StartsWith("The last APC download failed", PursuitRfpReview.Describe(waiting with { LastFetchAtUtc = Now, LastFetchSucceeded = false, LastFetchNote = "403" }, Now));
        Assert.Equal("3 documents in from APC. Claude's review starts by itself on Ian's PC.", PursuitRfpReview.Describe(waiting with { PortalFiles = 3 }, Now));
        var reading = waiting with { PortalFiles = 3, RunId = 9, RunCreatedAtUtc = Now.AddMinutes(-5) };
        Assert.StartsWith("Claude is reading the RFP", PursuitRfpReview.Describe(reading, Now));
        Assert.StartsWith("Claude started reading the RFP", PursuitRfpReview.Describe(reading with { RunCreatedAtUtc = Now.AddHours(-7) }, Now));
        Assert.Equal("Claude's RFP review is ready.", PursuitRfpReview.Describe(reading with { RunReportedAtUtc = Now, Reviews = 1 }, Now));
        Assert.Null(PursuitRfpReview.Describe(waiting with { Portal = null }, Now));
    }

    [Fact]
    public void A_review_opens_by_itself_only_for_a_drafting_pursuit_with_portal_documents_and_no_run_or_review()
    {
        var sql = SqlPursuitReviewStore.DueSql;
        Assert.Contains("e.Stage = 1", sql);
        Assert.Contains("f.UploadedBy IN (N'apc', N'bids-and-tenders')", sql);
        Assert.Contains("r.EngagementId = e.Id AND r.Kind = N'rfp'", sql);
        Assert.Contains("FROM opportunities.PursuitReview v WHERE v.EngagementId = e.Id", sql);
        Assert.Equal(PursuitRfpReview.PortalUploaders, new[] { "apc", "bids-and-tenders" });
    }
}
