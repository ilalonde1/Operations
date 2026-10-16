#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.NetworkOps.Core.Health;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// What distinguishes a genuine OS patch from a driver in Windows-Update history, and that the
// "last patched" date (and the not-patched clock) count only real patches.
//
// WHAT IT COVERS: UpdateHistory's KB-tagged classification over a realistic mixed history (cumulative,
// servicing-stack, .NET, MSRT, and real driver titles incl. the Silicon-Labs / NVIDIA entries seen on the
// fleet), the UpdateInfo computed members, back-compat with pre-v14 scalar-only captures, and the
// not-patched rule reading the classified patch date. WHAT IT DOES NOT: that the probe actually emits a
// populated Recent[] on a real machine (a live run proves that, not this), nor a feature-update / OOB whose
// title carries no KB (it reads as "other", a documented limitation). A SAME-CLASS FAULT IT WOULD NOT CATCH:
// a patch whose title is renamed to drop the KB token -- classification is by title only.
public sealed class UpdateHistoryTests
{
    private static UpdateHistoryItem H(string title, string date, string? by = null)
        => new(title, DateTime.Parse(date), by);

    private const string Cumulative = "2026-10 Cumulative Update for Windows 11 Version 24H2 for x64-based Systems (KB5127648)";
    private const string Ssu = "2026-10 Servicing Stack Update for Windows 11 Version 24H2 for x64-based Systems (KB5128100)";
    private const string DotNet = "2026-10 Cumulative Update for .NET Framework 3.5 and 4.8.1 for Windows 11 (KB5126990)";
    private const string Msrt = "Windows Malicious Software Removal Tool x64 - v5.130 (KB890830)";
    private const string SiliconLabs = "Silicon Laboratories Inc. - Ports - 11.2.0.167";
    private const string Nvidia = "NVIDIA - Display - 31.0.15.3699";

    [Fact]
    public void A_driver_is_never_the_last_patch_even_when_it_is_the_newest_install()
    {
        // The newest install is a driver; the newest KB-tagged patch is older. The device page's
        // "last patched" must be the patch, and the driver must surface only as "other".
        var recent = new List<UpdateHistoryItem>
        {
            H(Nvidia,      "2026-10-15T09:00:00"),
            H(SiliconLabs, "2026-10-12T08:00:00"),
            H(Cumulative,  "2026-10-08T23:14:00", "MoUpdateOrchestrator"),
            H(Msrt,        "2026-10-08T23:10:00"),
            H(DotNet,      "2026-10-08T23:05:00"),
            H(Ssu,         "2026-09-20T22:00:00"),
        };

        var patch = UpdateHistory.LastPatch(recent);
        var other = UpdateHistory.LastOther(recent);

        Assert.NotNull(patch);
        Assert.Contains("KB5127648", patch!.Title);          // the newest KB-tagged entry
        Assert.True(UpdateHistory.IsOsPatch(patch.Title));
        Assert.Equal(Nvidia, other?.Title);                   // the newest driver
        Assert.False(UpdateHistory.IsOsPatch(SiliconLabs));
        Assert.False(UpdateHistory.IsOsPatch(Nvidia));
        Assert.True(UpdateHistory.IsOsPatch(Msrt));           // MSRT carries a KB -- it counts
    }

    [Fact]
    public void A_newer_driver_does_not_displace_an_older_patch()
    {
        var recent = new List<UpdateHistoryItem> { H(SiliconLabs, "2026-10-12"), H(Cumulative, "2026-10-08") };
        Assert.Equal(Cumulative, UpdateHistory.LastPatch(recent)?.Title);
        Assert.Equal(SiliconLabs, UpdateHistory.LastOther(recent)?.Title);
    }

    [Fact]
    public void No_history_classifies_to_nothing()
    {
        Assert.Null(UpdateHistory.LastPatch(null));
        Assert.Null(UpdateHistory.LastOther(null));
        Assert.Null(UpdateHistory.LastPatch(new List<UpdateHistoryItem>()));
        Assert.Null(UpdateHistory.LastOther(new List<UpdateHistoryItem>()));
    }

    [Fact]
    public void Recent_drives_the_computed_members()
    {
        var u = new UpdateInfo(true, 1)
        {
            Recent = [H(Nvidia, "2026-10-15T09:00:00"), H(Cumulative, "2026-10-08T23:14:00", "MoUpdateOrchestrator")],
        };
        Assert.Equal(new DateTime(2026, 10, 8, 23, 14, 0), u.LastInstall);
        Assert.Contains("KB5127648", u.LastInstallTitle!);
        Assert.Equal("MoUpdateOrchestrator", u.LastInstallBy);
        Assert.Equal(Nvidia, u.LastOther?.Title);
    }

    [Fact]
    public void Pre_v14_scalar_only_captures_keep_their_old_meaning()
    {
        // Old fixtures have no Recent. The three scalars are read exactly as before -- including the case
        // where that scalar was itself a driver (KOR-1001's Silicon-Labs line): it is NOT reclassified away,
        // because the collapsed history is gone; only fresh v14 captures get the richer split.
        var patch = new UpdateInfo(true, 1) { RawLastInstall = new DateTime(2026, 9, 28), RawLastInstallTitle = "Update for Windows (KB5124010)", RawLastInstallBy = "wusa" };
        Assert.Equal(new DateTime(2026, 9, 28), patch.LastInstall);
        Assert.Equal("Update for Windows (KB5124010)", patch.LastInstallTitle);
        Assert.Null(patch.LastOther);

        var driver = new UpdateInfo(true, 1) { RawLastInstall = new DateTime(2026, 9, 29), RawLastInstallTitle = SiliconLabs, RawLastInstallBy = "pwsh.exe" };
        Assert.Equal(new DateTime(2026, 9, 29), driver.LastInstall);
        Assert.Equal(SiliconLabs, driver.LastInstallTitle);
        Assert.Null(driver.LastOther);
    }

    [Fact]
    public void Not_patched_counts_the_last_real_patch_not_a_recent_driver()
    {
        var now = new DateTime(2026, 10, 16);

        // Last KB patch 45 days old; a driver 4 days old must NOT mask the gap.
        var stale = Snap(now, new UpdateInfo(true, 1) { Recent = [H(SiliconLabs, "2026-10-12"), H(Cumulative, "2026-09-01")] });
        Assert.Contains(HealthRules.Evaluate(stale), x => x.RuleKey == "not-patched");

        // A fresh KB patch 3 days old -> no gap, even with an old driver present.
        var fresh = Snap(now, new UpdateInfo(true, 1) { Recent = [H(SiliconLabs, "2026-09-01"), H(Cumulative, "2026-10-13")] });
        Assert.DoesNotContain(HealthRules.Evaluate(fresh), x => x.RuleKey == "not-patched");
    }

    private static HealthSnapshot Snap(DateTime collectedAt, UpdateInfo update)
        => new() { Computer = "SYNTH", CollectedAt = collectedAt, WmiHealthy = true, Update = update };
}
