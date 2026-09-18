using Kor.Operations.EngineeringTools.Dxf;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// ONE LIVE BASELINE, KEPT (2026-09-18 14:55). LiveProjectBaselineTests builds the reference buildings from their Revit
/// DXFs and asserts the counts, then deletes the model - so when a count moves there is nothing to open. This builds
/// the same set the same way and KEEPS the model and the composer's warnings under TestResults/live-baseline/&lt;job&gt;/,
/// for a diff between two builds (a git bisect's good and bad, run in two worktrees). Silent unless
/// <c>KOR_LIVE_BASELINE</c> names a job: 31138 (2170 W 1st), 31168 (YMCA).
/// <code>KOR_LIVE_BASELINE=31138 dotnet test --filter FullyQualifiedName~LiveBaselineProbe</code>
/// </summary>
[Collection(SheetNamingVocabularyCollection.Name)]   // DxfToEtabsService sets PlanSheetNaming.Vocabulary, a shared static
public sealed class LiveBaselineProbe
{
    [Fact]
    public void BuildOneLiveBaselineAndKeepIt()
    {
        string? job = Environment.GetEnvironmentVariable("KOR_LIVE_BASELINE");
        if (string.IsNullOrWhiteSpace(job)) return;
        Assert.True(LiveProjects.ShareReachable, "the projects share is not reachable");
        (string drawings, string reference) = job switch
        {
            "31138" => ("_DXF-plans-for-rebuild", "31138-reference-from-Andrea-gravity.e2k"),
            _ => throw new ArgumentException($"no live baseline named {job}"),
        };
        string dir = Path.Combine(AppContext.BaseDirectory, "TestResults", "live-baseline", job);
        Directory.CreateDirectory(dir);
        string output = Path.Combine(dir, "out.e2k");
        var report = DxfToEtabsService.Run(new DxfToEtabsRequest
        {
            RequireRuleSettings = true,
            DxfFolder = LiveProjects.Drawings(job, drawings),
            ReferenceE2k = LiveProjects.File(job, reference),
            OutputE2k = output,
        });
        var lines = new List<string>
        {
            $"storeys {report.SavedModel.Storeys.Count} walls {report.SavedModel.Walls} columns {report.SavedModel.Columns} floors {report.SavedModel.Floors}",
        };
        lines.AddRange(report.Warnings.Select(w => "  - " + w));
        lines.AddRange(report.Summary.Flags.Where(f => !report.Warnings.Contains(f)).Select(f => "  - " + f));
        File.WriteAllLines(Path.Combine(dir, "report.txt"), lines);
    }
}
