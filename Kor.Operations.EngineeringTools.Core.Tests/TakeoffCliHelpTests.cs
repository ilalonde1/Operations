#nullable enable
using System.Text.RegularExpressions;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public sealed class TakeoffCliHelpTests
{
    [Fact]
    public void Help_lists_every_dispatched_subcommand()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Kor.Operations.EngineeringTools.TakeoffCli", "Program.cs"));
        var dispatched = Regex.Matches(source, @"args\[0\]\.Equals\(""(?<name>[^""]+)""")
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var helped = global::TakeoffCliHelp.Commands
            .Select(command => command.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(
            dispatched.SequenceEqual(helped, StringComparer.OrdinalIgnoreCase),
            "Help command list does not match dispatched subcommands. Dispatched: "
            + string.Join(", ", dispatched)
            + " Help: "
            + string.Join(", ", helped));

        using var writer = new StringWriter();
        global::TakeoffCliHelp.WriteTo(writer);
        var help = writer.ToString();
        foreach (var command in global::TakeoffCliHelp.Commands)
        {
            Assert.Contains(command.Name, help, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(command.Description, help, StringComparison.Ordinal);
            Assert.Contains(command.Usage, help, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Bare_invocation_and_help_flags_use_the_help_printer()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Kor.Operations.EngineeringTools.TakeoffCli", "Program.cs"));

        Assert.True(global::TakeoffCliHelp.IsHelpRequest([]));
        Assert.True(global::TakeoffCliHelp.IsHelpRequest(["--help"]));
        Assert.True(global::TakeoffCliHelp.IsHelpRequest(["-h"]));
        Assert.Contains("if (TakeoffCliHelp.IsHelpRequest(args))", source, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Kor.Operations.EngineeringTools.TakeoffCli"))
                && Directory.Exists(Path.Combine(dir.FullName, "Kor.Operations.App")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }
}
