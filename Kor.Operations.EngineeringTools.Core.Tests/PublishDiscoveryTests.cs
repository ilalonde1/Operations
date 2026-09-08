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
        Assert.Single(contents.PlatesByStorey);
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

    // ── where the drawings and the reference are, and what a publish will and will not guess ──

    /// <summary>
    /// On 2026-09-07 every generated 31168 artefact was one folder down from where it had been,
    /// in "01 ETABS Models\TEST", and discovery looking one level deep found nothing. A reference
    /// named by the caller is found below the model folder, and the rebuilt model lands beside it.
    /// </summary>
    [Fact]
    public void AReferenceOneFolderDownIsFoundAndItsFolderBecomesTheModelFolder()
    {
        string models = NewFolder();
        string test = Directory.CreateDirectory(Path.Combine(models, "TEST")).FullName;
        File.WriteAllText(Path.Combine(test, "31168-reference.e2k"), "$ STORIES");
        string set = Directory.CreateDirectory(Path.Combine(test, "_DXF-from-Revit")).FullName;
        File.WriteAllText(Path.Combine(set, "LEVEL 1.dxf"), "0\nEOF\n");

        var found = PublishDiscovery.Discover(new PublishDiscoveryRequest("31168", models, null, "31168-reference.e2k"));

        Assert.Equal(test, found.ModelFolder);
        Assert.Equal("31168-reference.e2k", found.Reference);
        Assert.Equal(set, found.DxfFolder);
    }

    /// <summary>A folder with DXF in its name that holds no .dxf files is not a drawing set.</summary>
    [Fact]
    public void AFolderNamedDxfWithNoSheetsInItIsNotASet()
    {
        string models = NewFolder();
        File.WriteAllText(Path.Combine(models, "ref.e2k"), "$ STORIES");
        Directory.CreateDirectory(Path.Combine(models, "DXF Files"));                        // dated sets live below it
        string dated = Directory.CreateDirectory(Path.Combine(models, "DXF Files", "2026-08-25")).FullName;
        File.WriteAllText(Path.Combine(dated, "LEVEL 1.dxf"), "0\nEOF\n");

        // "2026-08-25" has no DXF in its name and "DXF Files" holds no sheets: nothing qualifies
        var ex = Assert.Throws<DirectoryNotFoundException>(() =>
            PublishDiscovery.Discover(new PublishDiscoveryRequest("job", models, null, "ref.e2k")));
        Assert.Contains("DXF", ex.Message);
    }

    /// <summary>
    /// Three sets beside one reference is what 31168 holds now. Taking the first of them was how a
    /// gate came to fail on a drawing set nothing ships; a publish does not guess which drawings it
    /// reads, it is told.
    /// </summary>
    [Fact]
    public void SeveralDrawingSetsAreRefusedUntilOneIsNamed()
    {
        string models = NewFolder();
        File.WriteAllText(Path.Combine(models, "ref.e2k"), "$ STORIES");
        foreach (string name in new[] { "_DXF-from-Revit-2026-08-26", "_DXF-plans-for-rebuild" })
        {
            string set = Directory.CreateDirectory(Path.Combine(models, name)).FullName;
            File.WriteAllText(Path.Combine(set, "LEVEL 1.dxf"), "0\nEOF\n");
        }

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PublishDiscovery.Discover(new PublishDiscoveryRequest("job", models, null, "ref.e2k")));
        Assert.Contains("_DXF-from-Revit-2026-08-26", ex.Message);
        Assert.Contains("_DXF-plans-for-rebuild", ex.Message);
        Assert.Contains("--dxf", ex.Message);

        var named = PublishDiscovery.Discover(new PublishDiscoveryRequest("job", models, "_DXF-plans-for-rebuild", "ref.e2k"));
        Assert.EndsWith("_DXF-plans-for-rebuild", named.DxfFolder);

        var missing = Assert.Throws<DirectoryNotFoundException>(() =>
            PublishDiscovery.Discover(new PublishDiscoveryRequest("job", models, "_DXF-nowhere", "ref.e2k")));
        Assert.Contains("_DXF-plans-for-rebuild", missing.Message);
    }

    [Fact]
    public void AThingIsFoundUnderARootByNameToADepthAndNoFurther()
    {
        string root = NewFolder();
        string deep = Directory.CreateDirectory(Path.Combine(root, "a", "b", "c")).FullName;
        File.WriteAllText(Path.Combine(deep, "thing.txt"), "");

        Assert.Equal(Path.Combine(deep, "thing.txt"), PublishDiscovery.FindUnder(root, "thing.txt", maxDepth: 3, file: true));
        Assert.Null(PublishDiscovery.FindUnder(root, "thing.txt", maxDepth: 2, file: true));
        Assert.Equal(deep, PublishDiscovery.FindUnder(root, "c", maxDepth: 2));
        Assert.Equal(deep, PublishDiscovery.FindUnder(root, Path.Combine("b", "c"), maxDepth: 1));
    }

    /// <summary>
    /// One place says where the jobs are. The environment may override the share; nothing else
    /// may carry the root, and no test file does.
    /// </summary>
    [Fact]
    public void TheProjectsRootIsTheEnvironmentsAnswerElseTheShare()
    {
        string? env = Environment.GetEnvironmentVariable(PublishDiscovery.ProjectsRootEnvironmentVariable);

        Assert.Equal(string.IsNullOrEmpty(env) ? PublishDiscovery.DefaultProjectsRoot : env, PublishDiscovery.ProjectsRoot);
    }

    private static string NewFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "kor-publish-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
