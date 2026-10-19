#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using Kor.MorningBrief;
using Kor.Opportunities.Data.BdReports.Generators;
using Kor.Opportunities.Data.Crm;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// The meeting rhythm of both virtual seats (migration 369): which cadence is due, the invite draft's validation, the
/// kor://draft link, the email's Outlook links and the composer's meeting lines. Covers state, proposed slot (including the
/// 1 Nov 2026 change from PDT to PST) and legacy meetings without a cadence id. Does NOT touch Outlook: a broken COM path or
/// a pwsh fallback that fails on the PC would pass every test here, and so would a cadence row seeded with a wrong person.
/// </summary>
public sealed class MeetingCadenceTests
{
    private static readonly TimeZoneInfo Pacific = MeetingsDue.Pacific();

    // 2026-10-18 20:00 PDT, the evening the rhythm was wired.
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 3, 0, 0, TimeSpan.Zero);

    private static MeetingCadenceRow Cadence(int id, string seat, string cadence, string kind, string? with, int minutes, int every,
        int weekday, int hour, int minute, DateOnly? first) =>
        new(id, seat, cadence, kind, $"Cadence {id}", with, new[] { with ?? "jmarkulin@korstructural.com" }, minutes, every, weekday,
            new TimeOnly(hour, minute), first);

    private static readonly MeetingCadenceRow BdMonthly = Cadence(1, "BD", "Monthly", "AllHands", null, 27, 28, 5, 14, 0, new DateOnly(2026, 11, 13));
    private static readonly MeetingCadenceRow CooMonthly = Cadence(2, "COO", "Monthly", "Other", null, 27, 28, 4, 9, 0, new DateOnly(2026, 10, 29));
    private static readonly MeetingCadenceRow Omar = Cadence(3, "BD", "Quickie", "OneToOne", "omara@korstructural.com", 15, 14, 2, 10, 0, new DateOnly(2026, 10, 20));
    private static readonly MeetingCadenceRow Jm = Cadence(7, "Both", "Quickie", "OneToOne", "jmarkulin@korstructural.com", 20, 14, 4, 10, 0, new DateOnly(2026, 10, 29));

    private static BdMeetingRow Meeting(long id, string kind, string? with, DateTimeOffset at, string status, int? cadence) =>
        new(id, kind, "m", with, null, at, 15, status, null, null, null, null) { CadenceId = cadence };

    private static readonly BdMeetingRow NinthOctober = Meeting(1, "AllHands", null, new DateTimeOffset(2026, 10, 9, 21, 1, 0, TimeSpan.Zero), "Held", 1);

    [Fact]
    public void The_first_round_from_tonight()
    {
        var due = MeetingsDue.Compute(new[] { BdMonthly, CooMonthly, Omar, Jm }, new[] { NinthOctober }, Now, Pacific)
            .ToDictionary(d => d.Cadence.Id);

        // BD monthly: 9 Oct + 28 days is 6 Nov, held back to Fri 13 Nov by FirstOnOrAfter; 14:00 PST after the 1 Nov change.
        Assert.Equal("Later", due[1].State);
        Assert.Equal(new DateTimeOffset(2026, 11, 13, 22, 0, 0, TimeSpan.Zero), due[1].ProposedUtc);
        Assert.Equal(NinthOctober.ScheduledForUtc, due[1].LastHeldUtc);

        // COO monthly: Thu 29 Oct 09:00 PDT, inside its 14-day lead, so the invite is owed now.
        Assert.Equal("Prepare", due[2].State);
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 16, 0, 0, TimeSpan.Zero), due[2].ProposedUtc);

        // Omar's quickie: Tue 20 Oct 10:00 PDT, never held.
        Assert.Equal("Prepare", due[3].State);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 17, 0, 0, TimeSpan.Zero), due[3].ProposedUtc);
        Assert.Null(due[3].LastHeldUtc);

        // JM's quickie: Thu 29 Oct is outside a quickie's 7-day lead tonight.
        Assert.Equal("Later", due[7].State);
    }

    [Fact]
    public void Planned_ahead_is_planned_and_a_passed_plan_asks_for_the_record()
    {
        var ahead = Meeting(2, "OneToOne", Omar.WithStaffKey, new DateTimeOffset(2026, 10, 20, 17, 0, 0, TimeSpan.Zero), "Planned", 3);
        var passed = Meeting(3, "Other", null, new DateTimeOffset(2026, 10, 15, 16, 0, 0, TimeSpan.Zero), "Planned", 2);

        var due = MeetingsDue.Compute(new[] { Omar, CooMonthly }, new[] { ahead, passed }, Now, Pacific).ToDictionary(d => d.Cadence.Id);

        Assert.Equal(("Planned", ahead.ScheduledForUtc), (due[3].State, due[3].PlannedUtc));
        Assert.Equal(("NotRecorded", passed.ScheduledForUtc), (due[2].State, due[2].PlannedUtc));
    }

    [Fact]
    public void A_held_quickie_comes_round_after_its_interval_on_its_weekday()
    {
        var held = Meeting(4, "OneToOne", Omar.WithStaffKey, new DateTimeOffset(2026, 10, 20, 17, 0, 0, TimeSpan.Zero), "Held", 3);
        var later = new DateTimeOffset(2026, 10, 21, 3, 0, 0, TimeSpan.Zero);

        var d = MeetingsDue.Compute(new[] { Omar }, new[] { held }, later, Pacific).Single();

        Assert.Equal("Later", d.State);
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 18, 0, 0, TimeSpan.Zero), d.ProposedUtc); // Tue 3 Nov 10:00 PST
    }

    [Fact]
    public void A_legacy_one_to_one_counts_for_its_person_only()
    {
        var omarHeld = Meeting(5, "OneToOne", "OMARA@korstructural.com", new DateTimeOffset(2026, 10, 16, 17, 0, 0, TimeSpan.Zero), "Held", null);

        var due = MeetingsDue.Compute(new[] { Omar, Jm }, new[] { omarHeld }, Now, Pacific).ToDictionary(d => d.Cadence.Id);

        Assert.Equal(omarHeld.ScheduledForUtc, due[3].LastHeldUtc);
        Assert.Null(due[7].LastHeldUtc);
    }

    [Fact]
    public void An_invite_draft_must_carry_a_time_and_a_length()
    {
        Assert.Empty(ParseWith(new HourDraft("omara@korstructural.com", "BD quickie", "Agenda")
            { When = "2026-10-20T10:00:00-07:00", Minutes = 15, CadenceId = 3 }).Errors);
        Assert.Contains(ParseWith(new HourDraft("a@b.c", "s", "b") { When = "2026-10-20T10:00:00-07:00" }).Errors, e => e.Contains("minutes from 5 to 240"));
        Assert.Contains(ParseWith(new HourDraft("a@b.c", "s", "b") { When = "2026-10-20", Minutes = 15 }).Errors, e => e.Contains("ISO date and time"));
        Assert.Contains(ParseWith(new HourDraft("a@b.c", "s", "b") { CadenceId = 3 }).Errors, e => e.Contains("no when"));
    }

    [Fact]
    public void Draft_link_round_trips_and_refuses_the_malformed()
    {
        var url = KorReportLinks.Draft(9, 4, 2);
        Assert.Equal("kor://draft/9/4/2", url);
        Assert.True(KorReportLinks.TryParseDraft(url, out var hour, out var rank, out var n));
        Assert.Equal((9L, 4, 2), (hour, rank, n));
        Assert.False(KorReportLinks.TryParseMove(url, out _, out _, out _));
        foreach (var bad in new[] { "kor://draft/9/4/0", "kor://draft/9/13/1", "kor://draft/0/1/1", "kor://draft/9/4", "kor://move/9/4/2", null })
            Assert.False(KorReportLinks.TryParseDraft(bad, out _, out _, out _));
    }

    [Fact]
    public void The_email_opens_each_draft_in_Outlook_only_with_the_hour_id()
    {
        var invite = new HourDraft("omara@korstructural.com", "BD quickie, 15 minutes", "Agenda")
            { When = "2026-10-20T10:00:00-07:00", Minutes = 15, CadenceId = 3 };
        var (hour, errors) = ParseWith(invite, new HourDraft("omara@korstructural.com", "Pre-read", "Figures"));
        Assert.Empty(errors);
        var rank = DirectorHourFile.AllMoves(hour!).First().Rank;

        var html = DirectorHourEmail.Render(hour!, 12);

        Assert.Contains($"href=\"kor://draft/12/{rank}/1\"", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"kor://draft/12/{rank}/2\"", html, StringComparison.Ordinal);
        Assert.Contains("Open the invite in Outlook", html, StringComparison.Ordinal);
        Assert.Contains("Tue 20 Oct, 10:00–10:15", html, StringComparison.Ordinal);
        Assert.DoesNotContain("kor://draft/", DirectorHourEmail.Render(hour!), StringComparison.Ordinal);
    }

    [Fact]
    public void The_composer_lists_a_due_meeting_with_its_slot_and_its_peoples_actions()
    {
        var action = new BdActionRow(22, "omara@korstructural.com", "Omar Alcazar", "Reach Beedie about the two Surrey towers.", null, 15,
            new DateTimeOffset(2026, 10, 17, 7, 0, 0, TimeSpan.Zero), "Open", null, null, null, 1, null, null, null, null, null, null);
        var due = MeetingsDue.Compute(new[] { Omar, BdMonthly }, new[] { NinthOctober }, Now, Pacific);

        var fact = MorningMeetings.Facts("BD meetings", due, new[] { action }, Now, Pacific);

        Assert.Equal("BD meetings (1 of 2 need you)", fact.Name);
        Assert.StartsWith("PREPARE cadence 3:", fact.Lines[0], StringComparison.Ordinal);
        Assert.Contains("(when 2026-10-20T10:00:00-07:00)", fact.Lines[0], StringComparison.Ordinal);
        Assert.Contains("#22 Reach Beedie about the two Surrey towers (due 2026-10-17, overdue)", fact.Lines[0], StringComparison.Ordinal);
        Assert.StartsWith("LATER cadence 1:", fact.Lines[1], StringComparison.Ordinal);
        Assert.Single(fact.Situations);
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
