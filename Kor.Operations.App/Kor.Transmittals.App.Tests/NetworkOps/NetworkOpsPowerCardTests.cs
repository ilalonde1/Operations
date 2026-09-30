#nullable enable
using System;
using System.Linq;
using Kor.Operations.App.NetworkOps;
using Kor.Operations.NetworkOps.Core.Learning;
using Xunit;

namespace Kor.Operations.App.Tests.NetworkOps;

/// <summary>
/// The Rack power card: what each UPS line says and which colour it gets, the headline for each verdict,
/// and that an unarmed chain says so in words. WHAT IT DOES NOT COVER: how the card looks (the render
/// test draws it to a PNG) or anything the service decides -- this only reads a PowerSnapshot.
/// </summary>
public sealed class NetworkOpsPowerCardTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 20, 0, 0, DateTimeKind.Utc);

    /// <summary>The Eaton's circuit is out; the APC carries the rack. Also the render test's fixture.</summary>
    internal static PowerSnapshot OneOnBattery() => new(
        [
            new UpsRow("APC SRT1500", "192.168.1.101", DateTime.UtcNow.AddSeconds(-8), true, "Mains", 0, 36, 100, 31, false, false, null),
            new UpsRow("Eaton 5PX", "192.168.1.44", DateTime.UtcNow.AddSeconds(-8), true, "Battery", 420, 21, 74, 31, false, false, null),
        ],
        "Degraded", "Eaton 5PX ON BATTERY for 7 min, 21 min left -- APC SRT1500 is carrying the rack on mains, so nothing is shut down",
        DateTime.UtcNow.AddMinutes(-7), false,
        [new PowerEventRow(DateTime.UtcNow.AddHours(-13), "ChainEnd", true, true, "DRY RUN complete: 5 steps, every one proven")]);

    [Fact]
    public void A_UPS_on_battery_is_red_and_says_how_long_while_the_one_on_mains_is_green()
    {
        var vm = new NetworkOpsCommandCenterViewModel(NetworkOpsClient.Unconfigured("test"));
        vm.ApplyPower(OneOnBattery(), Now);

        var eaton = vm.Ups.Single(u => u.Name.StartsWith("Eaton"));
        Assert.StartsWith("ON BATTERY 7 min", eaton.Detail);
        Assert.Contains("21 min runtime", eaton.Detail);
        Assert.Same(NetworkOpsBrushes.Critical, eaton.Brush);
        Assert.Same(NetworkOpsBrushes.Healthy, vm.Ups.Single(u => u.Name.StartsWith("APC")).Brush);
        Assert.Equal("Needs attention", vm.PowerHeadline);
        Assert.Contains("NOT armed", vm.ChainText);
        Assert.Contains("dry run", vm.LastRehearsalText);
    }

    [Fact]
    public void A_card_that_is_not_answering_is_grey_and_says_so()
    {
        var vm = new NetworkOpsCommandCenterViewModel(NetworkOpsClient.Unconfigured("test"));
        vm.ApplyPower(new PowerSnapshot([new UpsRow("APC SRT1500", ".101", Now.AddMinutes(-5), false, "Mains", 0, 36, 100, 31, false, false, "timeout")],
            "Degraded", "APC SRT1500 not answering", Now, true, []), Now);
        Assert.StartsWith("NOT ANSWERING", vm.Ups.Single().Detail);
        Assert.Same(NetworkOpsBrushes.Unknown, vm.Ups.Single().Brush);
        Assert.Contains("ARMED", vm.ChainText);
        Assert.Equal("No chain run recorded yet.", vm.LastRehearsalText);
    }

    [Fact]
    public void A_service_without_the_rack_configured_reads_as_not_watched()
    {
        var vm = new NetworkOpsCommandCenterViewModel(NetworkOpsClient.Unconfigured("test"));
        vm.ApplyPower(new PowerSnapshot([], "Off", "the UPS watcher is not configured on this service", null, false, []), Now);
        Assert.Equal("Not watched", vm.PowerHeadline);
        Assert.Empty(vm.Ups);
    }
}
