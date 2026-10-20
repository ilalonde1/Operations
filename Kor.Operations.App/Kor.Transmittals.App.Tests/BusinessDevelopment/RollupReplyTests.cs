#nullable enable
using System;
using System.Linq;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Crm;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// Recording an owner's reply to a roll-up: the numbered lines are read, each number maps to the pursuit the roll-up numbered,
/// and each understood line becomes a logged activity plus its change (moved date, cleared next step, archive). Covers the
/// three answers, the ways a reply goes wrong (unknown number, twice-answered, not understood, a past date, quoted text) and
/// the date forms owners write. Does NOT cover the CRM writes themselves (UpdateAsync and AppendAsync are existing, tested
/// stores) or the dialog; a roll-up whose numbered blocks were written in a different order from its keys would map wrongly
/// here, which is why the preview names every pursuit before anything is saved.
/// </summary>
public sealed class RollupReplyTests
{
    private static readonly DateOnly Today = new(2026, 10, 20); // a Tuesday

    private static HourDraft Rollup() => new("omara@korstructural.com", "Your pursuits this week", "Hi Omar,")
    {
        Touchstones = new[] { "312:9:2026-09-30", "312:8:2026-09-06", "401:7:2026-05-30", "455:5:2026-10-01" },
    };

    [Fact]
    public void Items_follow_the_keys_one_per_pursuit()
    {
        var items = RollupReply.Items(Rollup());
        Assert.Equal(new[] { (1, 312L, 9), (2, 401L, 7), (3, 455L, 5) }, items.Select(i => (i.Number, i.EngagementId, i.TouchstoneId)).ToArray());
    }

    [Fact]
    public void The_three_answers_and_the_date_forms_owners_write_are_read()
    {
        var lines = RollupReply.Parse("""
            Thanks Ian,
            1. done, called Stefan
            2) moved to Fri 30 Oct
            3 - dropped, they went with RJC
            > 1. URGENT · quoted roll-up text is ignored
            """, Today);
        Assert.Equal(new[] { "Done", "Moved", "Dropped" }, lines.Select(l => l.Kind).ToArray());
        Assert.Equal("called Stefan", lines[0].Note);
        Assert.Equal(new DateOnly(2026, 10, 30), lines[1].MovedTo);
        Assert.Equal("they went with RJC", lines[2].Note);

        Assert.Equal(new DateOnly(2026, 10, 23), RollupReply.FindDate("by Friday", Today));
        Assert.Equal(new DateOnly(2026, 10, 27), RollupReply.FindDate("moved to tue", Today)); // today is Tuesday: next one
        Assert.Equal(new DateOnly(2026, 11, 4), RollupReply.FindDate("moved to Nov 4", Today));
        Assert.Equal(new DateOnly(2026, 11, 6), RollupReply.FindDate("moved to 2026-11-06", Today));
        Assert.Equal(new DateOnly(2027, 1, 5), RollupReply.FindDate("moved to 5 Jan", Today.AddDays(60)));
        Assert.Equal(Today.AddDays(7), RollupReply.FindDate("moved, next week", Today));
        Assert.Equal("Unclear", RollupReply.Parse("2. moved", Today).Single().Kind); // moved with no date is not guessed
    }

    [Fact]
    public void Problems_are_reported_and_nothing_is_planned_for_them()
    {
        var lines = RollupReply.Parse("1. done\n1. moved to 30 Oct\n2. maybe later\n7. done\n3. moved to 2 Oct", Today);
        var (actions, problems) = RollupReply.Plan(lines, RollupReply.Items(Rollup()), id => "Pursuit " + id, Today);
        Assert.Equal(new[] { 1 }, actions.Select(a => a.Item.Number).ToArray());
        Assert.Contains(problems, p => p.StartsWith("1 is answered 2 times"));
        Assert.Contains(problems, p => p.StartsWith("2 (Pursuit 401): not understood"));
        Assert.Contains(problems, p => p.StartsWith("7: the roll-up has no item 7"));
        Assert.Contains(problems, p => p.Contains("which has passed"));
        Assert.Equal("1. Pursuit 312: done; its due next step is cleared; logged.", actions[0].Preview);
    }

    [Fact]
    public void Each_answer_changes_the_pursuit_the_way_the_preview_says()
    {
        var zone = MeetingsDue.Pacific();
        var e = new CrmEngagement { Id = 312, Stage = CrmEngagementStage.Drafting, NextActionDueUtc = DateTimeOffset.Parse("2026-09-30T17:00:00-07:00"), NextActionNote = "Follow Stefan" };
        var (actions, _) = RollupReply.Plan(RollupReply.Parse("1. done\n2. moved to 30 Oct\n3. dropped, went with RJC", Today),
            RollupReply.Items(Rollup()), id => "P" + id, Today);

        var done = RollupReply.Apply(e, actions[0], zone);
        Assert.Null(done.NextActionDueUtc);
        Assert.Null(done.NextActionNote);

        var moved = RollupReply.Apply(e, actions[1], zone);
        Assert.Equal(DateTimeOffset.Parse("2026-10-30T17:00:00-07:00"), moved.NextActionDueUtc);
        Assert.Equal(CrmEngagementStage.Drafting, moved.Stage);

        var dropped = RollupReply.Apply(e, actions[2], zone);
        Assert.Equal(CrmEngagementStage.Archived, dropped.Stage);
        Assert.Equal("went with RJC", dropped.OutcomeReason);

        // "done" on a first-touch ask changes nothing but the log.
        var firstTouch = actions[0] with { Item = actions[0].Item with { TouchstoneId = 7 } };
        Assert.Same(e, RollupReply.Apply(e, firstTouch, zone));

        var log = RollupReply.Activity(actions[1], "Omar", "ilalonde@korstructural.com", DateTimeOffset.Parse("2026-10-20T09:00:00-07:00"));
        Assert.Equal(401, log.EngagementId);
        Assert.Equal("Roll-up reply from Omar: moved", log.Subject);
        Assert.Equal("2. moved to 30 Oct", log.Body);
    }
}
