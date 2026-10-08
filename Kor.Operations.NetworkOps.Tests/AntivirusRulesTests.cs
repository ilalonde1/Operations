#nullable enable
using System.Linq;
using Kor.Operations.NetworkOps.Core.Health;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// Antivirus posture on a workstation (probe v13, health.ps1 + HealthRules). The POLICY is: Defender OFF on a
// workstation, Webroot is the antivirus. Until v13 the workstation probe read NO antivirus state at all -- only the
// SERVER probe did -- so there was no way to see whether "Defender off" was actually applied on a PC (Ian, 2026-10-08).
//
// WHAT THIS COVERS: the wanted state (Defender passive, Webroot running) is silent; Defender in Normal mode raises
// av-defender-on; genuinely nothing protecting raises av-none; and an unreadable or unknown reading raises NEITHER
// (null = unknown, never a false alarm), including the SecurityCenter2 cross-check that keeps av-none from firing when
// Webroot is registered even though its service read missed.
// WHAT THIS DOES NOT COVER: that health.ps1 actually populates these fields on a real PC (that is the probe, verified
// live), or any mode string Windows emits other than the ones tested (e.g. "EDR Block Mode") -- those fall through as
// "not Normal", i.e. not a Defender-on finding, which is the intended conservative default.
public sealed class AntivirusRulesTests
{
    private static System.Collections.Generic.IReadOnlyList<Finding> Eval(AntivirusInfo? av)
        => HealthRules.Evaluate(new HealthSnapshot { ProbeVersion = 13, Antivirus = av });

    private static bool Has(System.Collections.Generic.IReadOnlyList<Finding> f, string key)
        => f.Any(x => x.RuleKey == key);

    [Fact]
    public void Defender_passive_with_Webroot_running_is_the_wanted_state_and_is_silent()
    {
        var f = Eval(new AntivirusInfo(DefenderInstalled: true, DefenderMode: "Passive", DefenderRealtime: false,
            WebrootRunning: true, RegisteredAv: ["Webroot SecureAnywhere", "Windows Defender"]));
        Assert.False(Has(f, "av-defender-on"));
        Assert.False(Has(f, "av-none"));
    }

    [Fact]
    public void Defender_in_Normal_mode_on_a_workstation_is_flagged()
    {
        var f = Eval(new AntivirusInfo(true, "Normal", true, WebrootRunning: false, RegisteredAv: ["Windows Defender"]));
        var hit = Assert.Single(f, x => x.RuleKey == "av-defender-on");
        Assert.Equal(Severity.Warning, hit.Severity);
        Assert.False(Has(f, "av-none"));   // Defender IS active, so it is not "no antivirus"
    }

    [Fact]
    public void Defender_normal_alongside_running_Webroot_is_two_engines_and_still_av_defender_on()
    {
        var f = Eval(new AntivirusInfo(true, "Normal", true, WebrootRunning: true, RegisteredAv: ["Webroot SecureAnywhere", "Windows Defender"]));
        var hit = Assert.Single(f, x => x.RuleKey == "av-defender-on");
        Assert.Contains("two engines", hit.Evidence);
    }

    [Fact]
    public void Nothing_protecting_raises_av_none_critical()
    {
        var f = Eval(new AntivirusInfo(true, "Not running", false, WebrootRunning: false, RegisteredAv: ["Windows Defender"]));
        var hit = Assert.Single(f, x => x.RuleKey == "av-none");
        Assert.Equal(Severity.Critical, hit.Severity);
        Assert.False(Has(f, "av-defender-on"));
    }

    [Fact]
    public void Webroot_registered_but_its_service_read_missed_does_not_cry_unprotected()
    {
        // WRSVC came back not-Running, but Security Center still has Webroot registered: don't raise av-none.
        var f = Eval(new AntivirusInfo(true, "Passive", false, WebrootRunning: false, RegisteredAv: ["Webroot SecureAnywhere"]));
        Assert.False(Has(f, "av-none"));
        Assert.False(Has(f, "av-defender-on"));
    }

    [Fact]
    public void An_unreadable_defender_mode_raises_neither_finding()
    {
        // Get-MpComputerStatus failed, so DefenderMode is empty: unknown must never be a finding either way.
        var f = Eval(new AntivirusInfo(DefenderInstalled: null, DefenderMode: "", DefenderRealtime: null,
            WebrootRunning: false, RegisteredAv: []));
        Assert.False(Has(f, "av-defender-on"));
        Assert.False(Has(f, "av-none"));
    }

    [Fact]
    public void No_antivirus_reading_at_all_raises_neither_finding()
    {
        var f = Eval(null);
        Assert.False(Has(f, "av-defender-on"));
        Assert.False(Has(f, "av-none"));
    }
}
