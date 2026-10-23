#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Power;
using Kor.Operations.NetworkOps.Core.Rack;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// UpsCardInfo + UpsRules' battery-due finding + the version-baseline probe for the cards. The values are the ones read off
// the live cards on 2026-10-22 (APC SRT1500 / NMC3 AP9641 at .101, Eaton 5PX / Network-M3 at .44).
//
// WHAT THIS COVERS: parsing each card's identity, firmware and battery dates; the facts recorded; the finding from the APC's
// recommended replace date at each threshold (none / Info to order / Warning due / Warning past); no battery finding for
// the Eaton (it publishes no recommended date); the card firmware reaching VersionBaselineRules as a product. WHAT IT DOES
// NOT: the SNMP read itself, or which baseline rows exist. A fault it would NOT catch: a card firmware update that changes
// the sysDescr layout (APC's "PF:" field) -- the firmware fact just goes missing, with no finding to say so.
public class UpsCardInfoTests
{
    private static readonly Dictionary<string, string> Apc = new()
    {
        [UpsMibs.SysDescr] = "APC Web/SNMP Management Card (MB:v4.2.9 PF:v2.5.5.1 PN:apc_hw21_aos_2.5.5.1.bin AF1:v2.5.5.1 AN1:apc_hw21_su_2.5.5.1.bin MN:AP9641 HR:05 SN: ZA2133012345 MD:08/16/2021)",
        [UpsMibs.ApcUpsFirmware] = "UPS 08.2 (ID1025)",
        [UpsMibs.ApcBatteryLastReplaced] = "08/31/2022",
        [UpsMibs.ApcBatteryReplaceBy] = "04/21/2027",
        [UpsMibs.ApcBatteryPart] = "APCRBC155",
    };

    private static readonly Dictionary<string, string> Eaton = new()
    {
        [UpsMibs.SysDescr] = "Gigabit Network Card 3",
        [UpsMibs.UpsMibUpsFirmware] = "01.12.0024",
        [UpsMibs.UpsMibAgentFirmware] = "2.1.2",
        [UpsMibs.EatonBatteryLastReplaced] = "10/01/2026",
    };

    private static readonly UpsReading Mains = new("APC", DateTime.UtcNow, true, PowerSource.Mains, 0, 40, 100, 30, false, false, null);

    [Fact]
    public void The_APC_card_reads_its_model_firmware_dates_and_part()
    {
        var c = UpsCardInfo.Parse(UpsMibs.PowerNet, Apc);
        Assert.Equal("APC AP9641", c.CardModel);
        Assert.Equal("2.5.5.1", c.CardFirmware);
        Assert.Equal("UPS 08.2 (ID1025)", c.UpsFirmware);
        Assert.Equal(new DateTime(2022, 8, 31), c.BatteryInstalled);
        Assert.Equal(new DateTime(2027, 4, 21), c.BatteryReplaceBy);
        Assert.Equal("APCRBC155", c.BatteryPart);
        Assert.Equal("2027-04-21", c.Facts()["ups.battery-replace-by"]);
    }

    [Fact]
    public void The_Eaton_card_reads_firmware_and_install_date_but_has_no_replace_by()
    {
        var c = UpsCardInfo.Parse(UpsMibs.UpsMib, Eaton);
        Assert.Equal("Eaton Gigabit Network Card 3", c.CardModel);
        Assert.Equal("2.1.2", c.CardFirmware);
        Assert.Equal(new DateTime(2026, 10, 1), c.BatteryInstalled);
        Assert.Null(c.BatteryReplaceBy);
        Assert.DoesNotContain(UpsRules.Evaluate(Mains, c, new DateTime(2026, 10, 22)).Findings, f => f.RuleKey == "ups.battery-due");
    }

    [Theory]
    [InlineData("2026-09-01", null)]                  // more than 6 months out: nothing yet
    [InlineData("2026-10-22", Severity.Info)]         // today: 6 months out -> order one (the APC's own warning, 10-21)
    [InlineData("2027-04-01", Severity.Warning)]      // within 30 days
    [InlineData("2027-05-01", Severity.Warning)]      // past it
    public void The_battery_finding_follows_the_cards_own_date(string today, Severity? expected)
    {
        var c = UpsCardInfo.Parse(UpsMibs.PowerNet, Apc);
        var f = UpsRules.Evaluate(Mains, c, DateTime.Parse(today, System.Globalization.CultureInfo.InvariantCulture)).Findings.SingleOrDefault(x => x.RuleKey == "ups.battery-due");
        Assert.Equal(expected, f?.Severity);
        if (f is not null) Assert.Contains("APCRBC155", f.Evidence);
    }

    [Fact]
    public void The_card_firmware_is_checked_against_the_version_baseline()
    {
        var facts = UpsRules.Evaluate(Mains, UpsCardInfo.Parse(UpsMibs.PowerNet, Apc), new DateTime(2026, 10, 22)).Facts;
        var none = VersionBaselineRules.Evaluate(facts, [], new DateTime(2026, 10, 22));
        Assert.Contains(none, f => f.RuleKey == "version.no-baseline" && f.Title.Contains("APC AP9641 card firmware"));

        var row = new VersionBaselineRow("APC AP9641 card firmware", "2.6.0.0", null, new DateTime(2026, 10, 22));
        Assert.Contains(VersionBaselineRules.Evaluate(facts, [row], new DateTime(2026, 10, 22)), f => f.RuleKey == "version.insecure");
    }

    [Fact]
    public void Without_a_card_read_the_power_state_is_still_judged()
    {
        var r = UpsRules.Evaluate(Mains with { Source = PowerSource.Battery, SecondsOnBattery = 60 });
        Assert.Contains(r.Findings, f => f.RuleKey == "ups.on-battery");
        Assert.DoesNotContain(r.Facts.Keys, k => k.StartsWith("ups.card", StringComparison.Ordinal));
    }
}
