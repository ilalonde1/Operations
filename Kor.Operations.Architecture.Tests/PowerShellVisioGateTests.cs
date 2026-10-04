using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.Architecture.Tests;

public sealed class PowerShellVisioGateTests
{
    private const string CoverageSummary =
        "Covers: PowerShell scripts that contain literal Visio ProgID or connector tokens. " +
        "Does not cover: concatenated/obfuscated ProgIDs, a C# re-implementation outside VisioRenderer, or automation of a different COM app.";

    private readonly ITestOutputHelper _out;

    public PowerShellVisioGateTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void PowerShellDoesNotReimplementVisioRendering()
    {
        _out.WriteLine(CoverageSummary);

        var root = ExtractorTests.RepoRootForTests();
        var offenders = new List<string>();
        foreach (var path in Directory.EnumerateFiles(root, "*.ps1", SearchOption.AllDirectories)
                     .OrderBy(p => Path.GetRelativePath(root, p), StringComparer.OrdinalIgnoreCase))
        {
            var rel = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (Skip(rel))
            {
                continue;
            }

            var text = TextFiles.ReadAllText(path);
            foreach (var forbidden in new[] { "Visio.Application", "ConnectorToolDataObject" })
            {
                if (text.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add($"{rel}: contains {forbidden}");
                }
            }
        }

        Assert.True(offenders.Count == 0, CoverageSummary + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static bool Skip(string rel)
        => rel.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
        || rel.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
        || rel.Contains("/.git/", StringComparison.OrdinalIgnoreCase)
        || rel.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
        || rel.StartsWith("obj/", StringComparison.OrdinalIgnoreCase);
}
