#nullable enable
using System;
using System.Linq;
using System.Text.RegularExpressions;
using Kor.Opportunities.Data.BdReports.Generators;
using Kor.Opportunities.Data.Crm;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// The 6 a.m. email's Done / Not today / Not doing links: the kor://move address round-trips, malformed ones are refused,
/// and the email carries three links per move only when the stored hour id is known. Does not open the app, show the
/// reason dialog, or write the store; a broken protocol registration on the PC would pass every test here.
/// </summary>
public sealed class MoveLinkTests
{
    [Theory]
    [InlineData("Done")]
    [InlineData("NotToday")]
    [InlineData("NotDoing")]
    public void Move_link_round_trips(string result)
    {
        var url = KorReportLinks.Move(41, 12, result);
        Assert.Equal($"kor://move/41/12/{result}", url);
        Assert.True(KorReportLinks.TryParseMove(url, out var hour, out var rank, out var parsed));
        Assert.Equal((41L, 12, result), (hour, rank, parsed));
        Assert.False(KorReportLinks.TryParse(url, out _, out _)); // never mistaken for a screen link
    }

    [Theory]
    [InlineData("kor://move/41/13/Done")]
    [InlineData("kor://move/41/0/Done")]
    [InlineData("kor://move/0/1/Done")]
    [InlineData("kor://move/41/1/Maybe")]
    [InlineData("kor://move/41/1")]
    [InlineData("kor://move/41/1/Done/extra")]
    [InlineData("kor://org/41/1/Done")]
    [InlineData("https://move/41/1/Done")]
    [InlineData(null)]
    public void Malformed_move_links_are_refused(string? url) => Assert.False(KorReportLinks.TryParseMove(url, out _, out _, out _));

    [Fact]
    public void Email_carries_three_answer_links_per_move_only_with_the_hour_id()
    {
        var hour = MorningBriefTests.FixtureHour();
        var moves = DirectorHourFile.AllMoves(hour).ToArray();

        var html = DirectorHourEmail.Render(hour, 7);
        foreach (var m in moves)
            foreach (var result in new[] { "Done", "NotToday", "NotDoing" })
                Assert.Contains($"href=\"kor://move/7/{m.Rank}/{result}\"", html, StringComparison.Ordinal);
        Assert.Equal(moves.Length, Regex.Matches(html, "Mark it: ").Count);

        Assert.DoesNotContain("kor://move/", DirectorHourEmail.Render(hour), StringComparison.Ordinal);
    }
}
