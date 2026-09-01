using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

public class PublishDiscoveryTests
{
    [Fact]
    public void ReferenceSelectionRefusesToChooseBetweenTwoEngineerModels()
    {
        string folder = NewFolder();
        File.WriteAllText(Path.Combine(folder, "site.e2k"), "$ STORIES");
        File.WriteAllText(Path.Combine(folder, "tower-b.e2k"), "$ STORIES");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PublishDiscovery.ResolveReference(folder, reference: null));

        Assert.Contains("site.e2k", ex.Message);
        Assert.Contains("tower-b.e2k", ex.Message);
    }

    [Fact]
    public void ReferenceSelectionPrefersReferenceNameAndExcludesGeneratedModels()
    {
        string folder = NewFolder();
        File.WriteAllText(Path.Combine(folder, "31168-FROM-DRAWINGS.e2k"), "  AREA \"KW1\" PANEL");
        File.WriteAllText(Path.Combine(folder, "engineer.e2k"), "$ STORIES");
        File.WriteAllText(Path.Combine(folder, "round-tripped.e2k"), "  LINE \"KC12\" COLUMN");

        string chosen = PublishDiscovery.ResolveReference(folder, reference: null);

        Assert.Equal("engineer.e2k", chosen);
    }

    [Fact]
    public void ModelContentsIncludesHeadersAndOpeningsWithoutCallerCountingText()
    {
        var doc = E2kDocument.Parse(new[]
        {
            "$ STORIES",
            "  STORY \"LEVEL 1\"  HEIGHT 120",
            "$ AREA CONNECTIVITIES",
            "  AREA \"KW1\"  PANEL  4  \"KP1\"  \"KP2\"  \"KP3\"  \"KP4\"  1  1  0  0",
            "  AREA \"KS1\"  PANEL  4  \"KP1\"  \"KP2\"  \"KP3\"  \"KP4\"  1  1  0  0",
            "  AREA \"KF1\"  FLOOR  4  \"KP1\"  \"KP2\"  \"KP3\"  \"KP4\"",
            "  AREA \"KO1\"  AREA  4  \"KP1\"  \"KP2\"  \"KP3\"  \"KP4\"",
            "$ LINE CONNECTIVITIES",
            "  LINE \"KC1\"  COLUMN  \"KP1\"  \"KP1\"  1",
            "$ AREA ASSIGNS",
            "  AREAASSIGN  \"KW1\"  \"LEVEL 1\"",
            "  AREAASSIGN  \"KS1\"  \"LEVEL 1\"",
            "  AREAASSIGN  \"KF1\"  \"LEVEL 1\"",
            "  AREAASSIGN  \"KO1\"  \"LEVEL 1\"",
            "$ LINE ASSIGNS",
            "  LINEASSIGN  \"KC1\"  \"LEVEL 1\"",
        });

        var contents = doc.ReadContents();

        Assert.Equal(1, contents.Walls);
        Assert.Equal(1, contents.Columns);
        Assert.Equal(1, contents.Floors);
        Assert.Equal(1, contents.Headers);
        Assert.Equal(1, contents.Openings);
        Assert.Equal(1, contents.PlatesByStorey.Count);
    }

    // The projects root holds a bucket per sector and discovery walks all of them. On a share any
    // one can refuse -- a permission this account does not hold, a folder mid-rename, a
    // reconnecting mount. Unguarded that throws out of the whole walk and the publish fails before
    // reading a drawing, for a condition in a bucket the job is not even in. The PowerShell this
    // was ported from searched each child with -ErrorAction SilentlyContinue for this reason.
    [Fact]
    public void AProjectBucketThatWillNotEnumerateIsSkippedRatherThanThrown()
    {
        string unreadable = Path.Combine(NewFolder(), "no-such-bucket");

        var found = PublishDiscovery.SafeChildren("31168")(unreadable);

        Assert.Empty(found);
    }

    [Fact]
    public void AReadableBucketStillReturnsItsMatchingJobs()
    {
        string bucket = NewFolder();
        Directory.CreateDirectory(Path.Combine(bucket, "31168-01 (YMCA Langara Vancouver)"));
        Directory.CreateDirectory(Path.Combine(bucket, "31138-01 (2170 W 1st)"));

        var found = PublishDiscovery.SafeChildren("31168")(bucket).ToList();

        Assert.Single(found);
        Assert.EndsWith("31168-01 (YMCA Langara Vancouver)", found[0], StringComparison.Ordinal);
    }

    private static string NewFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "kor-publish-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
