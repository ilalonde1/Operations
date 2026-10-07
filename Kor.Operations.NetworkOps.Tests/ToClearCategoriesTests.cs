#nullable enable
using System;
using System.Linq;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Learning;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The overview's category rollup (the Ninja "Device health issues" model Ian asked for): every open rule key falls into a
// broad, named category; the count is the DISTINCT machines; worst severity first.
// WHAT IT COVERS: the mapping of the real rule keys; distinct-machine counting; worst-first ordering; nothing hidden (an
// unmapped rule is "Other"). WHAT IT DOES NOT: how it looks -- the render test draws the strip.
public sealed class ToClearCategoriesTests
{
    private static ToClearIssue Issue(string rule, Severity sev, params string[] machines)
        => new(rule, rule, sev, "", DateTime.UtcNow, machines.Select((m, i) => new ToClearMachine(i + 1, m, i + 1, null, null)).ToList(), null);

    [Fact]
    public void Real_rule_keys_map_to_broad_categories()
    {
        Assert.Equal("Needs reboot", ToClearCategories.CategoryOf("reboot-overdue"));
        Assert.Equal("Low disk", ToClearCategories.CategoryOf("server.disk-full:C:"));
        Assert.Equal("Low disk", ToClearCategories.CategoryOf("low-disk:D"));
        Assert.Equal("Backups", ToClearCategories.CategoryOf("veeam.stale:Kor-FS01"));
        Assert.Equal("App crashes", ToClearCategories.CategoryOf("crash-loop:opushutil.exe"));
        Assert.Equal("Offline", ToClearCategories.CategoryOf("device-silent"));
        Assert.Equal("Failing storage", ToClearCategories.CategoryOf("disk-failing"));
        Assert.Equal("End of support", ToClearCategories.CategoryOf("version.end-of-support"));
        Assert.Equal("Other", ToClearCategories.CategoryOf("some-unmapped-rule"));
    }

    [Fact]
    public void Machines_are_counted_distinct_and_categories_are_worst_first()
    {
        var issues = new[]
        {
            Issue("reboot-overdue", Severity.Warning, "PC1", "PC2", "PC3"),
            Issue("low-disk:C", Severity.Critical, "PC1"),   // the SAME PC is also low on disk
            Issue("low-disk:D", Severity.Warning, "PC1"),    // two low-disk rules on PC1 -> one machine, two issues
            Issue("veeam.stale:x", Severity.Critical, "BK01"),
        };
        var cats = ToClearCategories.Of(issues);
        Assert.Equal(["Low disk", "Backups", "Needs reboot"], cats.Select(c => c.Name));   // Critical before Warning
        var lowDisk = cats.Single(c => c.Name == "Low disk");
        Assert.Equal(1, lowDisk.Machines);   // PC1 counted once, not per drive
        Assert.Equal(2, lowDisk.Issues);
        Assert.Equal(3, cats.Single(c => c.Name == "Needs reboot").Machines);
    }
}
