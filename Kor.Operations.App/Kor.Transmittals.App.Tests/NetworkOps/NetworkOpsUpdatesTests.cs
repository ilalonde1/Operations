#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.App.NetworkOps;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Learning;
using Kor.Operations.NetworkOps.Core.Updates;
using Xunit;

namespace Kor.Operations.App.Tests.NetworkOps;

/// <summary>
/// The Updates view's rows, from rows as the service sends them.
///
/// WHAT IT COVERS: what each row says it is (overdue, due, held, optional, up to date, can't reach, search failed);
/// a machine NetworkOps cannot reach can never be ticked; the waiting count separates security from other; the
/// domain controller and APP01 say what is special about them.
/// WHAT IT DOES NOT: the service's batch rules (UpdateTests in the NetworkOps tests), the install itself, or the
/// rendering (NetworkOpsWindowsRenderTests draws it). A SAME-CLASS FAULT IT WOULD NOT CATCH: a row the service marks
/// due for the wrong reason reads "Due" here just the same.
/// </summary>
public sealed class NetworkOpsUpdatesTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 17, 0, 0, DateTimeKind.Utc);

    private static PendingUpdate P(string kb, bool security, int daysOld, string? sev = "Important")
        => new(kb, $"2026-09 Security Update (KB{kb})", security ? "Security Updates" : "Updates", security, sev, Now.AddDays(-daysOld).Date, 100, false, true);

    public static IReadOnlyList<UpdateRow> Rows() =>
    [
        new(1, "KOR-1001", "Workstation", false, true, null, null, "ilalonde · active", "Active", Now.AddHours(-4), "Ok", true,
            [P("5126104", true, 23), P("5129195", true, 17, null)], Severity.Critical, "Security updates are due", null, null),
        new(2, "KOR-104N", "Workstation", false, true, null, null, "nobody signed in", "Nobody", Now.AddHours(-4), "Ok", false,
            [P("5129195", true, 5), P("890830", false, 5)], Severity.Warning, "Security updates are due", "Done: Installed 3 of 3; no restart needed.", Now.AddDays(-20)),
        new(3, "KOR-202", "Workstation", false, true, null, null, "jmarkulin · locked", "Locked", Now.AddHours(-4), "Ok", false,
            [P("5130001", true, 1)], Severity.Info, "New security updates (held 3 days)", null, null),
        new(4, "KOR-205", "Workstation", false, true, null, null, null, null, Now.AddHours(-4), "Offline: port 445 not answering", false, [], null, null, null, null),
        new(5, "KOR-DC01 (domain controller, DNS, DHCP)", "Server", true, true, null, "Alone", null, null, Now.AddHours(-4), "Ok", false,
            [P("5129000", true, 23)], Severity.Critical, "Security updates are due", null, null),
        new(6, "KOR-APP01 (apps, SQL, NetworkOps)", "Server", true, true, null, "NoRestart", null, null, Now.AddHours(-4), "Ok", false, [], null, null, null, null),
        new(7, "Veeam backups (BK01)", "Backup", true, false, "APP01 cannot run anything on it (SMB/445 is closed to it)", null, null, null, null, null, false, [], null, null, null, null),
    ];

    private static UpdateRowView V(int id) => new(Rows().Single(r => r.DeviceId == id), Now);

    [Fact]
    public void Each_row_says_what_it_is()
    {
        Assert.Equal("Overdue", V(1).DueText);
        Assert.Equal("Due", V(2).DueText);
        Assert.Equal("New (held)", V(3).DueText);
        Assert.Equal("Search failed", V(4).DueText);
        Assert.Equal("Up to date", V(6).DueText);
        Assert.Equal("Can't reach", V(7).DueText);
        Assert.Equal("2 security, 0 other · restart pending", V(1).WaitingText);
        Assert.Equal("1 security, 1 other", V(2).WaitingText);
    }

    [Fact]
    public void A_machine_NetworkOps_cannot_reach_can_never_be_ticked()
    {
        var bk = V(7);
        bk.IsTicked = true;
        Assert.False(bk.IsTicked);
        Assert.Contains("SMB/445", bk.InstallText);
    }

    [Fact]
    public void The_domain_controller_and_APP01_say_what_is_special_about_them()
    {
        Assert.Equal("Server · patch on its own", V(5).KindText);
        Assert.Equal("Server · never restarted from here", V(6).KindText);
        Assert.Equal("PC", V(1).KindText);
    }

    [Fact]
    public void Progress_replaces_the_last_install_while_an_install_runs()
    {
        var v = V(2);
        Assert.StartsWith("last: Done: Installed 3 of 3", v.InstallText);
        v.Progress = "installing…";
        Assert.Equal("installing…", v.InstallText);
    }
}
