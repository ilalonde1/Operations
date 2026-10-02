#nullable enable
using System.Text.RegularExpressions;
using Kor.Operations.NetworkOps.Service;
using Kor.Operations.NetworkOps.Service.Sweep;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// "Can APP01 run something on this rack device?" -- one answer, RackDevice.AppCanRunOn.
//
// The fault, 2026-10-01: five places answered it with two rules. The update search, the fix runner and a Claude session's
// reads took "WindowsServer or Mesh"; the Fix route and the remote-control install took "WindowsServer" only. So Windows
// updates were SEARCHED on KOR-RDS01 three times that day, and the Fix button refused to install them, saying RDS01 "is not
// a Windows machine".
//
// WHAT IT COVERS: the answer for a PC, each kind of Windows server, and a non-Windows rack device; that no service code
// outside AppCanRunOn (and the rack reader's own per-collector switch) compares a collector name to decide it.
// WHAT IT DOES NOT: whether APP01 really can reach a machine marked runnable (its account's rights, the firewall) -- only
// running something there proves that (netops run, 2026-10-01: FS01 and RDS01 as SYSTEM). A SAME-CLASS FAULT IT WOULD NOT
// CATCH: a new place that decides it without naming a collector at all (by Kind, say) -- the scan looks for collector names.
public sealed class RackRunnableTests
{
    private static NetworkOpsOptions Options() => new()
    {
        Rack =
        [
            new RackDevice { Name = "KOR-RDS01 (remote desktop)", Kind = "Server", Collector = "WindowsServer", Address = "KOR-RDS01" },
            new RackDevice { Name = "Remote-only box", Kind = "Server", Collector = "Mesh", Address = "BOX01" },
            new RackDevice { Name = "Eaton 5PX UPS", Kind = "UPS", Collector = "Ups", Address = "192.168.1.44" },
        ],
    };

    [Theory]
    [InlineData("KOR-217", "KOR-217")]                              // a PC: by its name
    [InlineData("KOR-RDS01 (remote desktop)", "KOR-RDS01")]         // a Windows server: by its address
    [InlineData("Remote-only box", "BOX01")]                        // remote-only, still Windows
    [InlineData("Eaton 5PX UPS", null)]                             // nothing to run on
    public void One_answer_for_where_APP01_can_run(string device, string? host)
        => Assert.Equal(host, ActionRunner.HostOf(Options(), device));

    [Fact]
    public void Nothing_else_decides_it_by_collector_name()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "Kor.Operations.NetworkOps.Service"))) root = root.Parent;
        Assert.NotNull(root);
        var service = Path.Combine(root!.FullName, "Kor.Operations.NetworkOps.Service");
        var allowed = new[] { "NetworkOpsOptions.cs", "RackCollector.cs" };   // the definition, and the reader that switches on collector to read
        var offenders = Directory.GetFiles(service, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => !allowed.Contains(Path.GetFileName(f)))
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, i, line)))
            .Where(x => Regex.IsMatch(x.line, @"Collector\s*(==|is)\s*""(WindowsServer|Mesh)"""))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}: {x.line.Trim()}")
            .ToList();
        Assert.True(offenders.Count == 0, "decide 'can APP01 run here' with RackDevice.AppCanRunOn, not a collector name:\n" + string.Join("\n", offenders));
    }
}
