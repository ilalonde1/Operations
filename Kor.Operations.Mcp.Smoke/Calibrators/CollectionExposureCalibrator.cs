#nullable enable
using Kor.Operations.Financials;

namespace Kor.Operations.Mcp.Smoke.Calibrators;

internal sealed class CollectionExposureCalibrator : CalibratorBase
{
    public CollectionExposureCalibrator(SmokeServices services)
        : base(services)
    {
    }

    public override async Task<CalibratedExpectation> CalibrateAsync(CancellationToken ct)
    {
        var arSvc = new ArFinancialsService(Odbc, Financials);
        var billedSvc = new RecentBilledService(Odbc, Financials);
        var arTask = arSvc.LoadAsync(ct);
        var billedTask = billedSvc.LoadAsync(ct);
        await Task.WhenAll(arTask, billedTask).ConfigureAwait(false);
        var ratio = billedTask.Result.Billed90 > 0.004
            ? (decimal)(arTask.Result.FirmwideOutstandingCadEquiv / billedTask.Result.Billed90)
            : 0m;
        return new CalibratedExpectation(
            "Collection exposure ratio",
            [new ExpectedToolCall("get_collection_exposure", [])],
            [new ExpectedAnswerValue("collection exposure", ratio)]);
    }
}
