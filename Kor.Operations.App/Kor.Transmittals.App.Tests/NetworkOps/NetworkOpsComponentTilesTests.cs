#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kor.Operations.App.NetworkOps;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Learning;
using Xunit;

namespace Kor.Operations.App.Tests.NetworkOps;

// The "This PC" strip in a PC's window, drawn from KOR-208-N's real probe-v10 check (the fixture NetworkOpsTests keeps).
//
// WHAT IT COVERS: the window shows tiles once it has a check and the text line otherwise; a tile is coloured by the live
// finding about its part and not by an acknowledged one; clicking a tile selects that finding.
// WHAT IT DOES NOT: which part a finding belongs to (PcComponentsTests), or how the strip looks (the render test).
// A SAME-CLASS FAULT IT WOULD NOT CATCH: the last-check call failing in production (a 403 for a non-admin) -- the page then
// quietly shows the text line, which is the intended fallback, so nothing here can tell it from "never checked".
public sealed class NetworkOpsComponentTilesTests
{
    internal static HealthSnapshot Kor208NCheck()
        => HealthSnapshot.Parse(File.ReadAllText(Path.Combine(XamlStaticResourceOrderTests.GetRepoRoot(),
            "Kor.Operations.NetworkOps.Tests", "Fixtures", "health", "KOR-208-N-v10-2026-10-02.json")));

    private static NetworkOpsDeviceViewModel Window(DateTime? acknowledged = null)
    {
        var now = DateTime.UtcNow;
        var dev = new DeviceRow(4, "KOR-208-N", now.AddMinutes(-2), now.AddMinutes(-2));
        var open = new List<FleetFinding>
        {
            new(20, "KOR-208-N", "disk-errors:st2000dm006-2dm164", Severity.Critical, "The data drive D: has unrecoverable read errors", "e",
                now.AddDays(-3), now, acknowledged, acknowledged is null ? null : "ian", null, null),
            new(21, "KOR-208-N", "crash-loop:opushutil.exe", Severity.Warning, "opushutil.exe keeps crashing", "e", now.AddDays(-4), now, null, null, null, null),
        };
        var snap = new FleetSnapshot([dev], new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase), open, [], null);
        return new NetworkOpsDeviceViewModel(NetworkOpsClient.Unconfigured("test"), snap, dev);
    }

    [Fact]
    public void Tiles_replace_the_text_line_once_there_is_a_check()
    {
        var vm = Window();
        Assert.False(vm.HasComponents);
        Assert.True(vm.ShowsHardwareLine);
        vm.SetLastCheck(Kor208NCheck());
        Assert.True(vm.HasComponents);
        Assert.False(vm.ShowsHardwareLine);
        Assert.Equal(7, vm.Components.Count);
    }

    [Fact]
    public void The_failing_drive_is_the_red_tile_and_clicking_it_shows_its_finding()
    {
        var vm = Window();
        vm.SetLastCheck(Kor208NCheck());
        var d = vm.Components.Single(t => t.Title.StartsWith("D:", StringComparison.Ordinal));
        Assert.True(d.HasProblem);
        Assert.Contains("unrecoverable", d.ToolTip);
        Assert.False(vm.Components.Single(t => t.Part.IsSystem).HasProblem);

        vm.SelectedFinding = vm.OpenFindings.Single(r => r.Finding.RuleKey.StartsWith("crash-loop", StringComparison.Ordinal));
        vm.SelectFindingFor(d);
        Assert.Equal("disk-errors:st2000dm006-2dm164", vm.SelectedFinding!.Finding.RuleKey);
    }

    // Ian, 2026-10-02: "why don't the tiles click anywhere? ... what does clicking on it allow?"
    [Fact]
    public void A_healthy_tile_shows_the_part_and_a_tile_with_a_finding_shows_the_finding_then_the_part()
    {
        var vm = Window();
        vm.SetLastCheck(Kor208NCheck());
        Assert.True(vm.ShowsExplanation);

        var cpu = vm.Components.Single(t => t.Part.Kind == "cpu");
        vm.ClickTile(cpu);
        Assert.Same(cpu, vm.SelectedPart);
        Assert.True(vm.ShowsPart);
        Assert.False(vm.ShowsExplanation);
        Assert.True(cpu.IsSelected);
        Assert.Contains(cpu.Info, r => r.Label == "Processor");

        var d = vm.Components.Single(t => t.Title.StartsWith("D:", StringComparison.Ordinal));
        vm.ClickTile(d);                                                          // what is wrong first
        Assert.Null(vm.SelectedPart);
        Assert.False(cpu.IsSelected);
        Assert.Equal("disk-errors:st2000dm006-2dm164", vm.SelectedFinding!.Finding.RuleKey);
        vm.ClickTile(d);                                                          // again: the drive itself
        Assert.Same(d, vm.SelectedPart);
        Assert.Contains(d.Info, r => r.Label == "Model" && r.Value == "ST2000DM006-2DM164");
        Assert.StartsWith("1 open finding", vm.SelectedPartFindings);

        vm.SelectedFinding = vm.OpenFindings.Single(r => r.Finding.RuleKey.StartsWith("crash-loop", StringComparison.Ordinal));
        Assert.Null(vm.SelectedPart);                                             // picking a finding brings its explanation back
        Assert.True(vm.ShowsExplanation);
    }

    [Fact]
    public void The_part_showing_survives_a_refresh()
    {
        var vm = Window();
        vm.SetLastCheck(Kor208NCheck());
        vm.ClickTile(vm.Components.Single(t => t.Part.Kind == "memory"));
        vm.SetLastCheck(Kor208NCheck());                                          // a new check rebuilds every tile
        Assert.Equal("memory", vm.SelectedPart!.Part.Kind);
        Assert.Same(vm.SelectedPart, vm.Components.Single(t => t.IsSelected));
    }

    [Fact]
    public void An_acknowledged_finding_does_not_colour_its_part()
    {
        var vm = Window(acknowledged: DateTime.UtcNow.AddHours(-1));
        vm.SetLastCheck(Kor208NCheck());
        Assert.False(vm.Components.Single(t => t.Title.StartsWith("D:", StringComparison.Ordinal)).HasProblem);
    }
}
