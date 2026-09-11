#nullable enable
using Kor.Operations.EngineeringTools.Intake;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// The census of the drawing corpus counts what each job holds for the intake to learn from — the
/// dated structural stick-file issues, the architects' sets, the engineer's ETABS models — from the
/// share's own layout, in bounded listings (intake step 44's first half, 2026-09-11).
/// </summary>
/// <remarks>
/// WHAT THIS COVERS: a job with four stick files under three spellings (date first, date last, no
/// date - the file's date stands in and says so; the newest named), the naming template and a PDF
/// without STICKFILE in its name counted as "other", an architect's set one folder down under 01 Architectural, an ETABS Models folder as a
/// grandchild of 02 Engineering with .e2k and .EDB counted; a job with nothing; a folder that is not
/// a job (no number) skipped; the summary's X of Y lines. WHAT IT DOES NOT: the live share (the
/// verb runs it; its numbers are in the completion plan); a model folder deeper than a grandchild
/// (counted absent by design); a listing that throws (recorded as a problem line, not tested here).
/// </remarks>
public sealed class TheCorpusCensusCountsWhatEachJobHoldsTests
{
    [Fact]
    public void AJobsIssuesSetsAndModelsAreCountedFromTheSharesLayout()
    {
        string root = Path.Combine(Path.GetTempPath(), $"kor-census-{Guid.NewGuid():N}");
        try
        {
            string job = Path.Combine(root, "03 Residential", "31168-01 (YMCA Langara Vancouver)");
            string stick = Path.Combine(job, StickFileCorpus.StickFileFolder);
            Directory.CreateDirectory(Path.Combine(stick, StickFileCorpus.ArchitecturalFolder, "75% BP"));
            File.WriteAllText(Path.Combine(stick, "31168-01 - 2026-04-21- YMCA Langara - Stickfile.pdf"), "");
            File.WriteAllText(Path.Combine(stick, "31168-01 - 2026-09-10- YMCA Langara - Stickfile - With Arch.pdf"), "");
            File.WriteAllText(Path.Combine(stick, "31###-01 YYYY-MM-DD Project Name Stickfile.pdf"), "");           // the naming template: not an issue
            File.WriteAllText(Path.Combine(stick, "31168-01 - YMCA Langara - Struct Stickfile - 2021-12-21.pdf"), ""); // the older spelling, date at the end
            File.WriteAllText(Path.Combine(stick, "YMCA Langara Stickfile.pdf"), "");                                // no date in the name: the file's date stands in
            File.WriteAllText(Path.Combine(stick, "YMCA Langara - Record Drawings.pdf"), "");                        // not a stick file by name: other
            File.WriteAllText(Path.Combine(stick, StickFileCorpus.ArchitecturalFolder, "75% BP", "260723_W49th_75% BP Draft.pdf"), "");
            string models = Path.Combine(job, StickFileCorpus.EngineeringFolder, "02 Lateral Design", "01 ETABS Models");
            Directory.CreateDirectory(Path.Combine(models, "TEST"));
            File.WriteAllText(Path.Combine(models, "31168-reference.e2k"), "");
            File.WriteAllText(Path.Combine(models, "YMCA.EDB"), "");
            File.WriteAllText(Path.Combine(models, "TEST", "trial.e2k"), "");
            Directory.CreateDirectory(Path.Combine(root, "04 Commercial", "31200-01 (Nothing yet)"));
            Directory.CreateDirectory(Path.Combine(root, "04 Commercial", "Archive"));                            // not a job

            var problems = new List<string>();
            var census = StickFileCorpus.Census(root, problems, parallel: 2);

            Assert.Empty(problems);
            Assert.Equal(2, census.Count);
            var langara = Assert.Single(census, j => j.Job == "31168-01");
            Assert.Equal("03 Residential", langara.Category);
            Assert.Equal(4, langara.StructuralIssues.Count);
            Assert.EndsWith("With Arch.pdf", langara.NewestIssue, StringComparison.Ordinal);        // the newest NAMED date wins; the file dated today by the filesystem does not outrank it
            Assert.Contains(langara.StructuralIssues, i => i.Date == "2021-12-21" && i.DateFromName);
            Assert.Single(langara.StructuralIssues, i => !i.DateFromName);
            Assert.Equal(2, langara.OtherStickPdfs.Count);                                            // the template and the record drawings
            Assert.Single(langara.ArchitecturalPdfs);
            Assert.Equal(models, langara.ModelFolder);
            Assert.Equal(2, langara.E2kModels);
            Assert.Equal(1, langara.EdbModels);
            var empty = Assert.Single(census, j => j.Job == "31200-01");
            Assert.False(empty.HasStickFile); Assert.False(empty.HasArchitecturalSet); Assert.False(empty.HasModel);

            string summary = StickFileCorpus.Summary(census);
            Assert.Contains("2 job folders under 2 categories", summary, StringComparison.Ordinal);
            Assert.Contains("1 of 2 have a structural stick file (4 issues in all, 1 dated only by the file", summary, StringComparison.Ordinal);
            Assert.Contains("1 of 2 have BOTH a stick file and a model", summary, StringComparison.Ordinal);

            string csv = Path.Combine(root, "census.csv");
            StickFileCorpus.WriteCsv(census, csv);
            var lines = File.ReadAllLines(csv);
            Assert.Equal(3, lines.Length);
            Assert.StartsWith("category,job,folder,structural_issues,newest_issue_date", lines[0], StringComparison.Ordinal);
            Assert.Contains("\"31168-01\"", lines[1], StringComparison.Ordinal);
            Assert.Contains("\"2026-09-10\"", lines[1], StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
