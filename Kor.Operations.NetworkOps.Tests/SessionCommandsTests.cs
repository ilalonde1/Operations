#nullable enable
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Learning;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The `netops` commands that replaced the night of 2026-10-01/02's hand work (Ian: "ALL THIS MUST BE CODE AND DB BASED. NO
// BULLSHIT SHORTCUTS"): a throwaway API reader for actions, history and triggers; a server's updates installed by posting to
// the API by hand because `netops fix` resolved PCs only; the morning brief as a diff of two JSON dumps.
//
// WHAT IT COVERS: --hosts naming PCs, rack devices (by the name before the bracket), "all", "rack", and unknowns; --since;
// the brief's words for new, came-and-went, cleared and done, and its counts; every new verb being one that goes through
// APP01 (the AskAndKnowledgeTests gate lists them).
// WHAT IT DOES NOT: the HTTP round trips (each verb was run live against APP01 on 2026-10-02), or GET /api/changes's SQL --
// read live the same day against the night's own changes.
// A SAME-CLASS FAULT IT WOULD NOT CATCH: a NEW piece of hand work next time -- this file only proves the five that were found.
public sealed class SessionCommandsTests
{
    private static readonly DeviceRow Pc = new(1, "KOR-217", null, null);
    private static readonly DeviceRow Pc2 = new(2, "KOR-208-N", null, null);
    private static readonly DeviceRow Fs01 = new(53, "KOR-FS01 (file server)", null, null, Kind: "Server");
    private static readonly DeviceRow Dc01 = new(54, "KOR-DC01 (domain controller, DNS, DHCP)", null, null, Kind: "Server");

    [Fact]
    public void Hosts_name_PCs_and_rack_devices_alike()
    {
        var got = HostNames.Resolve([Pc, Pc2], [Fs01, Dc01], "kor-217, KOR-FS01, KOR-NOPE", out var unknown);
        Assert.Equal(["KOR-217", "KOR-FS01 (file server)"], got.Select(d => d.Name));
        Assert.Equal(["KOR-NOPE"], unknown);
    }

    [Fact]
    public void All_is_every_PC_and_rack_is_every_rack_device()
    {
        Assert.Equal([1, 2], HostNames.Resolve([Pc, Pc2], [Fs01], "all", out _).Select(d => d.DeviceId).Order());
        Assert.Equal([53, 54], HostNames.Resolve([Pc], [Fs01, Dc01], "rack", out _).Select(d => d.DeviceId).Order());
        Assert.Single(HostNames.Resolve([Pc], [Fs01], "KOR-217,kor-217", out _));                       // named twice, once
        Assert.Empty(HostNames.Resolve([Pc], [Fs01], "KOR-FS", out var u));                             // a prefix is not a name
        Assert.Equal(["KOR-FS"], u);
    }

    [Theory]
    [InlineData("12h", 12)]
    [InlineData("3d", 72)]
    [InlineData(" 1H ", 1)]
    [InlineData(null, 24)]
    public void Since_reads_hours_and_days_back(string? since, int hours)
    {
        var now = new DateTime(2026, 10, 2, 20, 0, 0, DateTimeKind.Utc);
        Assert.Equal(now.AddHours(-hours), HostNames.ParseSince(since, now));
    }

    [Fact]
    public void Since_reads_a_local_date_and_refuses_nonsense()
    {
        var now = DateTime.UtcNow;
        Assert.Equal(new DateTime(2026, 10, 2, 1, 45, 0, DateTimeKind.Local).ToUniversalTime(), HostNames.ParseSince("2026-10-02T01:45", now));
        Assert.Null(HostNames.ParseSince("yesterday-ish", now));
    }

    [Fact]
    public void The_brief_says_what_is_new_what_cleared_and_what_was_done()
    {
        var since = new DateTime(2026, 10, 2, 8, 45, 0, DateTimeKind.Utc);
        var view = new ChangesView(since, since.AddHours(9),
            Opened:
            [
                new("KOR-101", "bios-behind", Severity.Info, "A newer BIOS is available", "S08KT5EA -> S08KT62A", since.AddHours(1), null),
                new("KOR-208-N", "disk-errors:st2000dm006-2dm164", Severity.Critical, "The data drive D: has unrecoverable read errors", "2 TB hard drive", since.AddHours(2), null),
                new("KOR-207", "probe-incomplete", Severity.Info, "Part of the health check couldn't read", "session", since.AddHours(1), since.AddHours(3)),
            ],
            Cleared: [new("KOR-305", "updates-due", Severity.Critical, "Security updates are due", "4 security", since.AddDays(-1), since.AddHours(1))],
            Actions:
            [
                new("KOR-305", 174, "install-updates", "ilalonde", since.AddMinutes(5), "Done", "Installed 4 of 4"),
                new("KOR-207", 172, "install-updates", "ilalonde", since.AddMinutes(5), "Done", "Installed 18 of 18"),
                new("KOR-FS01 (file server)", 186, "install-updates", "ilalonde", since.AddMinutes(20), "Failed", "cut off by a restart"),
            ],
            OpenCritical: 8, OpenWarning: 39, OpenInfo: 69);

        var brief = view.Brief();
        Assert.Contains("3 new, 1 cleared, 3 fixes or runs", brief);
        Assert.Contains("Open now: 8 critical, 39 need attention, 69 info.", brief);
        Assert.True(brief.IndexOf("[CRITICAL] KOR-208-N", StringComparison.Ordinal) < brief.IndexOf("[info] KOR-101", StringComparison.Ordinal), "worst first");
        Assert.Contains("Came and went:\n  KOR-207: Part of the health check couldn't read".Replace("\n", Environment.NewLine), brief);
        Assert.Contains("KOR-305: Security updates are due (open since", brief);
        Assert.Contains("install-updates Done: 2 -- KOR-207, KOR-305", brief);
        Assert.Contains("! KOR-FS01 (file server) install-updates Failed: cut off by a restart", brief);
    }

    [Fact]
    public void The_SQL_setup_reader_is_offered_where_a_SQL_update_fails_and_changes_nothing()
    {
        var f = Kor.Operations.NetworkOps.Core.Actions.FixCatalog.Get("read-sql-setup-logs")!;
        Assert.False(f.Disruptive);
        Assert.Contains(f, Kor.Operations.NetworkOps.Core.Actions.FixCatalog.For(Kor.Operations.NetworkOps.Core.Updates.UpdateRules.Rule));
        var script = Kor.Operations.NetworkOps.Core.Actions.FixCatalog.Script(f, null);
        Assert.DoesNotMatch(new System.Text.RegularExpressions.Regex(@"\b(Set-|Remove-|New-Item|Start-Process|Stop-|Restart-)", System.Text.RegularExpressions.RegexOptions.IgnoreCase), script);
    }
}
