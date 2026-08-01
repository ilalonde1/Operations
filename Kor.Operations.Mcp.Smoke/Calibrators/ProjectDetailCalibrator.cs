#nullable enable
using Kor.Operations.PMTools;

namespace Kor.Operations.Mcp.Smoke.Calibrators;

internal sealed class ProjectDetailCalibrator : CalibratorBase
{
    public ProjectDetailCalibrator(SmokeServices services)
        : base(services)
    {
    }

    public override async Task<CalibratedExpectation> CalibrateAsync(CancellationToken ct)
    {
        var svc = new ProjectAnalyticsService(Odbc, Financials);
        var rows = await Task.Run(() => svc.LoadProjectRowsSync(ct), ct).ConfigureAwait(false);
        var top = rows.OrderByDescending(r => r.TotalFee).FirstOrDefault();

        if (top == null)
        {
            return new CalibratedExpectation(
                "Project detail (no rows)",
                [new ExpectedToolCall("get_project_detail", [])],
                []);
        }

        // Picks the highest-TotalFee WBS1 at calibration time so the smoke
        // test stays meaningful regardless of which project leads the
        // portfolio. RuntimeQuestion injects the WBS1 into the prompt.
        var runtimeQuestion =
            $"Use the get_project_detail tool to fetch full detail for project WBS1 '{top.Wbs1}'. " +
            "Report the project's total fee.";

        return new CalibratedExpectation(
            $"Top-fee project detail ({top.Wbs1})",
            [new ExpectedToolCall("get_project_detail", [top.Wbs1])],
            [new ExpectedAnswerValue($"{top.Wbs1} total fee", (decimal)top.TotalFee)],
            RuntimeQuestion: runtimeQuestion);
    }
}
