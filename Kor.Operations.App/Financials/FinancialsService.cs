#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Odbc;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.Options;
using Kor.Operations.Data;
using static Kor.Operations.Data.DataReaderHelpers;
using static Kor.Operations.Core.MathHelpers;
using Kor.Operations.Shared;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
namespace Kor.Operations.Financials
{
    // Labor code constants — match Deltek Vantagepoint tkDetail.LaborCode values
    internal static class LaborCodes
    {
        public const int Engineering = 10;
        public const int Drafting    = 20;
        public const int Checking    = 30;
        public const int Inspection  = 40;
        public const int DocPrep     = 50;
        public const int General     = 60;
        public const int Admin       = 70;
        public const int NonBillable = 80;
    }

    public sealed class FinancialsService
    {
        private readonly object _cacheLock = new();
        private FinancialsSnapshot? _cache;
        private readonly DeltekOdbcOptions _odbcOptions;

        public FinancialsService(DeltekOdbcOptions odbcOptions)
        {
            _odbcOptions = odbcOptions ?? throw new ArgumentNullException(nameof(odbcOptions));
        }

        private bool? _cacheWatchlistOnly;

        public async Task<FinancialsSnapshot> GetSnapshotAsync(bool forceRefresh, CancellationToken ct, bool watchlistOnly = true)
        {
            lock (_cacheLock)
            {
                if (!forceRefresh && _cache != null && _cacheWatchlistOnly == watchlistOnly)
                    return _cache;
            }

            ct.ThrowIfCancellationRequested();
            var snap = await LoadSnapshotAsync(ct, watchlistOnly).ConfigureAwait(false);
            lock (_cacheLock)
            {
                _cache = snap;
                _cacheWatchlistOnly = watchlistOnly;
            }
            return snap;
        }

        public async Task<DateTime?> GetMaxPostedPeriodAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var dsn = string.IsNullOrWhiteSpace(_odbcOptions.Dsn) ? "Deltek" : _odbcOptions.Dsn;
            var catalog = DeltekCatalogValidator.ResolveCatalog(_odbcOptions.Catalog);
            var factory = new VpOdbcDsnFactory(dsn, _odbcOptions.User ?? "", _odbcOptions.Password ?? "",
                              () => new Dictionary<string, string>());

            return await Task.Run(() => LoadMaxPostedPeriodSync(factory, catalog, ct), ct).ConfigureAwait(false);
        }

        private async Task<FinancialsSnapshot> LoadSnapshotAsync(CancellationToken ct, bool watchlistOnly = true)
        {
            var refreshedAt = DateTimeOffset.Now;

            var dsn     = string.IsNullOrWhiteSpace(_odbcOptions.Dsn) ? "Deltek" : _odbcOptions.Dsn;
            var catalog = DeltekCatalogValidator.ResolveCatalog(_odbcOptions.Catalog);
            var factory = new VpOdbcDsnFactory(dsn, _odbcOptions.User ?? "", _odbcOptions.Password ?? "",
                              () => new Dictionary<string, string>());

            // 1) Base project list + peer dataset — run in parallel (peer doesn't need wbs1List)
            var tBase = Task.Run(() => LoadBaseProjectsSync(factory, catalog, ct, watchlistOnly), ct);
            var tPeers = Task.Run(() => LoadPeerProjectsSync(factory, catalog, ct), ct);
            await Task.WhenAll(tBase, tPeers).ConfigureAwait(false);
            var projects = tBase.Result;
            var peerDataset = tPeers.Result;

            var wbs1List = projects.Select(p => p.Wbs1).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
#if DEBUG
            var dupWbs1 = projects
                .GroupBy(p => p.Wbs1, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => $"{g.Key} ({g.Count()})")
                .ToList();
            if (dupWbs1.Count > 0)
                System.Diagnostics.Debug.Fail("Financials base query produced duplicate WBS1 rows: " + string.Join(", ", dupWbs1));
#endif
            if (wbs1List.Count == 0)
            {
                // Even with zero active projects, load the lifetime client portfolio and revenue
                // history so those tabs render historical data.
                var emptyRollupsTask = Task.Run(() => LoadClientPortfolioSync(factory, catalog, ct), ct);
                var emptyHistoryTask = Task.Run(() => LoadRevenueHistorySync(factory, catalog, ct), ct);
                var emptyMaxPostedTask = Task.Run(() => LoadMaxPostedPeriodSync(factory, catalog, ct), ct);
                await Task.WhenAll(emptyRollupsTask, emptyHistoryTask, emptyMaxPostedTask).ConfigureAwait(false);
                return new FinancialsSnapshot
                {
                    RefreshedAt = refreshedAt,
                    Headline = new FinancialsHeadlineKpis(),
                    Rows = new List<FinancialsProjectRow>(),
                    ClientRollups = emptyRollupsTask.Result,
                    RevenueHistory = emptyHistoryTask.Result,
                    MaxPostedPeriod = emptyMaxPostedTask.Result,
                };
            }

            // 2-10) All independent of each other — run on separate connections in parallel
            var t2  = Task.Run(() => LoadFeeBilledSync(factory, catalog, wbs1List, ct), ct);
            var t2c = Task.Run(() => LoadUnpostedFeeBilledSync(factory, catalog, wbs1List, ct), ct);
            var t2b = Task.Run(() => LoadHourlyRevenueSync(factory, catalog, wbs1List, ct), ct);
            var t3  = Task.Run(() => LoadHoursByLaborSync(factory, catalog, wbs1List, ct), ct);
            var t3b = Task.Run(() => LoadCostByLaborSync(factory, catalog, wbs1List, ct), ct);
            var t4  = Task.Run(() => LoadPrLaborSync(factory, catalog, wbs1List, ct), ct);
            var t5  = Task.Run(() => LoadApSync(factory, catalog, wbs1List, ct), ct);
            var t6  = Task.Run(() => LoadInspectionCountsSync(factory, catalog, wbs1List, ct), ct);
            var t7  = Task.Run(() => LoadClientLookupSync(factory, catalog, wbs1List, ct), ct);
            var t8  = Task.Run(() => LoadClientPortfolioSync(factory, catalog, ct), ct);
            var t9  = Task.Run(() => LoadRevenueHistorySync(factory, catalog, ct), ct);
            var t10 = Task.Run(() => LoadMaxPostedPeriodSync(factory, catalog, ct), ct);

            await Task.WhenAll(t2, t2c, t2b, t3, t3b, t4, t5, t6, t7, t8, t9, t10).ConfigureAwait(false);

            var feeBilledByWbs1    = t2.Result;
            var unpostedFeeBilledByWbs1 = t2c.Result;
            var hourlyRevByWbs1    = t2b.Result;
            var hrsByWbs1AndLabor  = t3.Result;
            var costByWbs1AndLabor = t3b.Result;
            var prLaborBudgetByKey = t4.Result;
            var apByWbs1           = t5.Result;
            var inspectionsByWbs1  = t6.Result;
            var clientByWbs1       = t7.Result;
            var clientRollups      = t8.Result;
            var revenueHistory     = t9.Result;
            var maxPostedPeriod    = t10.Result;

            // 6) Compute row KPIs and cache delivery confidence (preserves Excel semantics)
            var u1 = _odbcOptions.EngRate;
            var u2 = _odbcOptions.DraftRate;
            var u3 = (u1 > 0 && u2 > 0) ? 1.0 / (1.0 / u1 + 1.0 / u2) : 0.0;
            var rows = new List<FinancialsProjectRow>(projects.Count);
            foreach (var p in projects)
            {
                ct.ThrowIfCancellationRequested();

                var feeBilled = feeBilledByWbs1.TryGetValue(p.Wbs1, out var fb) ? fb : 0.0;
                var hourlyRev = hourlyRevByWbs1.TryGetValue(p.Wbs1, out var hr) ? hr : 0.0;
                var totalFee = p.Fee + hourlyRev;

                // Checking (30) merged into Engineering — same production category
                var eng     = GetHrs(hrsByWbs1AndLabor, p.Wbs1, LaborCodes.Engineering)
                            + GetHrs(hrsByWbs1AndLabor, p.Wbs1, LaborCodes.Checking);
                var draft   = GetHrs(hrsByWbs1AndLabor, p.Wbs1, LaborCodes.Drafting);
                var insp    = GetHrs(hrsByWbs1AndLabor, p.Wbs1, LaborCodes.Inspection);
                var docPrep = GetHrs(hrsByWbs1AndLabor, p.Wbs1, LaborCodes.DocPrep);
                var gen     = GetHrs(hrsByWbs1AndLabor, p.Wbs1, LaborCodes.General);
                var admin   = GetHrs(hrsByWbs1AndLabor, p.Wbs1, LaborCodes.Admin);
                var nonBill = GetHrs(hrsByWbs1AndLabor, p.Wbs1, LaborCodes.NonBillable);

                var engCost     = GetCost(costByWbs1AndLabor, p.Wbs1, LaborCodes.Engineering)
                                + GetCost(costByWbs1AndLabor, p.Wbs1, LaborCodes.Checking);
                var draftCost   = GetCost(costByWbs1AndLabor, p.Wbs1, LaborCodes.Drafting);
                var inspCost    = GetCost(costByWbs1AndLabor, p.Wbs1, LaborCodes.Inspection);
                var docPrepCost = GetCost(costByWbs1AndLabor, p.Wbs1, LaborCodes.DocPrep);
                var genCost     = GetCost(costByWbs1AndLabor, p.Wbs1, LaborCodes.General);
                var adminCost   = GetCost(costByWbs1AndLabor, p.Wbs1, LaborCodes.Admin);
                var nonBillCost = GetCost(costByWbs1AndLabor, p.Wbs1, LaborCodes.NonBillable);

                var prLaborIdEng   = _odbcOptions.PrLaborIdEng;
                var prLaborIdDraft = _odbcOptions.PrLaborIdDraft;
                var engBudgetActual   = prLaborBudgetByKey.TryGetValue(p.Wbs1, out var lm)  && lm.TryGetValue(prLaborIdEng,   out var eb) ? eb : 0.0;
                var draftBudgetActual = prLaborBudgetByKey.TryGetValue(p.Wbs1, out var lm2) && lm2.TryGetValue(prLaborIdDraft, out var db) ? db : 0.0;

                // Budget modes:
                //   Target Rate: Fee / TargetRate for ALL projects. Simple, predictable, driven by the Target slider.
                //   Peer-Based:  1) Deltek actual  2) Peer median from similar closed projects  3) Formula fallback.
                var peerCount = 0;
                string budgetSource;
                double engBudget, draftBudget;
                if (_odbcOptions.UseTargetRateBudget)
                {
                    // Target Rate mode: formula for everything
                    engBudget = CalcBudget(totalFee, _odbcOptions.EngRate, u3);
                    draftBudget = CalcBudget(totalFee, _odbcOptions.DraftRate, u3);
                    budgetSource = "Target Rate";
                }
                else if (engBudgetActual > 0 && draftBudgetActual > 0)
                {
                    engBudget = engBudgetActual;
                    draftBudget = draftBudgetActual;
                    budgetSource = "Deltek";
                }
                else
                {
                    var (peerEng, peerDraft, pc) = PeerBudgetEstimator.Estimate(totalFee, p.Phase, p.ConstructionType, p.ProjectCategory, peerDataset, p.Wbs1);
                    peerCount = pc;
                    if (pc >= AnalyticsThresholds.MinPeerCount)
                    {
                        engBudget = peerEng;
                        draftBudget = peerDraft;
                        budgetSource = $"Peers ({pc})";
                    }
                    else
                    {
                        engBudget = CalcBudget(totalFee, _odbcOptions.EngRate, u3);
                        draftBudget = CalcBudget(totalFee, _odbcOptions.DraftRate, u3);
                        budgetSource = "Formula";
                    }
                }

                // FeePerHours denominator: production hours only (eng+draft).
                // This differs from PM Tools BillableRateScore which uses all non-admin labor.
                // Rationale: Fee/Hr measures production efficiency; inspection/docprep are support.
                var feeHoursDen    = eng + draft;
                var billedHoursDen = eng + draft + insp + docPrep + gen + admin + nonBill;

                var row = new FinancialsProjectRow
                {
                    Wbs1          = p.Wbs1,
                    Name          = p.Name,
                    Phase         = p.Phase,
                    Pm            = p.Pm,
                    DraftingManager = p.DraftingManager,
                    BillingManager  = p.BillingManager,
                    ConstructionType = p.ConstructionType,
                    ProjectCategory  = p.ProjectCategory,
                    DraftingType     = p.DraftingType,
                    IsOnHotlist      = p.IsOnHotlist,

                    Gfa              = p.Gfa,
                    Fee              = p.Fee,
                    HourlyRevenue    = hourlyRev,
                    FeeBilled        = feeBilled,
                    UnpostedFeeBilled = unpostedFeeBilledByWbs1.TryGetValue(p.Wbs1, out var ufb) ? ufb : 0.0,
                    SubconsultantCost = apByWbs1.TryGetValue(p.Wbs1, out var ap) ? ap : 0.0,
                    PercentBilled    = SafeDiv(feeBilled, totalFee),

                    EngHrs    = eng,
                    DraftHrs  = draft,
                    InspHrs   = insp,
                    DocPrepHrs = docPrep,
                    GenHrs    = gen,
                    AdminHrs  = admin,
                    NonBillHrs = nonBill,
                    EngLaborCost     = engCost,
                    DraftLaborCost   = draftCost,
                    InspLaborCost    = inspCost,
                    DocPrepLaborCost = docPrepCost,
                    GenLaborCost     = genCost,
                    AdminLaborCost   = adminCost,
                    NonBillLaborCost = nonBillCost,

                    DraftBudget      = draftBudget,
                    EngBudget        = engBudget,
                    DraftBudgetActual = draftBudgetActual,
                    EngBudgetActual  = engBudgetActual,
                    DraftPercent     = SafeDiv(draft, draftBudget),
                    EngPercent       = SafeDiv(eng,   engBudget),

                    FeePerHours    = feeHoursDen > 0 ? (totalFee / feeHoursDen) : 0.0,
                    BilledPerHours = SafeDiv(feeBilled, billedHoursDen),
                    BilledPerHoursWithUnposted = SafeDiv(feeBilled + (unpostedFeeBilledByWbs1.TryGetValue(p.Wbs1, out var ufb2) ? ufb2 : 0.0), billedHoursDen),
                };
                row.BudgetPeerCount = peerCount;
                row.BudgetSource = budgetSource;
                // Compute delivery confidence once here; row models reuse it (Fix 4)
                row.DeliveryResult = DeliveryConfidenceCalculator.Compute(row);
                if (inspectionsByWbs1.TryGetValue(p.Wbs1, out var inspCounts))
                {
                    row.TotalInspections = inspCounts.Total;
                    row.LastMonthInspections = inspCounts.LastMonth;
                }
                if (clientByWbs1.TryGetValue(p.Wbs1, out var client))
                {
                    row.ClientId = client.ClientId;
                    row.ClientName = client.ClientName;
                }
                rows.Add(row);
            }

            // 8) Compute headline KPIs (match Excel)
            var headline = ComputeHeadline(rows);
            return new FinancialsSnapshot
            {
                RefreshedAt = refreshedAt,
                Headline = headline,
                Rows = rows,
                ClientRollups = clientRollups,
                RevenueHistory = revenueHistory,
                MaxPostedPeriod = maxPostedPeriod,
            };
        }

        // ── Per-query helpers (each opens its own connection for parallel execution) ──

        private static List<ProjectBaseRow> LoadBaseProjectsSync(VpOdbcDsnFactory factory, string catalog, CancellationToken ct, bool watchlistOnly = true)
        {
            var projects = new List<ProjectBaseRow>();
            using var cn = factory.Create();
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed.", ex); }

            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT
    pr.WBS1,
    pr.Name,
    pr.Status,
    pr.ProjMgr,
    em.FirstName,
    em.LastName,
    pctf.CustProjectPhase AS Phase,
    pctf.CustActualGFA AS GFA,
    pr.Fee,
    pctf.CustWatchlist,
    pctf.CustDraftingManager,
    em2.FirstName AS DmFirstName,
    em2.LastName AS DmLastName,
    pr.Principal,
    em3.FirstName AS BmFirstName,
    em3.LastName AS BmLastName,
    pctf.CustConstructionType,
    pctf.CustProjectCategory,
    pctf.CustDraftingType
 FROM [{catalog}].dbo.PR pr
 LEFT JOIN [{catalog}].dbo.ProjectCustomTabFields pctf
     ON pctf.WBS1 = pr.WBS1
    AND (pctf.WBS2 IS NULL OR LTRIM(RTRIM(pctf.WBS2)) = '')
 LEFT JOIN [{catalog}].dbo.EMMain em
     ON em.Employee = pr.ProjMgr
LEFT JOIN [{catalog}].dbo.EMMain em2
    ON em2.Employee = pctf.CustDraftingManager
LEFT JOIN [{catalog}].dbo.EMMain em3
    ON em3.Employee = pr.Principal
 WHERE
     (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
     AND UPPER(LTRIM(RTRIM(pr.Status))) IN ('A', 'ACTIVE')
     AND pr.WBS1 NOT LIKE '[A-Z]%'
     AND pr.WBS1 NOT LIKE '9[A-Z]%'
     AND pr.WBS1 NOT LIKE '99%'
     {(watchlistOnly ? "AND UPPER(LTRIM(RTRIM(pctf.CustWatchlist))) IN ('Y', 'YES', 'TRUE', '1')" : "")}
 ORDER BY pr.WBS1;";

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var wbs1 = GetTrimmed(r, 0);
                if (string.IsNullOrWhiteSpace(wbs1)) continue;
                var pm = BuildPmDisplay(GetTrimmed(r, 3), GetTrimmed(r, 4), GetTrimmed(r, 5));
                var dm = BuildPmDisplay(GetTrimmed(r, 10), GetTrimmed(r, 11), GetTrimmed(r, 12));
                var bm = BuildPmDisplay(GetTrimmed(r, 13), GetTrimmed(r, 14), GetTrimmed(r, 15));
                var watchlistRaw = GetTrimmed(r, 9);
                var isHotlisted = !string.IsNullOrEmpty(watchlistRaw)
                    && (watchlistRaw.Equals("Y", StringComparison.OrdinalIgnoreCase)
                        || watchlistRaw.Equals("YES", StringComparison.OrdinalIgnoreCase)
                        || watchlistRaw.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
                        || watchlistRaw == "1");

                projects.Add(new ProjectBaseRow
                {
                    Wbs1 = wbs1, Name = GetTrimmed(r, 1), Pm = pm, DraftingManager = dm, BillingManager = bm,
                    Phase = GetTrimmed(r, 6), Gfa = GetDouble(r, 7), Fee = GetDouble(r, 8),
                    ConstructionType = GetTrimmed(r, 16), ProjectCategory = GetTrimmed(r, 17), DraftingType = GetTrimmed(r, 18),
                    IsOnHotlist = isHotlisted,
                });
            }
            return projects;
        }

        private static Dictionary<string, double> LoadFeeBilledSync(
            VpOdbcDsnFactory factory, string catalog, List<string> wbs1List, CancellationToken ct)
        {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            using var cn = factory.Create();
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (fee billed).", ex); }
            foreach (var chunk in Chunk(wbs1List, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                cmd.CommandText = $@"
SELECT WBS1, SUM(CASE WHEN BilledFee <> 0 THEN BilledFee ELSE Revenue END) AS FeeBilled
FROM [{catalog}].dbo.PRSummaryMain
WHERE WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
GROUP BY WBS1;";
                AddInListParameters(cmd, chunk);
                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var wbs1 = GetTrimmed(r, 0);
                    if (!string.IsNullOrWhiteSpace(wbs1))
                        result[wbs1] = GetDouble(r, 1);
                }
            }
            return result;
        }

        private static Dictionary<string, double> LoadUnpostedFeeBilledSync(
            VpOdbcDsnFactory factory, string catalog, List<string> wbs1List, CancellationToken ct)
        {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            using var cn = factory.Create();
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (unposted fee billed).", ex); }
            foreach (var chunk in Chunk(wbs1List, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                var phAr = MakeInListPlaceholders(chunk.Count);
                var phPr = MakeInListPlaceholders(chunk.Count);
                cmd.CommandText = $@"
SELECT WBS1, SUM(UnpostedAmt) AS UnpostedFeeBilled
FROM (
    SELECT
        arP.WBS1,
        arP.Period,
        arP.ArAmt - COALESCE(prP.PostedAmt, 0) AS UnpostedAmt
    FROM (
        SELECT WBS1, Period, SUM(COALESCE(InvBalanceSourceCurrency, 0)) AS ArAmt
        FROM [{catalog}].dbo.AR
        WHERE WBS1 IN ({phAr})
          AND COALESCE(InvBalanceSourceCurrency, 0) > 0
        GROUP BY WBS1, Period
    ) arP
    LEFT JOIN (
        SELECT WBS1, Period,
               SUM(CASE WHEN BilledFee <> 0 THEN BilledFee ELSE COALESCE(Revenue, 0) END) AS PostedAmt
        FROM [{catalog}].dbo.PRSummaryMain
        WHERE WBS1 IN ({phPr})
        GROUP BY WBS1, Period
    ) prP
        ON prP.WBS1 = arP.WBS1 AND prP.Period = arP.Period
    WHERE arP.ArAmt - COALESCE(prP.PostedAmt, 0) > 0
) gap
GROUP BY WBS1;";
                AddInListParameters(cmd, chunk);
                AddInListParameters(cmd, chunk);
                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var wbs1 = GetTrimmed(r, 0);
                    if (!string.IsNullOrWhiteSpace(wbs1))
                        result[wbs1] = GetDouble(r, 1);
                }
            }
            return result;
        }

        private static Dictionary<string, double> LoadHourlyRevenueSync(
            VpOdbcDsnFactory factory, string catalog, List<string> wbs1List, CancellationToken ct)
        {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            using var cn = factory.Create();
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (hourly revenue).", ex); }
            foreach (var chunk in Chunk(wbs1List, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                cmd.CommandText = $@"
SELECT sm.WBS1, SUM(CASE WHEN sm.BilledFee <> 0 THEN sm.BilledFee ELSE COALESCE(sm.Revenue, 0) END) AS HourlyRevenue
FROM [{catalog}].dbo.PRSummaryMain sm
INNER JOIN [{catalog}].dbo.PR pr
    ON pr.WBS1 = sm.WBS1 AND pr.WBS2 = sm.WBS2 AND pr.WBS3 = sm.WBS3
WHERE sm.WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
  AND pr.Fee = 0
  AND pr.WBS2 IS NOT NULL AND LTRIM(RTRIM(pr.WBS2)) <> ''
  AND pr.WBS3 IS NOT NULL AND LTRIM(RTRIM(pr.WBS3)) <> ''
GROUP BY sm.WBS1
HAVING SUM(CASE WHEN sm.BilledFee <> 0 THEN sm.BilledFee ELSE COALESCE(sm.Revenue, 0) END) > 0;";
                AddInListParameters(cmd, chunk);
                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var wbs1 = GetTrimmed(r, 0);
                    if (!string.IsNullOrWhiteSpace(wbs1))
                        result[wbs1] = GetDouble(r, 1);
                }
            }
            return result;
        }

        private static Dictionary<(string Wbs1, int LaborCode), double> LoadHoursByLaborSync(
            VpOdbcDsnFactory factory, string catalog, List<string> wbs1List, CancellationToken ct)
        {
            var result = new Dictionary<(string, int), double>();
            using var cn = factory.Create();
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (hours by labor).", ex); }
            foreach (var chunk in Chunk(wbs1List, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                cmd.CommandText = $@"
SELECT WBS1, LaborCode, SUM(COALESCE(RegHrs,0) + COALESCE(OvtHrs,0) + COALESCE(SpecialOvtHrs,0)) AS Hrs
FROM [{catalog}].dbo.tkDetail
WHERE WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
GROUP BY WBS1, LaborCode;";
                AddInListParameters(cmd, chunk);
                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var wbs1 = GetTrimmed(r, 0);
                    var laborObj = r.IsDBNull(1) ? null : r.GetValue(1);
                    if (string.IsNullOrWhiteSpace(wbs1) || laborObj == null) continue;
                    if (TryParseLaborCode(laborObj, out var laborCode))
                        result[(wbs1, laborCode)] = GetDouble(r, 2);
                }
            }
            return result;
        }

        private static Dictionary<(string Wbs1, int LaborCode), double> LoadCostByLaborSync(
            VpOdbcDsnFactory factory, string catalog, List<string> wbs1List, CancellationToken ct)
        {
            var result = new Dictionary<(string, int), double>();
            using var cn = factory.Create();
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (cost by labor).", ex); }
            foreach (var chunk in Chunk(wbs1List, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                cmd.CommandText = $@"
SELECT WBS1, LaborCode,
       SUM(COALESCE(RegAmt,0) + COALESCE(OvtAmt,0) + COALESCE(SpecialOvtAmt,0)) AS Cost
FROM [{catalog}].dbo.tkDetail
WHERE WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
GROUP BY WBS1, LaborCode;";
                AddInListParameters(cmd, chunk);
                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var wbs1 = GetTrimmed(r, 0);
                    var laborObj = r.IsDBNull(1) ? null : r.GetValue(1);
                    if (string.IsNullOrWhiteSpace(wbs1) || laborObj == null) continue;
                    if (TryParseLaborCode(laborObj, out var laborCode))
                        result[(wbs1, laborCode)] = GetDouble(r, 2);
                }
            }
            return result;
        }

        private static Dictionary<string, Dictionary<string, double>> LoadPrLaborSync(
            VpOdbcDsnFactory factory, string catalog, List<string> wbs1List, CancellationToken ct)
        {
            var result = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
            using var cn = factory.Create();
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (PR labor).", ex); }
            foreach (var chunk in Chunk(wbs1List, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                cmd.CommandText = $@"
SELECT WBS1, LaborID, SUM(COALESCE(EstimateHrs,0)) AS BudgetHrs
FROM [{catalog}].dbo.PRLabor
WHERE WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
GROUP BY WBS1, LaborID;";
                AddInListParameters(cmd, chunk);
                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var wbs1    = GetTrimmed(r, 0);
                    var laborId = GetTrimmed(r, 1);
                    if (string.IsNullOrWhiteSpace(wbs1) || string.IsNullOrWhiteSpace(laborId)) continue;
                    if (!result.TryGetValue(wbs1, out var inner))
                        result[wbs1] = inner = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    inner[laborId] = GetDouble(r, 2);
                }
            }
            return result;
        }

        private static Dictionary<string, double> LoadApSync(
            VpOdbcDsnFactory factory, string catalog, List<string> wbs1List, CancellationToken ct)
        {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            using var cn = factory.Create();
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (AP).", ex); }
            foreach (var chunk in Chunk(wbs1List, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                cmd.CommandText = $@"
SELECT WBS1, SUM(COALESCE(Amount, 0)) AS ApTotal
FROM [{catalog}].dbo.apDetail
WHERE WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
GROUP BY WBS1;";
                AddInListParameters(cmd, chunk);
                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var wbs1 = GetTrimmed(r, 0);
                    if (!string.IsNullOrWhiteSpace(wbs1))
                        result[wbs1] = GetDouble(r, 1);
                }
            }
            return result;
        }

        /// <summary>
        /// Resolves each WBS1 to its client (ClientID + Name) via the AR table.
        /// A project's client is the most recent ClientID found on its invoices.
        /// Projects with no invoices return an empty entry (handled as "(unknown)" in the rollup).
        /// </summary>
        private static Dictionary<string, (string ClientId, string ClientName)> LoadClientLookupSync(
            VpOdbcDsnFactory factory, string catalog, List<string> wbs1List, CancellationToken ct)
        {
            var result = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            using var cn = factory.Create();
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (clients).", ex); }
            foreach (var chunk in Chunk(wbs1List, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                // Pick the most recent ClientID per WBS1 from AR, then resolve the display name from Clendor.
                cmd.CommandText = $@"
SELECT latest.WBS1, latest.ClientID, COALESCE(cc.Name, '') AS ClientName
FROM (
    SELECT ar.WBS1, ar.ClientID,
           ROW_NUMBER() OVER (PARTITION BY ar.WBS1
                              ORDER BY COALESCE(ar.InvoiceDate, ar.DueDate) DESC) AS rn
    FROM [{catalog}].dbo.AR ar
    WHERE ar.WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
      AND ar.ClientID IS NOT NULL
      AND LTRIM(RTRIM(ar.ClientID)) <> ''
) latest
LEFT JOIN [{catalog}].dbo.Clendor cc ON cc.ClientID = latest.ClientID
WHERE latest.rn = 1;";
                AddInListParameters(cmd, chunk);
                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var wbs1 = GetTrimmed(r, 0);
                    if (string.IsNullOrWhiteSpace(wbs1)) continue;
                    var clientId = GetTrimmed(r, 1);
                    var clientName = GetTrimmed(r, 2);
                    result[wbs1] = (clientId, clientName);
                }
            }
            return result;
        }

        /// <summary>
        /// Loads lifetime client analytics across ALL projects (active + closed), independent of
        /// the active-only snapshot used for the project grid. Returns one ClientRollupRow per
        /// distinct ClientID with lifetime fee, billed, AR aging, project list, and active-project
        /// breakdown. Projects whose AR records have no ClientID are bucketed as "(unknown)".
        /// </summary>
        private static List<ClientRollupRow> LoadClientPortfolioSync(
            VpOdbcDsnFactory factory, string catalog, CancellationToken ct)
        {
            var asOf = DateTime.Today;
            var groups = new Dictionary<string, ClientRollupRow>(StringComparer.OrdinalIgnoreCase);

            using var cn = factory.Create();
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (client portfolio).", ex); }

            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            // One row per project: every project ever, joined to its most recent client via AR,
            // billed totals from PRSummaryMain, AR outstanding + 90+ aging from AR.
            cmd.CommandText = $@"
SELECT
    pr.WBS1,
    pr.Name,
    pr.Status,
    pctf.CustProjectPhase AS Phase,
    pr.Fee,
    pr.OpenDate,
    pr.CloseDate,
    ISNULL(billed.FeeBilled, 0) AS FeeBilled,
    ISNULL(unposted.UnpostedFeeBilled, 0) AS UnpostedFeeBilled,
    ISNULL(arSum.Outstanding, 0) AS Outstanding,
    ISNULL(arSum.Aged90Plus, 0) AS Aged90Plus,
    latest.ClientID,
    COALESCE(cc.Name, '') AS ClientName,
    ISNULL(hourly.HourlyRevenue, 0) AS HourlyRevenue
FROM [{catalog}].dbo.PR pr
LEFT JOIN [{catalog}].dbo.ProjectCustomTabFields pctf
    ON pctf.WBS1 = pr.WBS1
   AND (pctf.WBS2 IS NULL OR LTRIM(RTRIM(pctf.WBS2)) = '')
LEFT JOIN (
    SELECT WBS1, SUM(CASE WHEN BilledFee <> 0 THEN BilledFee ELSE Revenue END) AS FeeBilled
    FROM [{catalog}].dbo.PRSummaryMain
    GROUP BY WBS1
) billed ON billed.WBS1 = pr.WBS1
LEFT JOIN (
    -- Per-period unposted-billings reconciliation for client portfolio.
    -- Per (WBS1, Period): unposted = MAX(0, AR_open_balance - PRSummaryMain_billed).
    SELECT WBS1, SUM(UnpostedAmt) AS UnpostedFeeBilled
    FROM (
        SELECT
            arP.WBS1,
            arP.Period,
            arP.ArAmt - COALESCE(prP.PostedAmt, 0) AS UnpostedAmt
        FROM (
            SELECT WBS1, Period, SUM(COALESCE(InvBalanceSourceCurrency, 0)) AS ArAmt
            FROM [{catalog}].dbo.AR
            WHERE COALESCE(InvBalanceSourceCurrency, 0) > 0
            GROUP BY WBS1, Period
        ) arP
        LEFT JOIN (
            SELECT WBS1, Period,
                   SUM(CASE WHEN BilledFee <> 0 THEN BilledFee ELSE COALESCE(Revenue, 0) END) AS PostedAmt
            FROM [{catalog}].dbo.PRSummaryMain
            GROUP BY WBS1, Period
        ) prP
            ON prP.WBS1 = arP.WBS1 AND prP.Period = arP.Period
        WHERE arP.ArAmt - COALESCE(prP.PostedAmt, 0) > 0
    ) gap
    GROUP BY WBS1
) unposted ON unposted.WBS1 = pr.WBS1
LEFT JOIN (
    SELECT sm.WBS1, SUM(CASE WHEN sm.BilledFee <> 0 THEN sm.BilledFee ELSE COALESCE(sm.Revenue, 0) END) AS HourlyRevenue
    FROM [{catalog}].dbo.PRSummaryMain sm
    INNER JOIN [{catalog}].dbo.PR prInner
        ON prInner.WBS1 = sm.WBS1 AND prInner.WBS2 = sm.WBS2 AND prInner.WBS3 = sm.WBS3
    WHERE prInner.Fee = 0
      AND prInner.WBS2 IS NOT NULL AND LTRIM(RTRIM(prInner.WBS2)) <> ''
      AND prInner.WBS3 IS NOT NULL AND LTRIM(RTRIM(prInner.WBS3)) <> ''
    GROUP BY sm.WBS1
    HAVING SUM(CASE WHEN sm.BilledFee <> 0 THEN sm.BilledFee ELSE COALESCE(sm.Revenue, 0) END) > 0
) hourly ON hourly.WBS1 = pr.WBS1
LEFT JOIN (
    SELECT WBS1,
        SUM(COALESCE(InvBalanceSourceCurrency, 0)) AS Outstanding,
        SUM(CASE WHEN DATEDIFF(day, COALESCE(DueDate, InvoiceDate), ?) > 90
                 THEN COALESCE(InvBalanceSourceCurrency, 0) ELSE 0 END) AS Aged90Plus
    FROM [{catalog}].dbo.AR
    GROUP BY WBS1
) arSum ON arSum.WBS1 = pr.WBS1
LEFT JOIN (
    SELECT ar.WBS1, ar.ClientID,
           ROW_NUMBER() OVER (PARTITION BY ar.WBS1
                              ORDER BY COALESCE(ar.InvoiceDate, ar.DueDate) DESC) AS rn
    FROM [{catalog}].dbo.AR ar
    WHERE ar.ClientID IS NOT NULL
      AND LTRIM(RTRIM(ar.ClientID)) <> ''
) latest ON latest.WBS1 = pr.WBS1 AND latest.rn = 1
LEFT JOIN [{catalog}].dbo.Clendor cc ON cc.ClientID = latest.ClientID
WHERE (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
  AND pr.WBS1 NOT LIKE '[A-Z]%'
  AND pr.WBS1 NOT LIKE '9[A-Z]%'
  AND pr.WBS1 NOT LIKE '99%'";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = asOf });

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var wbs1 = GetTrimmed(r, 0);
                if (string.IsNullOrWhiteSpace(wbs1)) continue;

                var status = GetTrimmed(r, 2);
                var isActive = string.Equals(status, "A", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(status, "ACTIVE", StringComparison.OrdinalIgnoreCase);

                var openDate = r.IsDBNull(5) ? (DateTime?)null : Convert.ToDateTime(r.GetValue(5), CultureInfo.InvariantCulture);
                var closeDate = r.IsDBNull(6) ? (DateTime?)null : Convert.ToDateTime(r.GetValue(6), CultureInfo.InvariantCulture);
                var clientId = GetTrimmed(r, 11);
                var clientName = GetTrimmed(r, 12);

                var project = new ClientProjectRow
                {
                    Wbs1 = wbs1,
                    Name = GetTrimmed(r, 1),
                    Phase = GetTrimmed(r, 3),
                    Status = status,
                    IsActive = isActive,
                    OpenDate = openDate,
                    CloseDate = closeDate,
                    Fee = GetDouble(r, 4),
                    FeeBilled = GetDouble(r, 7),
                    UnpostedFeeBilled = GetDouble(r, 8),
                    Outstanding = GetDouble(r, 9),
                    Outstanding90Plus = GetDouble(r, 10),
                    HourlyRevenue = GetDouble(r, 13),
                };

                var key = string.IsNullOrWhiteSpace(clientId) ? "" : clientId;
                var display = string.IsNullOrWhiteSpace(clientName) ? "(unknown)" : clientName;
                if (!groups.TryGetValue(key, out var roll))
                {
                    roll = new ClientRollupRow { ClientId = key, ClientName = display };
                    groups[key] = roll;
                }
                roll.Projects.Add(project);
                roll.ProjectCount++;
                if (isActive) roll.ActiveProjectCount++;
                roll.LifetimeFee += project.TotalFee;
                roll.LifetimeBilled += project.FeeBilled;
                roll.LifetimeUnpostedBilled += project.UnpostedFeeBilled;
                roll.Outstanding += project.Outstanding;
                roll.Outstanding90Plus += project.Outstanding90Plus;
                if (project.TotalFee > roll.LargestProjectFee) roll.LargestProjectFee = project.TotalFee;

                // Tenure: earliest open date
                if (openDate.HasValue && (!roll.FirstProjectDate.HasValue || openDate.Value < roll.FirstProjectDate.Value))
                    roll.FirstProjectDate = openDate.Value;
                // Activity: latest of (active → today, closed → CloseDate, fallback OpenDate)
                var activityCandidate = isActive ? DateTime.Today
                    : (closeDate ?? openDate);
                if (activityCandidate.HasValue && (!roll.LastActivityDate.HasValue || activityCandidate.Value > roll.LastActivityDate.Value))
                    roll.LastActivityDate = activityCandidate.Value;
            }

            // Sort each client's project list by date (most recent first) so the detail panel
            // shows the freshest engagement at the top.
            foreach (var roll in groups.Values)
            {
                roll.Projects.Sort((a, b) =>
                {
                    var da = a.OpenDate ?? a.CloseDate ?? DateTime.MinValue;
                    var db = b.OpenDate ?? b.CloseDate ?? DateTime.MinValue;
                    return db.CompareTo(da);
                });
            }

            return groups.Values
                .Where(g => g.LifetimeFee > 0 || g.LifetimeBilled > 0 || g.Outstanding > 0)
                .OrderByDescending(g => g.LifetimeFee)
                .ToList();
        }

        /// <summary>
        /// Loads firm-wide monthly revenue actuals for the trailing 24 months from PRSummaryMain.
        /// Period column is YYYYMM (string). We resolve Period → month via CFGAcctngCalendarData
        /// when available, falling back to YYYYMM parsing. endMonth uses the max posted period
        /// in PRSummaryMain, not the calendar previous-month, to avoid reading across empty periods.
        /// Months with no entry are returned as zero so the forecast chart has a continuous timeline.
        /// </summary>
        private static List<RevenueMonthRow> LoadRevenueHistorySync(
            VpOdbcDsnFactory factory, string catalog, CancellationToken ct)
        {
            var monthlyTotals = new Dictionary<DateTime, double>();
            using var cn = factory.Create();
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (revenue history).", ex); }

            // 1) Try to load the period→date calendar mapping (best-effort; fall back to YYYYMM parsing if missing).
            var calendar = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var calCmd = cn.CreateCommand();
                calCmd.CommandTimeout = SqlTimeouts.Batch;
                calCmd.CommandText = $@"SELECT Period, StartDate FROM [{catalog}].dbo.CFGAcctngCalendarData;";
                using var calReg = ct.Register(() => { try { calCmd.Cancel(); } catch { } });
                using var cr = calCmd.ExecuteReader();
                while (cr.Read())
                {
                    var p = GetTrimmed(cr, 0);
                    if (string.IsNullOrEmpty(p) || cr.IsDBNull(1)) continue;
                    var d = Convert.ToDateTime(cr.GetValue(1), CultureInfo.InvariantCulture);
                    calendar[p] = new DateTime(d.Year, d.Month, 1);
                }
            }
            catch
            {
                // Calendar table missing or inaccessible — fall back to YYYYMM parsing only.
            }

            // 2) Sum revenue by period across all projects.
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT Period, SUM(CASE WHEN BilledFee <> 0 THEN BilledFee ELSE COALESCE(Revenue, 0) END) AS Revenue
FROM [{catalog}].dbo.PRSummaryMain
GROUP BY Period;";

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var period = GetTrimmed(r, 0);
                if (string.IsNullOrEmpty(period)) continue;
                var revenue = GetDouble(r, 1);
                if (revenue == 0) continue;

                DateTime monthStart;
                if (calendar.TryGetValue(period, out var calMonth))
                {
                    monthStart = calMonth;
                }
                else if (period.Length == 6
                         && int.TryParse(period.Substring(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y)
                         && int.TryParse(period.Substring(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m)
                         && m >= 1 && m <= 12 && y >= 1990 && y <= 2100)
                {
                    monthStart = new DateTime(y, m, 1);
                }
                else
                {
                    continue;
                }

                if (monthlyTotals.TryGetValue(monthStart, out var existing))
                    monthlyTotals[monthStart] = existing + revenue;
                else
                    monthlyTotals[monthStart] = revenue;
            }

            // 3) Build a continuous 24-month timeline ending at the max posted PRSummaryMain period.
            //    If that probe fails, fall back to the previous full calendar month.
            var fallbackEndMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
            var endMonth = fallbackEndMonth;
            try
            {
                using var maxCmd = cn.CreateCommand();
                maxCmd.CommandTimeout = SqlTimeouts.Batch;
                maxCmd.CommandText = $@"SELECT MAX(Period) FROM [{catalog}].dbo.PRSummaryMain;";
                using var maxReg = ct.Register(() => { try { maxCmd.Cancel(); } catch { } });
                var maxPeriodObj = maxCmd.ExecuteScalar();
                if (maxPeriodObj != null && maxPeriodObj != DBNull.Value)
                {
                    var maxPeriod = Convert.ToString(maxPeriodObj, CultureInfo.InvariantCulture)?.Trim();
                    if (TryParseDeltekPeriod(maxPeriod, out var parsedPeriod))
                    {
                        endMonth = parsedPeriod;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Unexpected MAX(Period) value '{maxPeriod ?? "<null>"}'.");
                    }
                }
                else
                {
                    throw new InvalidOperationException("MAX(Period) returned NULL.");
                }
            }
            catch (Exception ex)
            {
                var logger = global::Kor.Operations.Services.AppServices.GetOptional<global::Microsoft.Extensions.Logging.ILoggerFactory>()
                    ?.CreateLogger(nameof(FinancialsService));
                if (logger != null)
                {
                    global::Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(
                        logger,
                        ex,
                        "Revenue history max-period probe failed; falling back to previous full calendar month.");
                }
                endMonth = fallbackEndMonth;
            }

            var startMonth = endMonth.AddMonths(-23);
            var result = new List<RevenueMonthRow>(24);
            for (var month = startMonth; month <= endMonth; month = month.AddMonths(1))
            {
                result.Add(new RevenueMonthRow
                {
                    MonthStart = month,
                    Revenue = monthlyTotals.TryGetValue(month, out var v) ? v : 0,
                    IsActual = true,
                });
            }
            return result;
        }

        private static DateTime? LoadMaxPostedPeriodSync(VpOdbcDsnFactory factory, string catalog, CancellationToken ct)
        {
            using var cn = factory.Create();
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (max posted period).", ex); }

            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"SELECT MAX(Period) FROM [{catalog}].dbo.PRSummaryMain;";
            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            var maxPeriodObj = cmd.ExecuteScalar();
            if (maxPeriodObj == null || maxPeriodObj == DBNull.Value)
                return null;

            var maxPeriod = Convert.ToString(maxPeriodObj, CultureInfo.InvariantCulture)?.Trim();
            return TryParseDeltekPeriod(maxPeriod, out var parsedPeriod) ? parsedPeriod : null;
        }

        private static bool TryParseDeltekPeriod(string? period, out DateTime monthStart)
        {
            monthStart = default;
            if (string.IsNullOrWhiteSpace(period) || period.Length != 6)
                return false;

            if (!int.TryParse(period.Substring(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year)
                || !int.TryParse(period.Substring(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var month)
                || month < 1 || month > 12 || year < 1990 || year > 2100)
            {
                return false;
            }

            monthStart = new DateTime(year, month, 1);
            return true;
        }

        internal static FinancialsHeadlineKpis ComputeHeadline(List<FinancialsProjectRow> rows)
            => FinancialsHeadlineCalculator.Compute(rows);

        private static Dictionary<string, (int Total, int LastMonth)> LoadInspectionCountsSync(
            VpOdbcDsnFactory factory, string catalog, List<string> wbs1List, CancellationToken ct)
        {
            var monthEnd = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var monthStart = monthEnd.AddMonths(-1);
            var result = new Dictionary<string, (int Total, int LastMonth)>(StringComparer.OrdinalIgnoreCase);
            using var cn = factory.Create();
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (inspections).", ex); }
            foreach (var chunk in Chunk(wbs1List, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                cmd.CommandText = $@"
SELECT WBS1,
    COUNT(*) AS TotalInspections,
    SUM(CASE WHEN TransDate >= ? AND TransDate < ? THEN 1 ELSE 0 END) AS LastMonthInspections
FROM [{catalog}].dbo.tkDetail
WHERE LaborCode = 40
  AND WBS1 IN ({MakeInListPlaceholders(chunk.Count)})
GROUP BY WBS1;";
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Date, Value = monthStart });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Date, Value = monthEnd });
                AddInListParameters(cmd, chunk);
                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var wbs1 = GetTrimmed(r, 0);
                    if (!string.IsNullOrWhiteSpace(wbs1))
                        result[wbs1] = ((int)GetDouble(r, 1), (int)GetDouble(r, 2));
                }
            }
            return result;
        }

        private double CalcBudget(double fee, double rate, double u3)
        {
            // Fee-based estimation using a single configurable target rate.
            // Band-specific rates were attempted but the source data ($/hr by fee band)
            // was skewed by active projects with incomplete hours, producing budgets
            // that were far too small. The single rate is more conservative and reliable.
            // Eng/Draft split controlled by EngRate/DraftRate — calibrated to 58%/42%.
            // See Historical Analytics → Fee Bands view for per-band $/hr analysis.
            if (fee <= 0 || rate <= 0) return 0.0;
            var target = _odbcOptions.TargetBillingRate;
            if (target <= 0) target = AnalyticsThresholds.DefaultTargetBillingRate;
            return (fee / target) * (u3 / rate);
        }


        private static double GetHrs(Dictionary<(string Wbs1, int LaborCode), double> map, string wbs1, int laborCode)
            => map.TryGetValue((wbs1, laborCode), out var v) ? v : 0.0;

        private static double GetCost(Dictionary<(string Wbs1, int LaborCode), double> map, string wbs1, int laborCode)
            => map.TryGetValue((wbs1, laborCode), out var v) ? v : 0.0;

        private static string BuildPmDisplay(string pmId, string first, string last)
        {
            var full = $"{first} {last}".Trim();
            if (!string.IsNullOrWhiteSpace(full))
                return full;
            return pmId;
        }


        private static bool TryParseLaborCode(object v, out int code)
        {
            code = 0;
            if (v is int i) { code = i; return true; }
            if (v is short s) { code = s; return true; }
            if (v is long l) { code = (int)l; return true; }
            var str = Convert.ToString(v, CultureInfo.InvariantCulture);
            return int.TryParse(str?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out code);
        }

        private static string MakeInListPlaceholders(int count)
            => string.Join(", ", Enumerable.Repeat("?", count));

        private static void AddInListParameters(OdbcCommand cmd, List<string> vals)
        {
            foreach (var v in vals)
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = v });
        }

        private static IEnumerable<List<T>> Chunk<T>(List<T> src, int size)
        {
            for (var i = 0; i < src.Count; i += size)
                yield return src.GetRange(i, Math.Min(size, src.Count - i));
        }

        // ── Peer-based budget estimation ────────────────────────────────────
        // Loads closed projects as a reference dataset, then uses median eng/draft
        // hours from similar peers (fee ±50%, same construction type / phase) instead
        // of the single-variable CalcBudget formula.

        private static List<PeerBudgetEstimator.PeerProject> LoadPeerProjectsSync(
            VpOdbcDsnFactory factory, string catalog, CancellationToken ct)
        {
            var peers = new List<PeerBudgetEstimator.PeerProject>();
            using var cn = factory.Create();
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (peers).", ex); }

            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.Batch;
            cmd.CommandText = $@"
SELECT
    pr.WBS1,
    pr.Fee,
    pctf.CustProjectPhase,
    pctf.CustConstructionType,
    ISNULL(labor.EngHrs, 0) AS EngHrs,
    ISNULL(labor.DraftHrs, 0) AS DraftHrs,
    pctf.CustProjectCategory
FROM [{catalog}].dbo.PR pr
LEFT JOIN [{catalog}].dbo.ProjectCustomTabFields pctf
    ON pctf.WBS1 = pr.WBS1
   AND (pctf.WBS2 IS NULL OR LTRIM(RTRIM(pctf.WBS2)) = '')
LEFT JOIN (
    SELECT WBS1,
        SUM(CASE WHEN LaborCode IN ({LaborCodes.Engineering}, {LaborCodes.Checking}) THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)+COALESCE(SpecialOvtHrs,0) ELSE 0 END) AS EngHrs,
        SUM(CASE WHEN LaborCode = {LaborCodes.Drafting} THEN COALESCE(RegHrs,0)+COALESCE(OvtHrs,0)+COALESCE(SpecialOvtHrs,0) ELSE 0 END) AS DraftHrs
    FROM [{catalog}].dbo.tkDetail
    WHERE WBS1 NOT LIKE '[A-Z]%'
      AND WBS1 NOT LIKE '9[A-Z]%'
      AND WBS1 NOT LIKE '99%'
    GROUP BY WBS1
) labor ON labor.WBS1 = pr.WBS1
WHERE (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
  AND UPPER(LTRIM(RTRIM(pr.Status))) NOT IN ('A', 'ACTIVE')
  AND pr.Fee > 0
  AND pr.WBS1 NOT LIKE '[A-Z]%'
  AND pr.WBS1 NOT LIKE '9[A-Z]%'
  AND pr.WBS1 NOT LIKE '99%'
  AND (ISNULL(labor.EngHrs, 0) + ISNULL(labor.DraftHrs, 0)) >= 50";

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var wbs1 = GetTrimmed(r, 0);
                if (string.IsNullOrWhiteSpace(wbs1)) continue;
                peers.Add(new PeerBudgetEstimator.PeerProject
                {
                    Wbs1 = wbs1,
                    Fee = GetDouble(r, 1),
                    Phase = GetTrimmed(r, 2),
                    ConstructionType = GetTrimmed(r, 3),
                    EngHrs = GetDouble(r, 4),
                    DraftHrs = GetDouble(r, 5),
                    ProjectCategory = GetTrimmed(r, 6),
                });
            }
            return peers;
        }

        private sealed class ProjectBaseRow
        {
            public string Wbs1 { get; set; } = "";
            public string Name { get; set; } = "";
            public string Phase { get; set; } = "";
            public string Pm { get; set; } = "";
            public string DraftingManager { get; set; } = "";
            public string BillingManager { get; set; } = "";
            public double Gfa { get; set; }
            public double Fee { get; set; }
            public string ConstructionType { get; set; } = "";
            public string ProjectCategory { get; set; } = "";
            public string DraftingType { get; set; } = "";
            public bool IsOnHotlist { get; set; }
        }
    }

    public sealed class FinancialsSnapshot
    {
        public DateTimeOffset RefreshedAt { get; set; }
        public FinancialsHeadlineKpis Headline { get; set; } = new();
        public List<FinancialsProjectRow> Rows { get; set; } = new();
        public List<ClientRollupRow> ClientRollups { get; set; } = new();
        public List<RevenueMonthRow> RevenueHistory { get; set; } = new();
        public DateTime? MaxPostedPeriod { get; set; }
    }

    /// <summary>
    /// One month of historical or forecast revenue. Used to drive the Revenue Forecast section.
    /// IsActual = true for historical months from PRSummaryMain; false for forecast months.
    /// IsPartial = true when the month is in the historical window but its revenue is suspiciously
    /// low compared to historical median, indicating Deltek hasn't finished posting it yet.
    /// </summary>
    public sealed class RevenueMonthRow
    {
        public DateTime MonthStart { get; set; }
        public string Label => MonthStart.ToString("MMM yy");
        public double Revenue { get; set; }
        public bool IsActual { get; set; }
        public bool IsPartial { get; set; }
    }

    /// <summary>
    /// One project row inside a ClientRollupRow. Independent of FinancialsProjectRow because
    /// the client portfolio loads ALL projects (active + closed) without the per-project hour
    /// detail loaded for the active grid.
    /// </summary>
    public sealed class ClientProjectRow
    {
        public string Wbs1 { get; set; } = "";
        public string Name { get; set; } = "";
        public string Phase { get; set; } = "";
        public string Status { get; set; } = "";
        public bool IsActive { get; set; }
        public DateTime? OpenDate { get; set; }
        public DateTime? CloseDate { get; set; }
        public double Fee { get; set; }
        public double HourlyRevenue { get; set; }
        public double TotalFee => Fee + HourlyRevenue;
        public double FeeBilled { get; set; }
        public double UnpostedFeeBilled { get; set; }
        public double FeeBilledWithUnposted => FeeBilled + UnpostedFeeBilled;
        public double Outstanding { get; set; }
        public double Outstanding90Plus { get; set; }
        public double PercentBilled => TotalFee > 0 ? FeeBilled / TotalFee : 0;
        public double PercentBilledWithUnposted => TotalFee > 0 ? FeeBilledWithUnposted / TotalFee : 0;
        public bool   HasUnpostedBilling => UnpostedFeeBilled > 0.004;
    }

    /// <summary>
    /// Aggregated lifetime metrics for a single client across ALL their projects (active + closed).
    /// Loaded independently of the active-projects-only snapshot that drives the project grid.
    /// </summary>
    public sealed class ClientRollupRow
    {
        public string ClientId { get; set; } = "";
        public string ClientName { get; set; } = "";
        public int ProjectCount { get; set; }
        public int ActiveProjectCount { get; set; }
        public double LifetimeFee { get; set; }
        public double LifetimeBilled { get; set; }
        public double LifetimeUnpostedBilled { get; set; }
        public double LifetimeBilledWithUnposted => LifetimeBilled + LifetimeUnpostedBilled;
        public double Outstanding { get; set; }
        public double Outstanding90Plus { get; set; }
        public DateTime? FirstProjectDate { get; set; }
        public DateTime? LastActivityDate { get; set; }
        public double LargestProjectFee { get; set; }
        public List<ClientProjectRow> Projects { get; set; } = new();

        public double PercentBilled => LifetimeFee > 0 ? LifetimeBilled / LifetimeFee : 0;
        public double PercentBilledWithUnposted => LifetimeFee > 0 ? LifetimeBilledWithUnposted / LifetimeFee : 0;
        public bool   HasUnpostedBilling => LifetimeUnpostedBilled > 0.004;
        public double AvgFeePerProject => ProjectCount > 0 ? LifetimeFee / ProjectCount : 0;
        public bool IsRepeatClient => ProjectCount > 1;
        public double YearsAsClient => FirstProjectDate.HasValue
            ? Math.Max(0, (DateTime.Today - FirstProjectDate.Value).TotalDays / 365.25)
            : 0;
        /// <summary>True when client has no active projects AND no activity in the last 12 months.</summary>
        public bool IsCold => ActiveProjectCount == 0
            && LastActivityDate.HasValue
            && (DateTime.Today - LastActivityDate.Value).TotalDays > 365;
        /// <summary>Has significant aged AR (>$5K at 90+ days).</summary>
        public bool HasArRisk => Outstanding90Plus > 5000;
        public string TenureDisplay => YearsAsClient >= 1
            ? $"{YearsAsClient:N1} yr"
            : YearsAsClient > 0 ? $"{YearsAsClient * 12:N0} mo" : "—";
        public string LastActivityDisplay => LastActivityDate.HasValue
            ? LastActivityDate.Value.ToString("MMM yyyy")
            : "—";
    }

    public enum HotlistSyncState
    {
        Idle,
        Pending,
        Error
    }

    public sealed class FinancialsProjectRow : System.ComponentModel.INotifyPropertyChanged
    {
        public string Wbs1 { get; set; } = "";
        public string Name { get; set; } = "";
        public string Phase { get; set; } = "";
        public string Pm { get; set; } = "";
        public string DraftingManager { get; set; } = "";
        public string BillingManager { get; set; } = "";
        public string ConstructionType { get; set; } = "";
        public string ProjectCategory { get; set; } = "";
        public string DraftingType { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string ClientName { get; set; } = "";

        public double Gfa { get; set; }
        public double Fee { get; set; }
        public double HourlyRevenue { get; set; }
        public double TotalFee => Fee + HourlyRevenue;
        public double FeeBilled { get; set; }
        public double UnpostedFeeBilled { get; set; }
        public double FeeBilledWithUnposted => FeeBilled + UnpostedFeeBilled;
        public double SubconsultantCost { get; set; }
        public double PercentBilled { get; set; }
        public double PercentBilledWithUnposted => TotalFee > 0 ? FeeBilledWithUnposted / TotalFee : 0;
        public bool   HasUnpostedBilling => UnpostedFeeBilled > 0.004;

        public double EngHrs { get; set; }
        public double DraftHrs { get; set; }
        public double InspHrs { get; set; }
        public double DocPrepHrs { get; set; }
        public double GenHrs { get; set; }
        public double AdminHrs { get; set; }
        public double NonBillHrs { get; set; }
        public double EngLaborCost { get; set; }
        public double DraftLaborCost { get; set; }
        public double InspLaborCost { get; set; }
        public double DocPrepLaborCost { get; set; }
        public double GenLaborCost { get; set; }
        public double AdminLaborCost { get; set; }
        public double NonBillLaborCost { get; set; }

        public double DraftBudget { get; set; }
        public double EngBudget { get; set; }
        public double EngBudgetActual { get; set; }
        public double DraftBudgetActual { get; set; }
        public double DraftPercent { get; set; }
        public double EngPercent { get; set; }
        public double FeePerHours { get; set; }
        public double BilledPerHours { get; set; }
        public double BilledPerHoursWithUnposted { get; set; }

        /// <summary>
        /// Delivery confidence result computed once by <see cref="FinancialsService"/>.
        /// Row models (PmProjectRow, UtilizationRow, DraftUtilizationRow) read this
        /// instead of re-running the calculator, reducing 3× work to 1×.
        /// </summary>
        public int TotalInspections { get; set; }
        public int LastMonthInspections { get; set; }

        /// <summary>Number of peer projects used for budget estimate (0 = formula fallback).</summary>
        public int BudgetPeerCount { get; set; }
        public string BudgetSource { get; set; } = "Formula";

        internal DeliveryConfidenceCalculator.Result? DeliveryResult { get; set; }
        public string HealthStatus => DeliveryResult?.Status ?? "High Confidence";

        // ── Hotlist (CustWatchlist) — only INPC members to support optimistic-UI toggling ──
        private bool _isOnHotlist;
        public bool IsOnHotlist
        {
            get => _isOnHotlist;
            set
            {
                if (_isOnHotlist == value) return;
                _isOnHotlist = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsOnHotlist)));
            }
        }

        private HotlistSyncState _hotlistSyncState;
        public HotlistSyncState HotlistSyncState
        {
            get => _hotlistSyncState;
            set
            {
                if (_hotlistSyncState == value) return;
                _hotlistSyncState = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(HotlistSyncState)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsHotlistSyncing)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(HasHotlistError)));
            }
        }

        private string? _hotlistSyncError;
        public string? HotlistSyncError
        {
            get => _hotlistSyncError;
            set
            {
                if (_hotlistSyncError == value) return;
                _hotlistSyncError = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(HotlistSyncError)));
            }
        }

        public bool IsHotlistSyncing => _hotlistSyncState == HotlistSyncState.Pending;
        public bool HasHotlistError => _hotlistSyncState == HotlistSyncState.Error;

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
}
