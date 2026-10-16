#nullable enable
using Kor.Operations.NetworkOps.Core.Learning;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// "Someone is using it right now" came from the latest health check whatever its age -- out of hours, days old -- and
// shaped every restart warning (audit 2026-10-16, 1.11). A reading counts as NOW for two hours; after that its words are
// kept with their age, and its state is unknown.
public sealed class PresenceAsOfTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_fresh_reading_is_now()
        => Assert.Equal(("Active", "jmarkulin · active"), CommandCenterView.PresenceAsOf("Active", "jmarkulin · active", Now.AddMinutes(-40), Now));

    [Fact]
    public void A_stale_reading_keeps_its_words_with_their_age_and_loses_its_state()
    {
        var (state, summary) = CommandCenterView.PresenceAsOf("Active", "jmarkulin · active", Now.AddHours(-5), Now);
        Assert.Null(state);
        Assert.Equal("jmarkulin · active, at the last check 5 h ago", summary);
    }

    [Fact]
    public void No_reading_time_means_no_state()
        => Assert.Null(CommandCenterView.PresenceAsOf("Active", "x", null, Now).State);

    [Fact]
    public void Nothing_read_stays_nothing()
        => Assert.Equal((null, null), CommandCenterView.PresenceAsOf(null, null, Now.AddHours(-9), Now));
}
