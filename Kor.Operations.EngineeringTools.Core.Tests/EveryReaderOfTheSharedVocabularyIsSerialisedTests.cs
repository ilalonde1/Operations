#nullable enable
using System.Text.RegularExpressions;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// Every test class that touches <c>PlanSheetNaming.Vocabulary</c> must be in the collection that
/// serialises against it.
/// </summary>
/// <remarks>
/// `PlanSheetNaming.Vocabulary` is a public mutable STATIC — the words an office uses for a storey,
/// a building, a mezzanine — and production sets it once per run from the banked rules. In one
/// process that is fine; in a test run, with xUnit running classes in PARALLEL, it is shared global
/// state. `AnotherOfficesWordsTests` sets it to a different practice's words and restores it in
/// Dispose, and anything reading it at that moment fails on a sheet name that is perfectly correct.
///
/// It presents as an intermittent that passes alone and fails in a full run, which is the shape that
/// gets written off as flaky. SheetNamingVocabularyCollection exists for it and its own summary ends
/// "⚠ Any NEW test class that touches that static belongs here too" — a rule stated in prose, which
/// is exactly the kind this repo has watched lose to momentum.
///
/// It lost. On 2026-09-02 `E2kDocumentTests.ASheetCanNameAMezzanineForOneLevelAndNotAnother` failed
/// in a full run and passed alone immediately after — the THIRD unexplained full-suite failure from
/// this one static, and E2kDocumentTests had never been added despite 60 references to the
/// vocabulary. PlacementModelTests and StickFileSlabThicknessReaderTests were exposed the same way
/// and had simply not been unlucky yet.
///
/// So the rule is a check now, and it fails on every instance at once rather than on whichever one
/// the scheduler happens to interleave.
///
/// WHAT THIS COVERS: any test class in this project that names PlanSheetNaming and is not in the
/// collection. WHAT IT DOES NOT: a class reaching the vocabulary INDIRECTLY, through a helper that
/// names PlanSheetNaming while the test class does not — this reads source text, not call graphs.
/// Nor does it cover other mutable statics; it knows about this one.
/// </remarks>
public sealed class EveryReaderOfTheSharedVocabularyIsSerialisedTests
{
    [Fact]
    public void NoTestClassReadsTheVocabularyOutsideItsCollection()
    {
        string root = Path.Combine(RepoRoot(), "Kor.Operations.EngineeringTools.Core.Tests");
        var offenders = new List<string>();

        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(file);

            // the collection's own definition names the static in its documentation
            if (Path.GetFileName(file) == "SheetNamingVocabularyCollection.cs") continue;
            if (Path.GetFileName(file) == Path.GetFileName(Self)) continue;

            if (!source.Contains("PlanSheetNaming.", StringComparison.Ordinal)) continue;
            if (source.Contains("Collection(SheetNamingVocabularyCollection.Name)", StringComparison.Ordinal))
                continue;

            offenders.Add(Path.GetFileName(file));
        }

        Assert.True(
            offenders.Count == 0,
            "These test files read PlanSheetNaming.Vocabulary — a mutable static another class "
            + "rewrites mid-run — without joining the collection that serialises them: "
            + string.Join(", ", offenders)
            + ". Add [Collection(SheetNamingVocabularyCollection.Name)] to the class.");
    }

    private const string Self = "EveryReaderOfTheSharedVocabularyIsSerialisedTests.cs";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Kor.Operations.EngineeringTools.Core.Tests")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
