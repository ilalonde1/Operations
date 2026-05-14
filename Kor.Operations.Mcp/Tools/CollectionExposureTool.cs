#nullable enable
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Kor.Operations.Financials;
using Kor.Operations.Mcp.Audit;
using ModelContextProtocol.Server;

namespace Kor.Operations.Mcp.Tools;

/// <summary>
/// Structured KOR-canonical Collection Exposure: ratio of current AR Outstanding
/// (CAD-equiv) to SUM(PRSummaryMain.Billed) over the latest 3 closed periods.
/// Composes ArFinancialsService + RecentBilledService so both halves match the
/// WPF Executive Summary tile by construction. "Billed90" is period-anchored,
/// NOT a literal 90-day calendar window - Deltek's ~3-month posting lag would
/// otherwise collapse a strict calendar window to near zero.
/// </summary>
[McpServerToolType]
public sealed class CollectionExposureTool
{
    private readonly ArFinancialsService _ar;
    private readonly RecentBilledService _recentBilled;
    private readonly AuditLogger _audit;
    private readonly ILogger<CollectionExposureTool> _logger;

    public CollectionExposureTool(
        ArFinancialsService ar,
        RecentBilledService recentBilled,
        AuditLogger audit,
        ILogger<CollectionExposureTool> logger)
    {
        _ar = ar;
        _recentBilled = recentBilled;
        _audit = audit;
        _logger = logger;
    }

    [McpServerTool(Name = "get_collection_exposure")]
    [Description(
        "Get KOR-canonical Collection Exposure ratio: AR Outstanding firmwide (CAD-equiv) divided " +
        "by SUM(PRSummaryMain.Billed) over the latest 3 closed periods. Composes ArFinancialsService " +
        "+ RecentBilledService so both halves match the WPF Executive Summary tile by construction. " +
        "NOTE: 'Billed90' is period-anchored (last 3 closed periods), NOT a literal 90-day calendar " +
        "window - at KOR, PRSummaryMain has a ~3-month posting lag, so a strict calendar window " +
        "would collapse to ~$0 and produce misleading 'we billed nothing recently' headlines. " +
        "High ratio = collection lagging recent billing pace. ALWAYS use this for 'collection " +
        "exposure', 'AR vs recent billing', 'how much AR vs recent invoicing' questions.")]
    public async Task<string> GetCollectionExposureAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        try
        {
            var arTask = _ar.LoadAsync(cancellationToken);
            var recentBilledTask = _recentBilled.LoadAsync(cancellationToken);
            await Task.WhenAll(arTask, recentBilledTask).ConfigureAwait(false);
            var ar = arTask.Result;
            var rb = recentBilledTask.Result;

            var arOutstanding = ar.FirmwideOutstandingCadEquiv;
            var billed90 = rb.Billed90;
            var ratio = billed90 > 0.004 ? arOutstanding / billed90 : 0.0;

            var payload = new
            {
                ratio = ratio,
                arOutstandingCadEquiv = arOutstanding,
                billed90CadEquiv = billed90,
                billed30CadEquiv = rb.Billed30,
                latestPeriod = rb.LatestPeriod,
                periodsUsed = rb.Periods,
                arUsdToCadRate = ar.UsdToCadRate,
                billedUsdToCadRate = rb.UsdToCadRate,
                arDataLoaded = ar.UsdToCadRate > 0,
                billedDataLoaded = rb.DataLoaded,
                methodology =
                    "Canonical KOR Collection Exposure. Numerator = ArFinancialsService " +
                    "firmwideOutstandingCadEquiv (AR firmwide, CAD-equiv, USA bucket FX-converted). " +
                    "Denominator is period-anchored: RecentBilledService Billed90 = SUM(PRSummaryMain.Billed) over the " +
                    "latest 3 closed periods, Org-bucketed (USA -> CAD at Financials.Billed.UsdToCadRate). " +
                    "Periods are determined via 'SELECT TOP 3 Period ... ORDER BY Period DESC' against " +
                    "PRSummaryMain - a deliberate simplification of the WPF flow's accounting-calendar " +
                    "join (PRSummaryMain only contains posted/closed periods at KOR, so the proxy is " +
                    "equivalent in practice). Ratio interpretation: 1.0 = AR equals 3 months of billing; " +
                    "higher = collection is lagging recent billing pace; lower = healthy collection cadence.",
                durationMs = (int)sw.ElapsedMilliseconds,
            };
            sw.Stop();
            return JsonSerializer.Serialize(payload);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            errorMessage = "Query cancelled.";
            return JsonError(errorMessage);
        }
        catch (Exception ex)
        {
            sw.Stop();
            errorMessage = $"{ex.GetType().Name}: {ex.Message}";
            _logger.LogWarning(ex, "get_collection_exposure failed.");
            return JsonError(errorMessage);
        }
        finally
        {
            _ = _audit.WriteAsync(new AuditEntry(
                UserUpn: null,
                ClientApp: null,
                ToolName: "get_collection_exposure",
                InputJson: "{}",
                ResultStatus: errorMessage == null ? "Ok" : "Error",
                DurationMs: (int)sw.ElapsedMilliseconds,
                ErrorMessage: errorMessage), CancellationToken.None);
        }
    }

    private static string JsonError(string message) =>
        ToolErrorEnvelope.Build("get_collection_exposure", message, errorClass: "Unknown", recoverable: true, durationMs: 0);
}
