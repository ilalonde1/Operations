#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using Kor.MorningBrief;
using Kor.Opportunities.Data.Crm;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// Pursuit touchstones (migration 371): which touchstone is due on which pursuit, the weekly owner roll-up, the composer's
/// lines and the roll-up draft's validation. Covers every anchor, the windows, the keep-warm clock restarting on a draft, and
/// the once-a-week rule giving way to urgent items. Does NOT cover the SQL that loads pursuits (a wrong join, a stage history
/// read the wrong way, or last touch ignoring filed email would pass every test here), nor the App's record-on-open.
/// </summary>
public sealed class PursuitTouchstoneTests
{
    private static readonly TimeZoneInfo Pacific = MeetingsDue.Pacific();

    // 2026-10-19 08:00 PDT.
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 15, 0, 0, TimeSpan.Zero);

    private static PursuitTouchstoneRow Rule(int id, string applies, string anchor, int offset, int window) =>
        new(id, applies, anchor, offset, window, $"Rule {id}", $"Carries {id}", new[] { "going-cold" });

    private static readonly PursuitTouchstoneRow[] Rules =
    {
        Rule(1, "Tender", "Opened", 2, 5), Rule(2, "Tender", "Deadline", -7, 4), Rule(3, "Tender", "Deadline", -2, 2),
        Rule(4, "Tender", "Deadline", 1, 0), Rule(5, "Any", "Submitted", 14, 14), Rule(6, "Any", "Submitted", 45, 0),
        Rule(7, "Relationship", "Opened", 7, 0), Rule(8, "Relationship", "LastTouch", 21, 0), Rule(9, "Any", "NextAction", 0, 0),
        Rule(10, "Any", "Won", 0, 30), Rule(11, "Any", "Lost", 0, 30),
    };

    private static DateTimeOffset DaysAgo(int days) => Now.AddDays(-days);

    private static PursuitState Pursuit(long id, int stage = 1, long? opportunity = null, int openedDaysAgo = 60, int touchedDaysAgo = 5,
        string owner = "ishabana@korstructural.com") =>
        new(id, owner, owner == "ishabana@korstructural.com" ? "Islam Shabana" : "Conor Murtagh", stage, $"Project {id}", "Buyer",
            7, opportunity, 50_000m, DaysAgo(openedDaysAgo), DaysAgo(touchedDaysAgo));

    private static int[] DueRules(PursuitState p, params TouchstoneDraftRow[] drafts) =>
        PursuitTouchstones.Compute(Rules, new[] { p }, drafts, Now, Pacific).Select(d => d.Touchstone.Id).OrderBy(i => i).ToArray();

    [Fact]
    public void A_tender_is_due_for_go_no_go_two_days_after_it_is_grabbed_and_not_after_its_window()
    {
        Assert.Equal(new[] { 1 }, DueRules(Pursuit(1, opportunity: 10, openedDaysAgo: 3, touchedDaysAgo: 3)));
        Assert.Empty(DueRules(Pursuit(1, opportunity: 10, openedDaysAgo: 1, touchedDaysAgo: 1)));
        Assert.Empty(DueRules(Pursuit(1, opportunity: 10, openedDaysAgo: 9, touchedDaysAgo: 9)));
    }

    [Fact]
    public void A_tender_deadline_brings_the_draft_review_then_the_submission_check_then_the_passed_question()
    {
        PursuitState Tender(int deadlineInDays) => Pursuit(2, opportunity: 11, openedDaysAgo: 30) with { DeadlineUtc = Now.AddDays(deadlineInDays) };
        Assert.Equal(new[] { 2 }, DueRules(Tender(6)));
        Assert.Equal(new[] { 3 }, DueRules(Tender(1)));
        Assert.Equal(new[] { 4 }, DueRules(Tender(-3)));
        // Submitted: the deadline touchstones stop; the follow-up comes two weeks after submitting.
        var submitted = Tender(-20) with { Stage = 3, SubmittedUtc = DaysAgo(15) };
        Assert.Equal(new[] { 5 }, DueRules(submitted));
        Assert.Equal(new[] { 6 }, DueRules(submitted with { SubmittedUtc = DaysAgo(50) }));
    }

    [Fact]
    public void A_quiet_relationship_pursuit_is_due_to_keep_warm_and_a_roll_up_restarts_its_clock()
    {
        var quiet = Pursuit(3, touchedDaysAgo: 30);
        Assert.Equal(new[] { 8 }, DueRules(quiet));
        var anchor = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(quiet.LastTouchUtc, Pacific).DateTime);
        var drafted = new TouchstoneDraftRow(new TouchstoneKey(3, 8, anchor), DaysAgo(3));
        Assert.Empty(DueRules(quiet, drafted));
        Assert.Equal(new[] { 8 }, DueRules(quiet, drafted with { DraftedAtUtc = DaysAgo(22) }));
        Assert.Empty(DueRules(Pursuit(3, touchedDaysAgo: 10)));
    }

    [Fact]
    public void First_touch_is_only_for_a_relationship_pursuit_never_touched()
    {
        var never = Pursuit(4, openedDaysAgo: 10, touchedDaysAgo: 10);
        Assert.Equal(new[] { 7 }, DueRules(never));
        Assert.Empty(DueRules(never with { LastTouchUtc = DaysAgo(2) }));
    }

    [Fact]
    public void Next_action_won_and_lost_come_due_only_when_something_is_missing()
    {
        Assert.Equal(new[] { 9 }, DueRules(Pursuit(5) with { NextActionDueUtc = DaysAgo(1), NextActionNote = "Call" }));
        Assert.Empty(DueRules(Pursuit(5) with { NextActionDueUtc = Now.AddDays(3) }));
        var won = Pursuit(6, stage: 6) with { ClosedUtc = DaysAgo(4) };
        Assert.Equal(new[] { 10 }, DueRules(won));
        Assert.Empty(DueRules(won with { WonProjectWbs1 = "31200.00" }));
        Assert.Empty(DueRules(won with { ClosedUtc = DaysAgo(40) }));
        var lost = Pursuit(7, stage: 7) with { ClosedUtc = DaysAgo(4) };
        Assert.Equal(new[] { 11 }, DueRules(lost));
        Assert.Empty(DueRules(lost with { OutcomeReason = "Fee" }));
    }

    [Fact]
    public void An_owner_hears_once_a_week_unless_something_urgent_is_due()
    {
        var quiet = Pursuit(8, touchedDaysAgo: 30);
        var other = Pursuit(9, touchedDaysAgo: 2);
        var lastWeek = new TouchstoneDraftRow(new TouchstoneKey(9, 8, new DateOnly(2026, 9, 1)), DaysAgo(3));
        var pursuits = new[] { quiet, other };
        var due = PursuitTouchstones.Compute(Rules, pursuits, new[] { lastWeek }, Now, Pacific);
        var rollup = Assert.Single(PursuitTouchstones.Rollups(due, pursuits, new[] { lastWeek }, Now, Pacific));
        Assert.False(rollup.IsOwed);
        Assert.Equal(new DateOnly(2026, 10, 23), rollup.NextOn);

        var urgent = other with { NextActionDueUtc = DaysAgo(1) };
        pursuits = new[] { quiet, urgent };
        due = PursuitTouchstones.Compute(Rules, pursuits, new[] { lastWeek }, Now, Pacific);
        rollup = Assert.Single(PursuitTouchstones.Rollups(due, pursuits, new[] { lastWeek }, Now, Pacific));
        Assert.True(rollup.IsOwed);
        Assert.Equal(new[] { 9, 8 }, rollup.Due.Select(d => d.Touchstone.Id).ToArray());
    }

    [Fact]
    public void The_composer_prints_each_owed_roll_up_with_its_keys_and_caps_what_it_shows()
    {
        var pursuits = Enumerable.Range(1, 10).Select(i => Pursuit(100 + i, touchedDaysAgo: 30 + i))
            .Append(Pursuit(200, touchedDaysAgo: 30, owner: "cmurtagh@korstructural.com"))
            .Append(Pursuit(201, touchedDaysAgo: 1, owner: "cmurtagh@korstructural.com")).ToArray();
        // Conor had a roll-up two days ago (for a next action on his other pursuit), so his keep-warm item waits for next week.
        var conorDraft = new TouchstoneDraftRow(new TouchstoneKey(201, 9, new DateOnly(2026, 8, 1)), DaysAgo(2));
        var drafts = new[] { conorDraft };
        var due = PursuitTouchstones.Compute(Rules, pursuits, drafts, Now, Pacific);
        var fact = MorningTouchstones.Facts(PursuitTouchstones.Rollups(due, pursuits, drafts, Now, Pacific), Now, Pacific);

        Assert.Equal("Pursuit touchstones (1 of 2 owners owe a roll-up today)", fact.Name);
        Assert.Contains(fact.Lines, l => l.StartsWith("ROLL-UP Islam Shabana <ishabana@korstructural.com>: 10 of 10 live pursuits have a touchstone due", StringComparison.Ordinal)
            && l.EndsWith("Showing 8 of 10. Subject: \"Your pursuits this week: 8 to move\".", StringComparison.Ordinal));
        Assert.Equal(8, fact.Lines.Count(l => l.Contains(" · Rule 8 · pursuit #", StringComparison.Ordinal)));
        // Quietest first: pursuit 110 (40 days), and its key is printed so the draft can carry it.
        var first = fact.Lines.First(l => l.Contains(" · Rule 8 · pursuit #", StringComparison.Ordinal));
        Assert.Contains("pursuit #110 Project 110", first);
        Assert.EndsWith("key " + due.First(d => d.Pursuit.EngagementId == 110).Key, first);
        Assert.Contains(fact.Lines, l => l.StartsWith("NOT TODAY Conor Murtagh <cmurtagh@korstructural.com>: 1 of 2 live pursuits have a touchstone due, none urgent", StringComparison.Ordinal));
        Assert.Single(fact.Situations);
    }

    [Fact]
    public void A_roll_up_asks_one_thing_per_pursuit_and_a_roll_up_restarts_every_quiet_clock()
    {
        // Never touched, quiet for months, and a next action overdue: three touchstones, one ask.
        var p = Pursuit(10, openedDaysAgo: 90, touchedDaysAgo: 90) with { NextActionDueUtc = DaysAgo(2) };
        var due = PursuitTouchstones.Compute(Rules, new[] { p }, Array.Empty<TouchstoneDraftRow>(), Now, Pacific);
        Assert.Equal(new[] { 7, 8, 9 }, due.Select(d => d.Touchstone.Id).OrderBy(i => i).ToArray());
        var rollup = Assert.Single(PursuitTouchstones.Rollups(due, new[] { p }, Array.Empty<TouchstoneDraftRow>(), Now, Pacific));
        Assert.Equal(9, Assert.Single(rollup.Due).Touchstone.Id);

        // Once that roll-up is drafted, keep-warm waits three weeks from it; first touch stays owed until a touch is logged.
        var drafted = new[] { new TouchstoneDraftRow(rollup.Due[0].Key, Now.AddHours(-1)) };
        Assert.Equal(new[] { 7 }, PursuitTouchstones.Compute(Rules, new[] { p }, drafted, Now, Pacific).Select(d => d.Touchstone.Id).ToArray());
    }

    [Fact]
    public void A_roll_up_draft_carries_valid_keys_and_an_invite_carries_none()
    {
        Assert.True(TouchstoneKey.TryParse("312:8:2026-09-06", out var key));
        Assert.Equal("312:8:2026-09-06", key.ToString());
        Assert.False(TouchstoneKey.TryParse("312:8:06-09-2026", out _));

        var ok = ParseWith(new HourDraft("ishabana@korstructural.com", "Your pursuits this week: 1 to move",
            "Hi Islam,\n\n1. Arcadis IBI Group (Arcadis IBI Group)\nDo: Call.\nBy: Fri 23 Oct\nWhy: Quiet.\n\nIan") { Touchstones = new[] { "312:8:2026-09-06" } });
        Assert.Empty(ok.Errors);
        var draft = Assert.Single(ok.Content!.Sections![0].Moves[0].Drafts);
        Assert.Equal(new[] { "312:8:2026-09-06" }, draft.Touchstones);
        Assert.Equal(new[] { "1. Arcadis IBI Group (Arcadis IBI Group)" }, draft.RollupHeadlines);

        var bad = ParseWith(new HourDraft("ishabana@korstructural.com", "Roll-up", "Body") { Touchstones = new[] { "pursuit 312" } });
        Assert.Contains(bad.Errors, e => e.Contains("touchstone 'pursuit 312'", StringComparison.Ordinal));
        // Two pursuits covered, one numbered block: the email's glance would hide one, so the import refuses it.
        var short1 = ParseWith(new HourDraft("ishabana@korstructural.com", "Roll-up", "1. One\nDo: x")
            { Touchstones = new[] { "312:8:2026-09-06", "313:8:2026-09-06" } });
        Assert.Contains(short1.Errors, e => e.Contains("covers 2 pursuits but numbers 1 items", StringComparison.Ordinal));
        var invite = ParseWith(HourDraft.Invite("omara@korstructural.com", "Quickie", "Agenda", Now.AddDays(1), 15)
            with { Touchstones = new[] { "312:8:2026-09-06" } });
        Assert.Contains(invite.Errors, e => e.Contains("cannot carry touchstones", StringComparison.Ordinal));
    }

    [Fact]
    public void Each_item_says_when_it_is_due_back_and_the_subject_says_how_heavy_the_week_is()
    {
        // Monday 19 Oct: urgent = two working days (Wed 21); otherwise the Friday after that (Fri 23); a tender closing on
        // Tue 20 pulls its submission check back to the day before, Mon 19.
        var urgent = PursuitTouchstones.Compute(Rules, new[] { Pursuit(1) with { NextActionDueUtc = DaysAgo(1) } }, Array.Empty<TouchstoneDraftRow>(), Now, Pacific).Single();
        var soft = PursuitTouchstones.Compute(Rules, new[] { Pursuit(2, touchedDaysAgo: 30) }, Array.Empty<TouchstoneDraftRow>(), Now, Pacific).Single();
        Assert.Equal(new DateOnly(2026, 10, 21), MorningTouchstones.DueBy(urgent, Now, Pacific));
        Assert.Equal(new DateOnly(2026, 10, 23), MorningTouchstones.DueBy(soft, Now, Pacific));
        var tender = Pursuit(3, opportunity: 12, openedDaysAgo: 30) with { DeadlineUtc = new DateTimeOffset(2026, 10, 20, 21, 0, 0, TimeSpan.Zero) };
        var review = PursuitTouchstones.Compute(Rules, new[] { tender }, Array.Empty<TouchstoneDraftRow>(), Now, Pacific).Single();
        Assert.Equal(new DateOnly(2026, 10, 19), MorningTouchstones.DueBy(review, Now, Pacific));

        Assert.Equal("Your pursuits this week: 1 urgent, 1 to move", MorningTouchstones.Subject(new[] { urgent, soft }));
        Assert.Equal("Your pursuits this week: 1 to move", MorningTouchstones.Subject(new[] { soft }));
        Assert.Equal("Your pursuits this week: 1 urgent", MorningTouchstones.Subject(new[] { urgent }));
    }

    private static (DirectorHourContent? Content, System.Collections.Generic.IReadOnlyList<string> Errors) ParseWith(params HourDraft[] drafts)
    {
        var hour = MorningBriefTests.FixtureHour();
        var sections = hour.Sections!.Select((s, i) => i == 0
            ? s with { Moves = s.Moves.Select((m, j) => j == 0 ? m with { Drafts = drafts } : m).ToArray() }
            : s).ToArray();
        var json = JsonSerializer.Serialize(hour with { Sections = sections }, DirectorHourFile.JsonOptions);
        return DirectorHourFile.Parse(json);
    }
}
