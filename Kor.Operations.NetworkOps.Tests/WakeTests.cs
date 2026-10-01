#nullable enable
using Kor.Operations.NetworkOps.Core.Actions;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Learning;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// Wake-on-LAN: whether a PC can be woken (the finding), what the Wake button sends, and where it sends it.
//
// WHAT IT COVERS: a real probe-v6 snapshot from KOR-104N parses, reports Fast Startup on as the blocker and records the
// wired MAC as a fact; each blocker (Fast Startup, magic packet off, not armed, PME off, Lenovo BIOS Disabled) is named;
// a ready PC raises nothing; an unknown (non-Lenovo) BIOS is never called wrong; the magic packet is 6 x FF + 16 x MAC;
// MACs parse in the forms Windows and DHCP print them; the fix script exists and never restarts the card.
// WHAT IT DOES NOT: whether the PC actually wakes -- that is the BIOS and the network, proven only by shutting one down
// and pressing Wake (done live on KOR-104N). A SAME-CLASS FAULT IT WOULD NOT CATCH: a non-Lenovo BIOS with Wake on LAN
// off reads "ready" here; only the failed wake says so.
public sealed class WakeTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "health", name));

    [Fact]
    public void A_real_v6_snapshot_names_Fast_Startup_and_records_the_MAC()
    {
        var s = HealthSnapshot.Parse(Fixture("KOR-104N-v6-2026-10-01.json"));
        Assert.Equal(6, s.ProbeVersion);
        Assert.Equal("D8:BB:C1:2C:E8:D6", s.Wake!.Wired!.Mac);
        var f = Assert.Single(HealthRules.Evaluate(s), x => x.RuleKey == "wake-not-ready");
        Assert.StartsWith("Fast Startup is on", f.Evidence);
        Assert.Equal("D8:BB:C1:2C:E8:D6", Facts.Extract(s)![Facts.WiredMac]);
    }

    private static WakeInfo W(int fast = 0, bool magic = true, bool armed = true, string? pme = "Enabled", string? bios = "Automatic")
        => new(fast, [new WakeNic("AA:BB:CC:DD:EE:FF", "Intel I219", true, magic, armed, pme)], bios);

    [Fact]
    public void A_ready_pc_raises_nothing_and_each_blocker_is_named()
    {
        Assert.Empty(HealthRules.WakeProblems(W()));
        Assert.Empty(HealthRules.WakeProblems(W(bios: null)));   // non-Lenovo: unknown is not wrong
        Assert.Contains("Fast Startup", Assert.Single(HealthRules.WakeProblems(W(fast: 1))));
        Assert.Contains("Wake on Magic Packet is off", Assert.Single(HealthRules.WakeProblems(W(magic: false))));
        Assert.Contains("not allowed to wake", Assert.Single(HealthRules.WakeProblems(W(armed: false))));
        Assert.Contains("PME", Assert.Single(HealthRules.WakeProblems(W(pme: "Disabled"))));
        Assert.Contains("BIOS", Assert.Single(HealthRules.WakeProblems(W(bios: "Disabled"))));
        Assert.Contains("no wired network card", Assert.Single(HealthRules.WakeProblems(new WakeInfo(0, [], null))));
    }

    [Fact]
    public void An_older_probe_without_the_wake_block_raises_nothing()
        => Assert.Empty(HealthRules.WakeProblems(null));

    [Fact]
    public void The_magic_packet_is_six_FF_then_the_MAC_sixteen_times()
    {
        var mac = MagicPacket.ParseMac("18-c0-4d-28-9f-d6")!;
        var p = MagicPacket.Build(mac);
        Assert.Equal(102, p.Length);
        Assert.All(p[..6], b => Assert.Equal(0xFF, b));
        for (var r = 0; r < 16; r++) Assert.Equal(mac, p[(6 + r * 6)..(12 + r * 6)]);
    }

    [Theory]
    [InlineData("18:C0:4D:28:9F:D6")]
    [InlineData("18-c0-4d-28-9f-d6")]
    [InlineData("18C04D289FD6")]
    public void MACs_parse_in_the_forms_Windows_and_DHCP_print(string mac)
        => Assert.Equal(new byte[] { 0x18, 0xC0, 0x4D, 0x28, 0x9F, 0xD6 }, MagicPacket.ParseMac(mac));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("18:C0:4D:28:9F")]
    [InlineData("ZZ:C0:4D:28:9F:D6")]
    public void Anything_else_is_not_a_MAC(string? mac) => Assert.Null(MagicPacket.ParseMac(mac));

    [Fact]
    public void The_fix_never_resets_the_card_it_reports_back_through()
    {
        var script = FixCatalog.Script(FixCatalog.Get("enable-wake-on-lan")!, null);
        Assert.Contains("HiberbootEnabled -Value 0", script);
        foreach (var line in script.Split('\n').Where(l => l.Contains("Set-NetAdapterAdvancedProperty") || l.Contains("Enable-NetAdapterPowerManagement")))
            Assert.Contains("-NoRestart", line);
        Assert.False(FixCatalog.Get("enable-wake-on-lan")!.Disruptive);
    }
}
