#nullable enable
using Kor.Operations.NetworkOps.Core.Updates;
using Kor.Operations.NetworkOps.Service;
using Kor.Operations.NetworkOps.Service.Agents;
using Kor.Operations.NetworkOps.Service.Mesh;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// What the Command Center says after a machine restarts. Ian, 2026-10-02: DC01, FS01 and RDS01 were patched by hand and
// restarted, and still read "security updates are due" 20 minutes later -- Windows Update was searched only at 08:00 and
// 13:00, a PC's restart was noticed only after 10 minutes away, and the install fix said "Done" before its own re-search.
//
// WHAT IT COVERS: the one rule (restarted since the last search -> search again), the boot times it is fed (server uptime,
// a PC's zone-less LastBoot), the agent telling a restart-length gap from a normal poll and from a long absence, that both
// sweeps that read a boot time feed the re-search queue, that an install's re-search comes BEFORE "Done", and that a
// device judged from MeshCentral is not called "not answering" before the service's first MeshCentral read.
// WHAT IT DOES NOT: a real restart end to end (watched live on 2026-10-02 instead), or how long Windows Update takes.
// A SAME-CLASS FAULT IT WOULD NOT CATCH: updates installed by hand WITHOUT a restart -- nothing restarts, so nothing
// re-searches; that machine's finding waits for the next scheduled search (it then says "restart pending" honestly).
public sealed class RestartFreshnessTests
{
    private static readonly DateTime Searched = new(2026, 10, 2, 7, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_restart_after_the_last_search_means_search_again()
    {
        Assert.True(UpdateRules.RestartedSinceSearch(Searched.AddMinutes(1), Searched));
        Assert.False(UpdateRules.RestartedSinceSearch(Searched.AddMinutes(-1), Searched));
        Assert.True(UpdateRules.RestartedSinceSearch(Searched, null));          // never searched
        Assert.False(UpdateRules.RestartedSinceSearch(null, Searched));         // boot unknown: claim nothing
        Assert.False(UpdateRules.RestartedSinceSearch(null, null));
    }

    [Fact]
    public void A_servers_boot_time_comes_from_the_uptime_it_reported()
    {
        var read = new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);
        Assert.Equal(read.AddMinutes(-30), UpdateRules.BootFromUptime(read, 0.5));
        Assert.Null(UpdateRules.BootFromUptime(read, null));
        Assert.Null(UpdateRules.BootFromUptime(read, -1));
    }

    [Fact]
    public void A_PCs_zoneless_LastBoot_is_read_in_the_offices_zone_and_never_in_the_future()
    {
        var pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        var read = new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);              // 02:00 PDT
        var bootLocal = new DateTime(2026, 10, 2, 1, 30, 0, DateTimeKind.Unspecified); // 01:30 PDT = 08:30 UTC
        Assert.Equal(new DateTime(2026, 10, 2, 8, 30, 0, DateTimeKind.Utc), UpdateRules.BootFromLocal(bootLocal, read, pacific));
        // A PC an hour ahead (EDMONTON-01) reads late, clamped to the read: one search more at worst, never one fewer.
        Assert.Equal(read, UpdateRules.BootFromLocal(new DateTime(2026, 10, 2, 2, 30, 0), read, pacific));
        Assert.Null(UpdateRules.BootFromLocal(null, read, pacific));
    }

    [Fact]
    public void The_agent_tells_a_restart_from_a_normal_poll_and_from_a_long_absence()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));
        var hub = new AgentHub(clock);
        Assert.True(hub.Seen("KOR-217", "1.0.2", null, "K")!.CameBack);                 // first poll this process sees

        clock.Advance(TimeSpan.FromSeconds(20));                                         // an ordinary poll
        var normal = hub.Seen("KOR-217", "1.0.2", null, "K")!;
        Assert.False(normal.CameBack); Assert.False(normal.Reconnected);

        clock.Advance(AgentHub.RestartGap + TimeSpan.FromSeconds(30));                   // a restart's silence
        var restart = hub.Seen("KOR-217", "1.0.2", null, "K")!;
        Assert.False(restart.CameBack); Assert.True(restart.Reconnected);

        clock.Advance(TimeSpan.FromMinutes(11));                                         // away for a while
        var away = hub.Seen("KOR-217", "1.0.2", null, "K")!;
        Assert.True(away.CameBack); Assert.False(away.Reconnected);
    }

    [Fact]
    public void A_reconnect_is_checked_even_right_after_a_fixs_own_check()
        // The check every fix queues lands minutes BEFORE a restart; it must not stand in for the check after it.
        => Assert.True(AgentApi.RecheckAfter <= TimeSpan.FromMinutes(1));

    [Fact]
    public void Both_sweeps_that_read_a_boot_time_feed_the_re_search_queue()
    {
        foreach (var file in new[] { @"Kor.Operations.NetworkOps.Service\Sweep\HealthSweeper.cs", @"Kor.Operations.NetworkOps.Service\Jobs\RackSweepJob.cs" })
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot(), file));
            Assert.True(text.Contains("rescans.ConsiderAsync(", StringComparison.Ordinal), $"{file} reads a boot time but never asks UpdateRescans");
        }
    }

    [Fact]
    public void An_install_is_marked_Done_only_after_its_re_search()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), @"Kor.Operations.NetworkOps.Service\Sweep\ActionRunner.cs"));
        var rescan = text.IndexOf("await updates.ScanAsync([a.DeviceId]", StringComparison.Ordinal);
        var done = text.IndexOf("await store.CompleteActionAsync(a.ActionId, true,", StringComparison.Ordinal);
        Assert.True(rescan > 0 && done > 0, "ActionRunner's install re-search or its Done line moved: re-point this test");
        Assert.True(rescan < done, "an install is marked Done before its re-search: Done would sit beside the finding it just cleared");
    }

    [Fact]
    public void Nothing_judged_from_MeshCentral_is_judged_before_the_first_MeshCentral_read()
    {
        var mesh = new MeshState(TimeProvider.System);
        Assert.False(mesh.Attempted);
        mesh.Failed("refused");
        Assert.True(mesh.Attempted);                     // a failed read is a read: "not answering" is then true
        Assert.True(new MeshState(TimeProvider.System) is var m && Read(m).Attempted);

        Assert.True(new RackDevice { Collector = "MeshServer" }.JudgedFromMesh);
        Assert.True(new RackDevice { Collector = "Mesh" }.JudgedFromMesh);
        Assert.False(new RackDevice { Collector = "WindowsServer" }.JudgedFromMesh);
        Assert.False(new RackDevice { Collector = "Ups" }.JudgedFromMesh);

        var sweep = File.ReadAllText(Path.Combine(RepoRoot(), @"Kor.Operations.NetworkOps.Service\Jobs\RackSweepJob.cs"));
        Assert.Contains("mesh.Attempted ? [] : named.Where(d => d.JudgedFromMesh)", sweep);

        static MeshState Read(MeshState s) { s.Update([], []); return s; }
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Directory.Build.props"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
