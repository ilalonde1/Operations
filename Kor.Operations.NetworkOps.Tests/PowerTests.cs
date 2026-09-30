#nullable enable
using Kor.Operations.NetworkOps.Core.Power;
using Kor.Operations.NetworkOps.Service;
using Kor.Operations.NetworkOps.Service.Power;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The UPS watcher and the rack shutdown chain.
//
// WHAT THIS COVERS: reading both MIBs (from values the two cards actually returned on 2026-09-29); the
// trigger policy, above all that it never shuts down a rack that still has mains (one UPS on battery,
// a missed poll, a blind card); the plan against the live inventory as verified on 2026-09-29 and
// against the configuration AS SHIPPED; the contract of the script that runs on the host.
//
// WHAT IT DOES NOT COVER: that hostd honours a forced ShutdownHost, that DSM's shutdown call is accepted,
// or the real timings -- only a live pull-the-plug test proves those, and the chain stays unarmed until
// it has. The daily PowerRehearsal (a dry run against the live rack) covers "does the plan still match
// the rack" and "can every step be taken". A fault this would NOT catch: a UPS card that reports "on
// mains" while its output is actually dead.
public sealed class PowerTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 20, 0, 0, DateTimeKind.Utc);
    private static readonly PowerPolicySettings Defaults = new();

    // ---------------------------------------------------------------- MIBs

    [Fact]
    public void Eaton_values_read_on_2026_09_29_parse_as_mains_with_29_minutes()
    {
        var v = new Dictionary<string, string>
        {
            [UpsMibs.UpsOutputSource] = "3", [UpsMibs.UpsBatteryStatus] = "2", [UpsMibs.UpsSecondsOnBattery] = "0",
            [UpsMibs.UpsMinutesRemaining] = "29", [UpsMibs.UpsChargeRemaining] = "100", [UpsMibs.UpsOutputLoad] = "31",
        };
        var r = UpsMibs.Parse("Eaton 5PX", UpsMibs.UpsMib, Now, v);
        Assert.Equal(PowerSource.Mains, r.Source);
        Assert.Equal(29, r.MinutesRemaining);
        Assert.Equal(100, r.ChargePercent);
        Assert.Equal(31, r.LoadPercent);
        Assert.False(r.BatteryLow);
        Assert.Null(r.Error);
    }

    [Fact]
    public void Apc_powernet_timeticks_become_minutes_and_status_codes_map()
    {
        var v = new Dictionary<string, string>
        {
            [UpsMibs.ApcOutputStatus] = "3", [UpsMibs.ApcBatteryStatus] = "3", [UpsMibs.ApcTimeOnBattery] = "36000",   // 6 min
            [UpsMibs.ApcRuntimeRemaining] = "220800", [UpsMibs.ApcCapacity] = "80", [UpsMibs.ApcReplaceIndicator] = "2", [UpsMibs.ApcOutputLoad] = "31",
        };
        var r = UpsMibs.Parse("APC SRT1500", UpsMibs.PowerNet, Now, v);
        Assert.Equal(PowerSource.Battery, r.Source);
        Assert.Equal(360, r.SecondsOnBattery);
        Assert.Equal(36, r.MinutesRemaining);   // 36 min 48 s, as the card's own CLI showed
        Assert.True(r.BatteryLow);
        Assert.True(r.ReplaceBattery);
    }

    [Fact]
    public void A_card_missing_values_says_so_instead_of_inventing_them()
    {
        var r = UpsMibs.Parse("Eaton 5PX", UpsMibs.UpsMib, Now, new Dictionary<string, string> { [UpsMibs.UpsOutputSource] = "3" });
        Assert.Equal(PowerSource.Mains, r.Source);
        Assert.Null(r.MinutesRemaining);
        Assert.Equal("5 of 6 values missing", r.Error);
    }

    // ---------------------------------------------------------------- policy

    private static UpsView View(string ups, PowerSource source, int onBatterySeconds = 0, int minutesLeft = 29, bool low = false, int ageSeconds = 5, bool reachable = true)
        => new(new UpsReading(ups, Now.AddSeconds(-ageSeconds), reachable, source, onBatterySeconds, minutesLeft, 100, 31, low, false, null),
               source == PowerSource.Battery ? Now.AddSeconds(-onBatterySeconds) : null);

    [Fact]
    public void Both_on_mains_is_normal()
        => Assert.Equal(PowerLevel.Normal, PowerPolicy.Evaluate([View("E", PowerSource.Mains), View("A", PowerSource.Mains)], Now, Defaults).Level);

    [Fact]
    public void One_UPS_on_battery_while_the_other_carries_the_rack_is_never_a_shutdown()
    {
        // The Eaton's circuit is dead for 20 minutes and it is nearly flat, but the APC has mains: every
        // dual-corded device is fed. Shutting down here would CAUSE the outage.
        var v = PowerPolicy.Evaluate([View("E", PowerSource.Battery, 1200, minutesLeft: 3, low: true), View("A", PowerSource.Mains)], Now, Defaults);
        Assert.Equal(PowerLevel.Degraded, v.Level);
        Assert.Contains("carrying the rack", v.Reason);
    }

    [Theory]
    [InlineData(4 * 60, PowerLevel.Degraded)]
    [InlineData(5 * 60, PowerLevel.Trigger)]
    public void Both_on_battery_triggers_at_the_on_battery_limit(int seconds, PowerLevel expected)
        => Assert.Equal(expected, PowerPolicy.Evaluate([View("E", PowerSource.Battery, seconds), View("A", PowerSource.Battery, seconds)], Now, Defaults).Level);

    [Fact]
    public void Both_on_battery_with_a_short_runtime_triggers_at_once()
        => Assert.Equal(PowerLevel.Trigger, PowerPolicy.Evaluate([View("E", PowerSource.Battery, 30, minutesLeft: 12), View("A", PowerSource.Battery, 30)], Now, Defaults).Level);

    [Fact]
    public void Both_on_battery_with_a_low_battery_triggers_at_once()
        => Assert.Equal(PowerLevel.Trigger, PowerPolicy.Evaluate([View("E", PowerSource.Battery, 30), View("A", PowerSource.Battery, 30, low: true)], Now, Defaults).Level);

    [Fact]
    public void Two_blind_cards_never_shut_the_rack_down()
        => Assert.Equal(PowerLevel.Degraded, PowerPolicy.Evaluate([View("E", PowerSource.Mains, ageSeconds: 600), View("A", PowerSource.Mains, ageSeconds: 600)], Now, Defaults).Level);

    [Fact]
    public void On_battery_beside_a_blind_card_waits_for_concrete_danger_not_the_clock()
    {
        // The APC card is rebooting (blind 3 min) -- its UPS may well be carrying the rack.
        var blindApc = View("A", PowerSource.Mains, ageSeconds: 180);
        Assert.Equal(PowerLevel.Degraded, PowerPolicy.Evaluate([View("E", PowerSource.Battery, 20 * 60), blindApc], Now, Defaults).Level);
        Assert.Equal(PowerLevel.Trigger, PowerPolicy.Evaluate([View("E", PowerSource.Battery, 20 * 60, minutesLeft: 10), blindApc], Now, Defaults).Level);
    }

    [Fact]
    public void The_cards_own_counter_carries_the_clock_over_a_watcher_restart()
    {
        // Just restarted: we have seen battery for 10 s, but the cards say 6 minutes.
        var e = new UpsView(new UpsReading("E", Now, true, PowerSource.Battery, 360, 20, 80, 31, false, false, null), Now.AddSeconds(-10));
        var a = new UpsView(new UpsReading("A", Now, true, PowerSource.Battery, 360, 25, 80, 31, false, false, null), Now.AddSeconds(-10));
        Assert.Equal(PowerLevel.Trigger, PowerPolicy.Evaluate([e, a], Now, Defaults).Level);
    }

    [Fact]
    public void A_missed_poll_of_the_UPS_on_mains_does_not_make_it_blind()
    {
        // THE failure this guards: Eaton on battery 6 min (its circuit), APC carrying on mains; one APC poll
        // is lost. If that read as "APC blind" the rack would be shut down with mains present.
        var s = new PowerState();
        var t0 = Now.AddSeconds(-30);
        s.Update(new UpsReading("APC", t0, true, PowerSource.Mains, 0, 36, 100, 31, false, false, null), ".101", t0);
        s.Update(new UpsReading("Eaton", t0, true, PowerSource.Battery, 330, 20, 80, 31, false, false, null), ".44", t0);
        s.Update(new UpsReading("Eaton", Now, true, PowerSource.Battery, 360, 20, 80, 31, false, false, null), ".44", Now);
        var changed = s.Update(UpsReading.Unreachable("APC", Now, "timeout"), ".101", Now);

        Assert.True(changed);   // answering -> silent is recorded
        Assert.Equal(PowerLevel.Degraded, PowerPolicy.Evaluate(s.Views(), Now, Defaults).Level);
        Assert.False(s.Snapshot(false, []).Ups.Single(u => u.Name == "APC").Reachable);   // the page shows it silent
    }

    [Fact]
    public void The_on_battery_clock_is_not_reset_by_a_silent_poll()
    {
        var s = new PowerState();
        var t0 = Now.AddMinutes(-6);
        s.Update(new UpsReading("E", t0, true, PowerSource.Battery, null, 20, 80, 31, false, false, null), ".44", t0);
        s.Update(UpsReading.Unreachable("E", Now.AddMinutes(-1), "timeout"), ".44", Now.AddMinutes(-1));
        s.Update(new UpsReading("E", Now, true, PowerSource.Battery, null, 18, 70, 31, false, false, null), ".44", Now);
        Assert.Equal(t0, s.Views().Single().ObservedOnBatterySinceUtc);
    }

    // ---------------------------------------------------------------- plan

    /// <summary>Both hosts as vim-cmd reported them on 2026-09-29 (esxi-shutdown-inventory.ps1).</summary>
    private static readonly IReadOnlyList<VmOnHost> Live20260929 =
    [
        new("192.168.1.10", 1, "vcenter", true), new("192.168.1.10", 11, "Kor-DC01", true), new("192.168.1.10", 12, "Kor-FS01", true),
        new("192.168.1.10", 14, "Kor-APP01", true), new("192.168.1.10", 17, "Kor-RDS01", true),
        new("192.168.1.10", 2, "vCLS-97bf60c5-12a5-49a5-bbf1-1e70c261e918", false),
        new("192.168.1.16", 1, "vCLS-a54c6c75-5601-4d7c-876c-b8314670b0cc", false), new("192.168.1.16", 11, "Kor-Lab01_proxy", false),
        new("192.168.1.16", 2, "Kor-BK01", true), new("192.168.1.16", 22, "KOR-UNIFI01", true),
    ];

    internal static NetworkOpsOptions Shipped()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Kor.Operations.NetworkOps.Service", "appsettings.json"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var cfg = new ConfigurationBuilder().AddJsonFile(Path.Combine(dir!.FullName, "Kor.Operations.NetworkOps.Service", "appsettings.json")).Build();
        var o = new NetworkOpsOptions();
        cfg.Bind(o);
        return o;
    }

    [Fact]
    public void The_shipped_plan_accounts_for_every_VM_running_on_2026_09_29()
        => Assert.Empty(ShutdownPlan.Problems(Shipped().PowerChain, Live20260929));

    [Fact]
    public void The_shipped_plan_runs_waves_then_the_other_host_then_hands_off_to_APP01s_host()
    {
        var plan = ShutdownPlan.Build(Shipped().PowerChain, Live20260929);
        var s = plan.Steps;
        Assert.Equal([ChainStepKind.GuestShutdown, ChainStepKind.GuestShutdown, ChainStepKind.GuestShutdown, ChainStepKind.HostPowerOff, ChainStepKind.Handoff], s.Select(x => x.Kind));
        Assert.Equal(["Kor-RDS01", "Kor-FS01", "KOR-UNIFI01", "Kor-BK01"], s[0].Vms.Select(v => v.Vm));
        Assert.Equal("Kor-DC01", s[1].Vms.Single().Vm);
        Assert.Equal("vcenter", s[2].Vms.Single().Vm);
        Assert.Equal("192.168.1.16", s[3].Host);
        Assert.Equal("192.168.1.10", s[4].Host);
        Assert.Equal("Kor-APP01", s[4].Vms.Single().Vm);
        // Never touched: the vCLS agents; never "shut down": a VM that is already off.
        Assert.DoesNotContain(s.SelectMany(x => x.Vms), v => v.Vm.StartsWith("vCLS-") || v.Vm == "Kor-Lab01_proxy");
    }

    [Fact]
    public void The_shipped_storage_order_puts_the_SAN_last_and_shuts_it_as_one_cluster()
    {
        var st = Shipped().PowerChain.Storage;
        Assert.True(st[^1].HoldsTheVms);
        Assert.True(st[^1].DualController);
        Assert.Single(st, x => x.HoldsTheVms);
        Assert.All(st, x => Assert.StartsWith("KOR_NETWORKOPS_", x.PasswordVariable));   // the service loads only this prefix from the machine store
    }

    [Fact]
    public void The_shipped_configuration_is_disarmed_and_pins_both_hosts()
    {
        var o = Shipped();
        Assert.False(o.PowerChainArmed);   // arming follows the live test, as a deliberate change
        Assert.All(o.PowerChain.Hosts, h => Assert.True(o.EsxiHostKeys.TryGetValue(h, out var pins) && pins.Count > 0, $"{h} has no pinned host key"));
        Assert.Equal(2, o.Ups.Count);
    }

    [Fact]
    public void A_new_VM_is_reported_as_drift_and_still_gets_a_clean_shutdown_first()
    {
        var drifted = Live20260929.Append(new VmOnHost("192.168.1.16", 30, "Kor-NEW01", true)).ToList();
        var o = Shipped().PowerChain;
        Assert.Contains(ShutdownPlan.Problems(o, drifted), p => p.Contains("Kor-NEW01") && p.Contains("in no wave"));
        var first = ShutdownPlan.Build(o, drifted).Steps[0];
        Assert.Equal(ChainStepKind.GuestShutdown, first.Kind);
        Assert.Equal("Kor-NEW01", first.Vms.Single().Vm);
    }

    [Fact]
    public void If_APP01_has_moved_host_the_chain_follows_it_instead_of_killing_itself_early()
    {
        var moved = Live20260929.Select(v => v.Vm == "Kor-APP01" ? v with { Host = "192.168.1.16" } : v).ToList();
        var o = Shipped().PowerChain;
        Assert.Contains(ShutdownPlan.Problems(o, moved), p => p.Contains("not on 192.168.1.10"));
        var s = ShutdownPlan.Build(o, moved).Steps;
        Assert.Equal("192.168.1.10", s.Single(x => x.Kind == ChainStepKind.HostPowerOff).Host);
        Assert.Equal("192.168.1.16", s[^1].Host);
    }

    [Fact]
    public void A_renamed_VM_and_a_SAN_not_last_are_both_reported()
    {
        var o = Shipped().PowerChain;
        var renamed = Live20260929.Select(v => v.Vm == "Kor-DC01" ? v with { Vm = "Kor-DC02" } : v).ToList();
        var problems = ShutdownPlan.Problems(o, renamed);
        Assert.Contains(problems, p => p.Contains("Kor-DC01") && p.Contains("renamed"));
        Assert.Contains(problems, p => p.Contains("Kor-DC02") && p.Contains("in no wave"));

        o.Storage.Reverse();
        Assert.Contains(ShutdownPlan.Problems(o, Live20260929), p => p.Contains("must be the LAST"));
    }

    [Fact]
    public void Inventory_parses_vim_cmd_lines_and_skips_noise()
    {
        var vms = EsxiCommands.ParseInventory("h", "14|Kor-APP01|Powered on\n2|vCLS-x|Powered off\n\nSkipping invalid VM '9'\n");
        Assert.Equal(2, vms.Count);
        Assert.True(vms[0].PoweredOn);
        Assert.Equal(14, vms[0].VmId);
        Assert.False(vms[1].PoweredOn);
    }

    // ---------------------------------------------------------------- the host script

    [Fact]
    public void The_host_script_goes_through_hostd_and_shuts_the_SAN_as_a_cluster()
    {
        var py = HostScript.Text;
        Assert.Contains("ShutdownHost_Task(force=True)", py);
        Assert.Contains("AcquireLocalTicket", py);
        Assert.Contains("\"node0,node1\"", py);
        Assert.Contains("suspend_taking_over", py);
        Assert.Contains(HostScript.DoneMarker, py);
        Assert.Contains("set_firewall(True)", py);   // the firewall is always put back
        Assert.Contains("os.remove(sys.argv[1])", py);   // the config (passwords) is deleted on read
        // The traps found on 2026-09-29: shutdown.sh is an empty stub; logger, nohup, setsid do not exist on ESXi.
        foreach (var bad in new[] { "shutdown.sh", "\"logger\"", "nohup", "busybox poweroff", "esxcli system shutdown" })
            Assert.DoesNotContain(bad, py.Split('\n').Where(l => !l.TrimStart().StartsWith('#')).Aggregate("", (a, l) => a + l + "\n"));
    }

    [Fact]
    public void Passwords_travel_in_the_config_on_stdin_never_on_a_command_line()
    {
        var t = new StorageTarget { Name = "SAN", Address = "192.168.1.12", Account = "admin", PasswordVariable = "KOR_NETWORKOPS_X", HoldsTheVms = true, DualController = true };
        var config = HostScript.Config(true, "Kor-APP01", 240, true, [(t, "s3cret-value")]);
        Assert.Contains("s3cret-value", config);
        Assert.Contains("\"dualController\":true", config);
        foreach (var command in new[] { HostScript.WriteStdinTo(HostScript.ConfigPath), HostScript.Launch, HostScript.ClearLog, HostScript.ReadLog })
            Assert.DoesNotContain("s3cret", command);
        Assert.Contains("0o600", HostScript.WriteStdinTo(HostScript.ConfigPath));
    }
}
