#nullable enable
using Kor.Operations.NetworkOps.Core.Actions;
using Kor.Operations.NetworkOps.Core.Health;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// Two headless-PC findings: (1) display-sleeps-headless -- no keyboard/mouse and the display is set to switch off, so a
// monitor plugged in later stays black; (2) headless-no-display -- no keyboard/mouse AND the graphics card is driving no
// display at all (no monitor, no dummy plug), so KOR Remote shows a blank/fallback console and the GPU idles.
//
// WHAT IT COVERS: display-sleeps-headless -- the real probe-v7 KOR-210 snapshot raises it; a keyboard OR mouse, or a
// display set to never turn off, or an older probe, raises nothing; its fix sets mains+battery to never and is not
// disruptive. headless-no-display -- headless + a real GPU driving 0x0 raises it (Info); a monitor/dummy plug driving a
// real resolution, a keyboard or mouse, only the Basic Display adapter present (no real GPU), or a pre-v12 probe (no
// resolution data), each raise nothing. The two are INDEPENDENT: a box can have both.
// WHAT IT DOES NOT: whether a monitor then lights up (proven by Ian at KOR-210); the resolution VALUE (only 0-vs-nonzero is
// judged, so a card stuck at 1024x768 with a real monitor attached is "driving a display", not flagged); and which
// physical port the card has (HDMI vs DP) -- WMI does not expose it, so the plug type is still an eyeball per box.
// A SAME-CLASS FAULT IT WOULD NOT CATCH: a card driving the Microsoft Basic Display adapter at a real resolution (the GPU
// fell back but a monitor IS attached) -- that is the gpu-hangs/basic-display story, and this reads it as "driving a display".
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

    // --- headless-no-display: a headless PC whose graphics card is driving no screen (no monitor, no dummy plug). v12+.
    private static HealthSnapshot Headless(int keyboards, int mice, params DisplayAdapterInfo[] adapters)
        => new() { ProbeVersion = 12, Console = new ConsoleInfo(0, keyboards, mice), DisplayAdapters = adapters };

    private static DisplayAdapterInfo Gpu(int width, int height) => new("NVIDIA RTX A4000", "31.0.15.3699", 0, width, height);

    [Fact]
    public void Headless_with_a_gpu_driving_no_display_wants_a_dummy_plug()
    {
        var f = Assert.Single(HealthRules.Evaluate(Headless(0, 0, Gpu(0, 0))), x => x.RuleKey == "headless-no-display");
        Assert.Equal(Severity.Info, f.Severity);
        Assert.Contains("dummy plug", f.Evidence);
    }

    [Fact]
    public void A_monitor_or_dummy_plug_driving_a_real_resolution_raises_nothing()
        => Assert.DoesNotContain(HealthRules.Evaluate(Headless(0, 0, Gpu(3840, 2160))), f => f.RuleKey == "headless-no-display");

    [Theory]
    [InlineData(1, 0)]   // a keyboard means someone can sit at it
    [InlineData(0, 1)]   // so does a mouse
    public void A_keyboard_or_mouse_means_it_is_not_headless(int keyboards, int mice)
        => Assert.DoesNotContain(HealthRules.Evaluate(Headless(keyboards, mice, Gpu(0, 0))), f => f.RuleKey == "headless-no-display");

    [Fact]
    public void Only_the_basic_display_adapter_present_is_not_a_dummy_plug_case()
        // No real GPU to engage -- the card dropped off entirely. That is the gpu-hangs/basic-display story, not this one.
        => Assert.DoesNotContain(
            HealthRules.Evaluate(Headless(0, 0, new DisplayAdapterInfo("Microsoft Basic Display Adapter", null, 0, 1024, 768))),
            f => f.RuleKey == "headless-no-display");

    [Fact]
    public void A_pre_v12_probe_carries_no_resolution_so_raises_no_dummy_plug_finding()
        => Assert.DoesNotContain(
            HealthRules.Evaluate(new HealthSnapshot { ProbeVersion = 11, Console = new ConsoleInfo(0, 0, 0), DisplayAdapters = [new DisplayAdapterInfo("NVIDIA RTX A4000", "31.0.15.3699", 0)] }),
            f => f.RuleKey == "headless-no-display");

    [Fact]
    public void The_probe_reports_each_adapters_current_resolution()
    {
        var probe = Kor.Operations.NetworkOps.Core.Probes.ProbeLibrary.Get(Kor.Operations.NetworkOps.Core.Probes.ProbeLibrary.Health);
        Assert.Contains("CurrentHorizontalResolution", probe);
        Assert.Contains("CurrentVerticalResolution", probe);
        Assert.Contains("ProbeVersion  = 12", probe);
    }
}
