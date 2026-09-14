#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// Audit F16 (intake step 61, 2026-09-13): the preferred export path (&lt;yardsticks&gt;/&lt;job&gt;.e2k) was
/// returned as it stood, while the model-folder search refused a file this tool wrote and a shell with
/// no column - so a generated model filed there would have measured itself and reported perfect
/// agreement. Both guards apply on both paths now.
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a KOR-generated export refused, a columnless export refused, an engineer's export
/// taken. WHAT IT DOES NOT: the model-folder fallback (mirrored from the share; its guards were there).
/// </remarks>
public sealed class OurOwnOutputIsNeverTheYardstickTests
{
    private static StickFileCorpus.JobCensus Job(string job) => new("03 Residential", @"\\fs\" + job, job, [], [], [], null, 0, 0);

    [Fact]
    public void AGeneratedOrEmptyExportIsRefusedAndAnEngineersIsTaken()
    {
        string root = Path.Combine(Path.GetTempPath(), $"kor-yard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllLines(Path.Combine(root, "31001-01.e2k"), ["$ LINE CONNECTIVITIES", "  LINE \"KC1\"  COLUMN  \"KP1\"  \"KP1\"  1"]);   // ours
            File.WriteAllLines(Path.Combine(root, "31002-01.e2k"), ["$ STORIES - IN SEQUENCE FROM TOP", "  STORY \"L1\"  HEIGHT 3000"]);      // a shell
            File.WriteAllLines(Path.Combine(root, "31003-01.e2k"), ["$ LINE CONNECTIVITIES", "  LINE \"C1\"  COLUMN  \"1\"  \"1\"  1"]);          // hers

            Assert.Null(CorpusAnalyzer.YardstickFor(Job("31001-01"), root));
            Assert.Null(CorpusAnalyzer.YardstickFor(Job("31002-01"), root));
            Assert.Equal(Path.Combine(root, "31003-01.e2k"), CorpusAnalyzer.YardstickFor(Job("31003-01"), root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
