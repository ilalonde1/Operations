#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.App.NetworkOps;
using Kor.Operations.NetworkOps.Core.Learning;
using Xunit;

namespace Kor.Operations.App.Tests.NetworkOps;

/// <summary>
/// The Prompt Library window's list and runs, from a catalog as the service sends it.
///
/// WHAT IT COVERS: tools come first, then each device with its open findings under it; every line carries the request
/// that writes its prompt, and the request made from a device window matches its line (so it opens preselected); only
/// a proposed learning can be accepted or rejected.
/// WHAT IT DOES NOT: the rendering (NetworkOpsWindowsRenderTests draws it), the terminal launch, or the service.
/// A SAME-CLASS FAULT IT WOULD NOT CATCH: a catalog whose finding ids drifted from the fleet's would still list fine
/// and then fail on open with "not open any more" -- only opening one live shows the ids agree.
/// </summary>
public sealed class PromptLibraryTests
{
    public static PromptCatalog Catalog(FleetSnapshot fleet) => new(
        [new PromptTool("agent", "The endpoint agent", "How it calls in."), new PromptTool("remote-control", "Remote control (MeshCentral)", "KOR-MESH01.")],
        fleet.Devices.Select(d => new PromptSubject(d.DeviceId, d.Name, d.Kind,
            fleet.OpenOn(d.Name).Select(f => new PromptFinding(f.FindingId, f.RuleKey, f.Title, f.Severity)).ToList())).ToList(),
        true);

    public static IReadOnlyList<PromptRunRow> Runs() =>
    [
        new(43, "ask", "KOR-214: Andrea's ETABS crashed opening Tower B", "ilalonde@korstructural.com", new DateTime(2026, 10, 1, 21, 40, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 1, 21, 55, 0, DateTimeKind.Utc), "solved", "ETABS ran out of its graphics cache on a 2 GB model; raised the cache and reopened.",
            null, "proposed", "Andrea's ETABS crashed around 2:40 today opening the Tower B model", "ETABS closes while opening large models"),
        new(42, "finding", "KOR-302N: webroot:KorTools.dll", "ilalonde@korstructural.com", new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 30, 22, 30, 0, DateTimeKind.Utc), "solved", "Webroot override was per-site; made it global and re-checked.",
            "The override must be global, not per-site.", "proposed"),
        new(41, "tool", "agent", "ilalonde@korstructural.com", new DateTime(2026, 9, 30, 21, 0, 0, DateTimeKind.Utc), null, null, null, null, null),
    ];

    [Fact]
    public void Tools_come_first_then_each_device_with_its_findings_under_it()
    {
        var fleet = NetworkOpsViewModelTests.Fleet();
        var rows = PromptSubjectRow.From(Catalog(fleet));

        Assert.Equal("tool", rows[0].Request.Kind);
        Assert.Equal("tool", rows[1].Request.Kind);
        var firstDevice = rows.Skip(2).First();
        Assert.Equal("device", firstDevice.Request.Kind);
        Assert.False(firstDevice.IsChild);
        Assert.Equal(fleet.Devices.Count, rows.Count(r => r.Request.Kind == "device"));
        Assert.Equal(fleet.OpenFindings.Count, rows.Count(r => r.Request.Kind == "finding" && r.IsChild));
    }

    [Fact]
    public void The_request_from_a_device_window_matches_its_line()
    {
        var fleet = NetworkOpsViewModelTests.Fleet();
        var f = fleet.OpenFindings.First();
        var device = fleet.Devices.First(d => d.Name == f.Device);
        var rows = PromptSubjectRow.From(Catalog(fleet));
        Assert.Single(rows, r => r.Request == new PromptRequest("finding", null, device.DeviceId, f.FindingId));
        Assert.Single(rows, r => r.Request == new PromptRequest("device", null, device.DeviceId, null));
    }

    [Fact]
    public void Only_a_proposed_learning_awaits_a_decision()
    {
        var runs = Runs().Select(r => new PromptRunView(r)).ToList();
        Assert.True(runs[1].AwaitsDecision);
        Assert.False(runs[2].AwaitsDecision);
        Assert.Equal("no report yet", runs[2].OutcomeText);
        Assert.StartsWith("Learned (proposed):", runs[1].LearnedText);
    }

    [Fact]
    public void An_ask_shows_its_question_and_the_card_it_proposes()
    {
        var ask = new PromptRunView(Runs()[0]);
        Assert.True(ask.AwaitsDecision);   // a card with no learning still waits for Ian
        Assert.StartsWith("Asked: Andrea's ETABS crashed", ask.Summary);
        // Since the plain+technical card format (010): plain line first when there is one, then the technical card. This
        // fixture sets only the title, so it is the technical line alone.
        Assert.Equal("Knowledge card (proposed):\n  • Technical — ETABS closes while opening large models", ask.LearnedText);
    }
}
