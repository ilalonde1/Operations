#nullable enable
using Kor.Operations.NetworkOps.Core.Actions;
using Kor.Operations.NetworkOps.Core.Health;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// A PC with no keyboard and no mouse whose display switches itself off: a monitor plugged in later stays black.
//
// WHAT IT COVERS: the real probe-v7 snapshot from KOR-210 (the PC this was found on, 2026-10-01) raises the finding;
// a keyboard OR a mouse, or a display set to never turn off, raises nothing; an older probe raises nothing; the fix is
// offered for it, sets both plugged-in and battery to never, and is not disruptive.
// WHAT IT DOES NOT: whether the monitor then lights up (proven by Ian at KOR-210). A SAME-CLASS FAULT IT WOULD NOT CATCH:
// a group policy that forces the display timeout back would re-raise it at the next check, which is the right outcome,
// but nothing here says WHY it came back.
public sealed class HeadlessDisplayTests
{
    [Fact]
    public void KOR_210_as_found_raises_it()
    {
        var s = HealthSnapshot.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "health", "KOR-210-v7-2026-10-01.json")));
        Assert.Equal(7, s.ProbeVersion);
        var f = Assert.Single(HealthRules.Evaluate(s), x => x.RuleKey == "display-sleeps-headless");
        Assert.Contains("after 15 min", f.Evidence);
        Assert.Contains(FixCatalog.For(f.RuleKey), a => a.Id == "display-never-off");
    }

    private static HealthSnapshot With(ConsoleInfo? c) => new() { ProbeVersion = 7, Console = c };

    [Theory]
    [InlineData(900, 1, 0)]   // a keyboard can wake it
    [InlineData(900, 0, 1)]   // so can a mouse
    [InlineData(0, 0, 0)]     // never turns off
    public void A_display_that_can_wake_or_never_sleeps_raises_nothing(int offAfter, int keyboards, int mice)
        => Assert.DoesNotContain(HealthRules.Evaluate(With(new ConsoleInfo(offAfter, keyboards, mice))), f => f.RuleKey == "display-sleeps-headless");

    [Fact]
    public void An_older_probe_raises_nothing()
        => Assert.DoesNotContain(HealthRules.Evaluate(With(null)), f => f.RuleKey == "display-sleeps-headless");

    [Fact]
    public void The_fix_sets_never_on_mains_and_battery_and_is_not_disruptive()
    {
        var a = FixCatalog.Get("display-never-off")!;
        var script = FixCatalog.Script(a, null);
        Assert.Contains("monitor-timeout-ac 0", script);
        Assert.Contains("monitor-timeout-dc 0", script);
        Assert.False(a.Disruptive);
    }
}
