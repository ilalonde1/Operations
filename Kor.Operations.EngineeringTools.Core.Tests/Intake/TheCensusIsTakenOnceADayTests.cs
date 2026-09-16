#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// THE CENSUS IS TAKEN ONCE A DAY, NOT ONCE A RUN (2026-09-15): every corpus run walked every job folder on the
/// share again, ~90 s of wall clock for an answer that changes when an office issues a set. WHAT THIS COVERS: a
/// census taken is kept and a second call within the age reuses it without walking the root (the root is deleted
/// between the calls and the kept census still answers); a forced call walks again; a kept census older than the
/// age is retaken. WHAT IT DOES NOT: the census's own reading of a job folder (StickFileCorpus's tests), a set
/// issued after the census was taken (the --census flag is the answer, stated in the summary).
/// </summary>
public sealed class TheCensusIsTakenOnceADayTests
{
    private static string AJobFolder(string root, string category, string job)
    {
        string stick = Path.Combine(root, category, $"{job} (A Building)", "05 Stickfile");
        Directory.CreateDirectory(stick);
        File.WriteAllBytes(Path.Combine(stick, $"{job} 2026-09-01 Stickfile.pdf"), new byte[16]);
        return root;
    }

    [Fact]
    public void AKeptCensusAnswersWithoutTheShareAndAForcedOneWalksAgain()
    {
        string scratch = Path.Combine(Path.GetTempPath(), "kor-census-test-" + Guid.NewGuid().ToString("N"));
        string root = Path.Combine(scratch, "Projects");
        string kept = Path.Combine(scratch, "kept");
        try
        {
            AJobFolder(root, "03 Residential", "31099-01");
            var problems = new List<string>();
            var first = StickFileCorpus.CensusCached(root, problems, TimeSpan.FromHours(12), force: false, parallel: 1, keptUnder: kept);
            Assert.Single(first);
            Assert.Equal("31099-01", first[0].Job);
            Assert.Single(Directory.GetFiles(kept, "census-*.json"));

            // the share goes away; the kept census still answers within its age
            Directory.Delete(root, recursive: true);
            var again = StickFileCorpus.CensusCached(root, problems, TimeSpan.FromHours(12), force: false, parallel: 1, keptUnder: kept);
            Assert.Single(again);
            Assert.Equal(first[0].NewestIssue, again[0].NewestIssue);

            // older than its age, it is retaken - and the root is gone, which the walk says
            Assert.Throws<DirectoryNotFoundException>(() => StickFileCorpus.CensusCached(root, problems, TimeSpan.Zero, force: false, parallel: 1, keptUnder: kept));
            // forced, likewise
            Assert.Throws<DirectoryNotFoundException>(() => StickFileCorpus.CensusCached(root, problems, TimeSpan.FromHours(12), force: true, parallel: 1, keptUnder: kept));
        }
        finally
        {
            if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
        }
    }
}
