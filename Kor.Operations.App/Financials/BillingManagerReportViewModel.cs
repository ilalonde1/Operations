#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.Options;
using Kor.Operations.Core;
using Kor.Operations.Data;
using Serilog;

namespace Kor.Operations.Financials
{
    /// <summary>One row in the flat DataGrid — either a manager summary header or a project detail row.</summary>
    public sealed class BillingManagerFlatRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded))); }
        }

        // ── identity ──────────────────────────────────────────────────────────
        public bool   IsHeader  { get; }
        public string Label     { get; }
        public string Wbs1      { get; }
        public string InfoText  { get; }

        // ── period metrics ────────────────────────────────────────────────────
        public double   TotalBilled     { get; }   // last 12 months
        public double   LastMo          { get; }   // last closed period
        public double   TwoMoAgo        { get; }   // period before that
        public double[] MonthlyBilled   { get; }   // 12 values oldest→newest
        public double   BarWidth        { get; set; }
        // "—" for zero values so the grid doesn't fill with $0
        public string LastMoText   => Math.Abs(LastMo)   < AnalyticsThresholds.RoundingDollarFloor ? "—" : LastMo.ToString("C0",   CultureInfo.CurrentCulture);
        public string TwoMoAgoText => Math.Abs(TwoMoAgo) < AnalyticsThresholds.RoundingDollarFloor ? "—" : TwoMoAgo.ToString("C0", CultureInfo.CurrentCulture);

        // ── activity ──────────────────────────────────────────────────────────
        public int    Streak            { get; }
        public string StreakText        { get; }
        public string AllTimeAvgText    { get; }

        // ── fee health ────────────────────────────────────────────────────────
        public double AllTimeBilled     { get; }
        public double TotalFee          { get; }
        public double RemainingFee      { get; }
        public string RemFeeText        { get; }
        public bool   RemFeeNegative    { get; }
        public string PctFeeBilledText  { get; }
        public string PctFeeBilledColor { get; }   // hex foreground
        public double PctFeeBilledValue { get; }   // 0–1 clamped, for data bar

        // ── year-over-year ────────────────────────────────────────────────────
        public double YoyDelta     { get; }
        public string YoyDeltaText { get; }
        public bool   YoyPositive  { get; }
        public bool   YoyNeutral   { get; }

        // ── trend ─────────────────────────────────────────────────────────────
        public string TrendArrow    { get; }
        public bool   TrendPositive { get; }
        public bool   TrendNeutral  { get; }
        public string SparklineColor { get; }   // hex stroke matching trend direction

        // ── stale ─────────────────────────────────────────────────────────────
        public bool IsStale { get; }

        // ── constructor ───────────────────────────────────────────────────────
        private BillingManagerFlatRow(
            bool isHeader, string label, string wbs1, string infoText,
            double totalBilled, double lastMo, double twoMoAgo, double[] monthlyBilled,
            int streak, string streakText,
            string allTimeAvgText,
            double allTimeBilled, double totalFee, double remainingFee,
            string remFeeText, bool remFeeNegative,
            string pctFeeBilledText, string pctFeeBilledColor, double pctFeeBilledValue,
            double yoyDelta, string yoyDeltaText, bool yoyPositive, bool yoyNeutral,
            string trendArrow, bool trendPositive, bool trendNeutral,
            string sparklineColor,
            bool isStale)
        {
            IsHeader          = isHeader;
            Label             = label;
            Wbs1              = wbs1;
            InfoText          = infoText;
            TotalBilled       = totalBilled;
            LastMo            = lastMo;
            TwoMoAgo          = twoMoAgo;
            MonthlyBilled     = monthlyBilled;
            Streak            = streak;
            StreakText        = streakText;
            AllTimeAvgText    = allTimeAvgText;
            AllTimeBilled     = allTimeBilled;
            TotalFee          = totalFee;
            RemainingFee      = remainingFee;
            RemFeeText        = remFeeText;
            RemFeeNegative    = remFeeNegative;
            PctFeeBilledText  = pctFeeBilledText;
            PctFeeBilledColor = pctFeeBilledColor;
            PctFeeBilledValue = pctFeeBilledValue;
            YoyDelta          = yoyDelta;
            YoyDeltaText      = yoyDeltaText;
            YoyPositive       = yoyPositive;
            YoyNeutral        = yoyNeutral;
            TrendArrow        = trendArrow;
            TrendPositive     = trendPositive;
            TrendNeutral      = trendNeutral;
            SparklineColor    = sparklineColor;
            IsStale           = isStale;
        }

        // ── factory: manager header row ───────────────────────────────────────
        public static BillingManagerFlatRow Header(
            string managerName, int projectCount, double[] monthly12,
            double allTimeBilled, int allTimePeriods, double totalFee,
            int staleCount, int activeProjectCount, double topProjectPct,
            double prior12Total, int lastMoPeriodIdx)
        {
            var total  = monthly12.Sum();
            var lastMo = lastMoPeriodIdx >= 0 ? monthly12[lastMoPeriodIdx] : 0.0;
            var twoMo  = lastMoPeriodIdx >= 1 ? monthly12[lastMoPeriodIdx - 1] : 0.0;

            var yoyNeutral = prior12Total == 0.0;
            var yoyDelta   = total - prior12Total;
            var yoyText    = yoyNeutral ? "—" : FormatDelta(yoyDelta);

            var (arrow, trendPos, trendNeu) = ComputeTrend(monthly12, lastMoPeriodIdx);

            var remFee  = totalFee > 0 ? totalFee - allTimeBilled : 0.0;
            var remText = totalFee > 0 ? remFee.ToString("C0", CultureInfo.CurrentCulture) : "—";
            var pctFee  = Math.Abs(totalFee) > AnalyticsThresholds.RoundingDollarFloor ? allTimeBilled / totalFee : -1.0;
            var (pctFeeText, pctFeeColor) = FormatPctFee(pctFee);
            var pctFeeValue = pctFee < 0 ? 0.0 : Math.Min(1.0, pctFee);

            var avgText    = allTimePeriods > 0
                ? (allTimeBilled / allTimePeriods).ToString("C0", CultureInfo.CurrentCulture) : "—";
            var streakText = $"{activeProjectCount}/{projectCount}";

            var infoText = $"{projectCount} project{(projectCount == 1 ? "" : "s")}";
            if (staleCount > 0)       infoText += $" · ⚠ {staleCount} stalled";
            if (topProjectPct > 0.50) infoText += $" · Top: {topProjectPct:P0}";

            var sparklineColor = trendPos ? "#FF16A34A" : trendNeu ? "#FF6B7280" : "#FFEF4444";

            return new BillingManagerFlatRow(
                true, managerName, "", infoText,
                total, lastMo, twoMo, monthly12,
                activeProjectCount, streakText,
                avgText,
                allTimeBilled, totalFee, remFee, remText, totalFee > 0 && remFee < 0,
                pctFeeText, pctFeeColor, pctFeeValue,
                yoyDelta, yoyText, !yoyNeutral && yoyDelta >= 0, yoyNeutral,
                arrow, trendPos, trendNeu, sparklineColor,
                false);
        }

        // ── factory: project detail row ───────────────────────────────────────
        public static BillingManagerFlatRow Detail(
            string wbs1, string projectName, string phase, double[] monthly12,
            double allTimeBilled, int allTimePeriods, double fee,
            double prior12Total, int lastMoPeriodIdx)
        {
            var label  = $"    {wbs1}  ·  {projectName}";
            var total  = monthly12.Sum();
            var lastMo = lastMoPeriodIdx >= 0 ? monthly12[lastMoPeriodIdx] : 0.0;
            var twoMo  = lastMoPeriodIdx >= 1 ? monthly12[lastMoPeriodIdx - 1] : 0.0;

            var yoyNeutral = prior12Total == 0.0;
            var yoyDelta   = total - prior12Total;
            var yoyText    = yoyNeutral ? "—" : FormatDelta(yoyDelta);

            var (arrow, trendPos, trendNeu) = ComputeTrend(monthly12, lastMoPeriodIdx);

            var remFee  = fee > 0 ? fee - allTimeBilled : 0.0;
            var remText = fee > 0 ? remFee.ToString("C0", CultureInfo.CurrentCulture) : "—";
            var pctFee  = Math.Abs(fee) > AnalyticsThresholds.RoundingDollarFloor ? allTimeBilled / fee : -1.0;
            var (pctFeeText, pctFeeColor) = FormatPctFee(pctFee);
            var pctFeeValue = pctFee < 0 ? 0.0 : Math.Min(1.0, pctFee);

            int streak = 0;
            for (int i = lastMoPeriodIdx; i >= 0; i--)
            {
                if (Math.Abs(monthly12[i]) > AnalyticsThresholds.RoundingDollarFloor) streak++;
                else break;
            }
            var streakText = streak == 0 ? "—" : $"{streak} mo";

            var avgText  = allTimePeriods > 0
                ? (allTimeBilled / allTimePeriods).ToString("C0", CultureInfo.CurrentCulture) : "—";
            var infoText = string.IsNullOrWhiteSpace(phase) ? "" : phase;

            var checkCount = Math.Min(3, lastMoPeriodIdx + 1);
            var isStale    = checkCount > 0 &&
                             Enumerable.Range(lastMoPeriodIdx - checkCount + 1, checkCount)
                                       .All(i => Math.Abs(monthly12[i]) < AnalyticsThresholds.RoundingDollarFloor);

            var sparklineColor = trendPos ? "#FF16A34A" : trendNeu ? "#FF6B7280" : "#FFEF4444";

            return new BillingManagerFlatRow(
                false, label, wbs1, infoText,
                total, lastMo, twoMo, monthly12,
                streak, streakText,
                avgText,
                allTimeBilled, fee, remFee, remText, fee > 0 && remFee < 0,
                pctFeeText, pctFeeColor, pctFeeValue,
                yoyDelta, yoyText, !yoyNeutral && yoyDelta >= 0, yoyNeutral,
                arrow, trendPos, trendNeu,
                sparklineColor,
                isStale);
        }

        // ── helpers ───────────────────────────────────────────────────────────
        private static string FormatDelta(double delta)
        {
            var prefix = delta >= 0 ? "+" : "−";
            return $"{prefix}{Math.Abs(delta).ToString("C0", CultureInfo.CurrentCulture)}";
        }

        private static (string text, string color) FormatPctFee(double pctFee)
        {
            if (pctFee < 0) return ("—", "#FF9CA3AF");
            var p     = pctFee * 100;
            var color = p < 50  ? "#FF6B7280"
                      : p < 85  ? "#FF16A34A"
                      : p < 95  ? "#FFD97706"
                      :           "#FFDC2626";
            return ($"{p:F0}%", color);
        }

        private static (string arrow, bool positive, bool neutral) ComputeTrend(double[] m, int lastIdx)
        {
            if (lastIdx < 5) return ("→", false, true);
            var last3 = (m[lastIdx] + m[lastIdx - 1] + m[lastIdx - 2]) / 3.0;
            var prev3 = (m[lastIdx - 3] + m[lastIdx - 4] + m[lastIdx - 5]) / 3.0;
            if (prev3 < 0.01)
                return last3 > 0.01 ? ("↑", true, false) : ("→", false, true);
            if (last3 > prev3 * 1.05) return ("↑", true,  false);
            if (last3 < prev3 * 0.95) return ("↓", false, false);
            return ("→", false, true);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    public sealed class BillingManagerChartEntry
    {
        public string ManagerName         { get; }
        public double TotalBilled         { get; }
        public double BarWidth            { get; set; }
        public bool   TrendPositive       { get; }
        public bool   TrendNeutral        { get; }
        public string YoyDeltaText        { get; }
        public double YoyPositiveBarWidth { get; set; }
        public double YoyNegativeBarWidth { get; set; }
        public string LegendColor         { get; set; } = "";

        public BillingManagerChartEntry(
            string name, double total,
            bool trendPositive, bool trendNeutral,
            string yoyDeltaText)
        {
            ManagerName   = name;
            TotalBilled   = total;
            TrendPositive = trendPositive;
            TrendNeutral  = trendNeutral;
            YoyDeltaText  = yoyDeltaText;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    public sealed class BillingManagerReportViewModel : ObservableObject
    {
        private readonly VpOdbcDsnFactory _factory;
        private readonly string _catalog;
        private bool _isLoading;
        private string _errorMessage = "";
        private string _lastMoPeriodLabel = "";
        private string _twoMoAgoPeriodLabel = "";
        private readonly Dictionary<BillingManagerFlatRow, List<BillingManagerFlatRow>> _detailsByHeader = new();

        public ObservableCollection<BillingManagerFlatRow>    Rows         { get; } = new();
        public ObservableCollection<BillingManagerChartEntry> ChartEntries { get; } = new();
        public ObservableCollection<BillingMonthBar>          MonthBars    { get; } = new();

        private static readonly string[] _monthAbbr =
            { "Jan","Feb","Mar","Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec" };

        private static readonly string[] _barPalette =
        {
            "#FF3B82F6", "#FFEF4444", "#FF16A34A", "#FFD97706", "#FF8B5CF6",
            "#FF0891B2", "#FFDB2777", "#FF65A30D", "#FFF97316", "#FF475569"
        };

        public bool IsLoading
        {
            get => _isLoading;
            private set { _isLoading = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusHint)); }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set { _errorMessage = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(HasError)); }
        }

        public string LastMoPeriodLabel
        {
            get => _lastMoPeriodLabel;
            private set { _lastMoPeriodLabel = value; OnPropertyChanged(); }
        }

        public string TwoMoAgoPeriodLabel
        {
            get => _twoMoAgoPeriodLabel;
            private set { _twoMoAgoPeriodLabel = value; OnPropertyChanged(); }
        }

        public bool   HasError   => !string.IsNullOrWhiteSpace(_errorMessage);
        public string StatusHint => _isLoading ? "Loading..." : "";

        public BillingManagerReportViewModel(VpOdbcDsnFactory factory, DeltekOdbcOptions opts)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _catalog = DeltekCatalogValidator.ResolveCatalog(
                !string.IsNullOrWhiteSpace(opts?.Catalog) ? opts.Catalog : ExecutiveSummaryLoaderSupport.Catalog);
        }

        // ── toggle expand/collapse ────────────────────────────────────────────
        public void ToggleManager(BillingManagerFlatRow header)
        {
            if (!header.IsHeader) return;
            header.IsExpanded = !header.IsExpanded;

            var idx = Rows.IndexOf(header);
            if (idx < 0) return;

            if (header.IsExpanded)
            {
                if (_detailsByHeader.TryGetValue(header, out var details))
                    for (int i = 0; i < details.Count; i++)
                        Rows.Insert(idx + 1 + i, details[i]);
            }
            else
            {
                while (idx + 1 < Rows.Count && !Rows[idx + 1].IsHeader)
                    Rows.RemoveAt(idx + 1);
            }
        }

        // ── main refresh ──────────────────────────────────────────────────────
        public async Task RefreshAsync(FinancialsSnapshot snap, CancellationToken ct)
        {
            if (_isLoading) return;
            IsLoading    = true;
            ErrorMessage = "";

            try
            {
                var snapRows = snap.Rows;
                if (snapRows.Count == 0)
                {
                    Rows.Clear(); ChartEntries.Clear(); MonthBars.Clear(); _detailsByHeader.Clear();
                    LastMoPeriodLabel = "";
                    TwoMoAgoPeriodLabel = "";
                    return;
                }

                // Use last-wins on duplicate WBS1 (defensive — should not occur with per-project snapshots)
                // USA-org rows are billed/feed in USD; FX-convert via snap.UsdToCadRate so manager
                // rollups (which mix CAD + USA projects under one principal) come out CAD-equivalent.
                var fxByWbs1 = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                var principalByWbs1 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var metaByWbs1      = new Dictionary<string, (string? name, string? phase, double fee)>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in snapRows)
                {
                    var fx = OrgFx.IsUsaOrg(r.Org) ? snap.UsdToCadRate : 1.0;
                    fxByWbs1[r.Wbs1] = fx;
                    principalByWbs1[r.Wbs1] = string.IsNullOrWhiteSpace(r.BillingManager) ? "Unassigned" : r.BillingManager.Trim();
                    metaByWbs1[r.Wbs1]      = (r.Name, r.Phase, r.TotalFee * fx);
                }

                var wbs1List = snapRows.Select(r => r.Wbs1).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                var (allPeriods, byWbsPeriod) = await Task.Run(
                    () => LoadPeriodsByWbs1(wbs1List, _catalog, fxByWbs1, ct), ct).ConfigureAwait(true);

                if (allPeriods.Count == 0)
                {
                    Rows.Clear(); ChartEntries.Clear(); MonthBars.Clear(); _detailsByHeader.Clear();
                    LastMoPeriodLabel = "";
                    TwoMoAgoPeriodLabel = "";
                    return;
                }

                var last12  = allPeriods.TakeLast(12).ToList();
                var prior12 = allPeriods.Count >= 13
                    ? allPeriods.SkipLast(12).TakeLast(12).ToList()
                    : new List<string>();

                // Find the last period that has actual billing — avoids showing $0 columns
                // when the current accounting period hasn't been processed yet.
                int lastMoPeriodIdx = last12.Count - 1;
                while (lastMoPeriodIdx > 0)
                {
                    var p = last12[lastMoPeriodIdx];
                    if (byWbsPeriod.Values.Any(pm => pm.TryGetValue(p, out var v) && Math.Abs(v) > AnalyticsThresholds.RoundingDollarFloor))
                        break;
                    lastMoPeriodIdx--;
                }

                LastMoPeriodLabel = FormatPeriodLabel(last12[lastMoPeriodIdx]);
                TwoMoAgoPeriodLabel = lastMoPeriodIdx >= 1
                    ? FormatPeriodLabel(last12[lastMoPeriodIdx - 1])
                    : "";

                // ── helpers ───────────────────────────────────────────────────
                var empty = new Dictionary<string, double>();

                double[] Monthly12(IReadOnlyDictionary<string, double> pm)
                    => last12.Select(p => pm.TryGetValue(p, out var v) ? v : 0.0).ToArray();

                double AllTimeBilledFor(IReadOnlyDictionary<string, double>? pm)
                    => pm == null ? 0.0 : pm.Values.Sum();

                double Prior12Total(IReadOnlyDictionary<string, double>? pm)
                    => prior12.Count == 0 || pm == null ? 0.0
                       : prior12.Sum(p => pm.TryGetValue(p, out var v) ? v : 0.0);

                // ── group by principal ────────────────────────────────────────
                var byPrincipal = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var wbs1 in wbs1List)
                {
                    principalByWbs1.TryGetValue(wbs1, out var mgr);
                    mgr = string.IsNullOrWhiteSpace(mgr) ? "Unassigned" : mgr.Trim();
                    if (!byPrincipal.TryGetValue(mgr, out var list))
                        byPrincipal[mgr] = list = new List<string>();
                    list.Add(wbs1);
                }

                // ── build groups ──────────────────────────────────────────────
                var groups = byPrincipal
                    .Select(kvp =>
                    {
                        var projectRows = kvp.Value
                            .Select(w =>
                            {
                                byWbsPeriod.TryGetValue(w, out var pm);
                                metaByWbs1.TryGetValue(w, out var meta);
                                return BillingManagerFlatRow.Detail(
                                    w, meta.name ?? w, meta.phase ?? "",
                                    Monthly12(pm ?? empty),
                                    AllTimeBilledFor(pm),
                                    pm?.Count ?? 0,
                                    meta.fee,
                                    Prior12Total(pm),
                                    lastMoPeriodIdx);
                            })
                            .OrderByDescending(r => r.TotalBilled)
                            .ToList();

                        var mgrMonthly12 = last12
                            .Select(p => kvp.Value.Sum(w =>
                                byWbsPeriod.TryGetValue(w, out var pm) && pm.TryGetValue(p, out var v) ? v : 0.0))
                            .ToArray();

                        var mgrAllTime   = projectRows.Sum(r => r.AllTimeBilled);
                        var mgrFee       = projectRows.Sum(r => r.TotalFee);
                        var mgrPrior12T  = kvp.Value.Sum(w =>
                        {
                            byWbsPeriod.TryGetValue(w, out var pm);
                            return Prior12Total(pm);
                        });
                        var mgrAllTimePeriods = kvp.Value
                            .SelectMany(w => byWbsPeriod.TryGetValue(w, out var pm)
                                ? pm.Keys : Enumerable.Empty<string>())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Count();

                        var staleCount  = projectRows.Count(r => r.IsStale);
                        var activeCount = projectRows.Count(r => Math.Abs(r.LastMo) > AnalyticsThresholds.RoundingDollarFloor);
                        var mgr12Total  = mgrMonthly12.Sum();
                        var maxProj12   = projectRows.Count > 0 ? projectRows.Max(r => r.TotalBilled) : 0.0;
                        var topProjPct  = Math.Abs(mgr12Total) > AnalyticsThresholds.RoundingDollarFloor ? maxProj12 / mgr12Total : 0.0;

                        var header = BillingManagerFlatRow.Header(
                            kvp.Key, kvp.Value.Count, mgrMonthly12,
                            mgrAllTime, mgrAllTimePeriods, mgrFee,
                            staleCount, activeCount, topProjPct,
                            mgrPrior12T, lastMoPeriodIdx);

                        return (header, projects: projectRows);
                    })
                    .OrderByDescending(g => g.header.TotalBilled)
                    .ToList();

                // ── compute all chart entry values before touching collections ──
                var maxTotal = groups.Count > 0 ? groups.Max(g => g.header.TotalBilled) : 1.0;
                if (maxTotal <= 0) maxTotal = 1.0;
                foreach (var g in groups)
                    g.header.BarWidth = g.header.TotalBilled / maxTotal * 250.0;

                var maxYoyAbs = groups.Count > 0 ? groups.Max(g => Math.Abs(g.header.YoyDelta)) : 1.0;
                if (maxYoyAbs <= 0) maxYoyAbs = 1.0;

                var newEntries = groups.Select((g, i) =>
                {
                    var delta  = g.header.YoyDelta;
                    var normed = Math.Abs(delta) / maxYoyAbs * 85.0;
                    return new BillingManagerChartEntry(
                        g.header.Label, g.header.TotalBilled,
                        g.header.TrendPositive, g.header.TrendNeutral,
                        g.header.YoyDeltaText)
                    {
                        BarWidth            = g.header.BarWidth,
                        YoyPositiveBarWidth = delta >= 0 ? normed : 0.0,
                        YoyNegativeBarWidth = delta <  0 ? normed : 0.0,
                        LegendColor         = _barPalette[i % _barPalette.Length]
                    };
                }).ToList();

                // ── populate collections — all values fully computed before Add ─
                Rows.Clear();
                ChartEntries.Clear();
                _detailsByHeader.Clear();
                foreach (var ((header, projects), entry) in groups.Zip(newEntries))
                {
                    _detailsByHeader[header] = projects;
                    Rows.Add(header);
                    ChartEntries.Add(entry);
                }

                // ── 60-month stacked bar chart ─────────────────────────────────────
                var last60 = allPeriods.TakeLast(60).ToList();
                var maxMonthTotal = 0.0;

                var rawMonthData = last60.Select(period =>
                {
                    var perMgr = groups
                        .Select((g, idx) =>
                        {
                            var val = g.projects.Sum(r =>
                            {
                                byWbsPeriod.TryGetValue(r.Wbs1, out var pm);
                                return pm != null && pm.TryGetValue(period, out var v) ? v : 0.0;
                            });
                            return (idx, val);
                        })
                        .Where(x => Math.Abs(x.val) > AnalyticsThresholds.RoundingDollarFloor)
                        .ToList();
                    var total = perMgr.Sum(x => x.val);
                    if (total > maxMonthTotal) maxMonthTotal = total;
                    return (period, total, perMgr);
                }).ToList();

                if (maxMonthTotal <= 0) maxMonthTotal = 1.0;
                const double MaxBarH = 160.0;

                MonthBars.Clear();
                foreach (var (period, total, perMgr) in rawMonthData)
                {
                    var segments = perMgr
                        .OrderByDescending(x => x.val)
                        .Select(x => new BillingBarSegment(
                            Math.Max(4.0, x.val / maxMonthTotal * MaxBarH),
                            _barPalette[x.idx % _barPalette.Length],
                            $"{groups[x.idx].header.Label}: {x.val:C0}"))
                        .ToList();

                    var isJanuary  = period.Length == 6 && period[4..] == "01";
                    var yearLabel  = isJanuary ? period[..4] : "";
                    var monthLabel = period.Length == 6
                        && int.TryParse(period[4..], out var mo) && mo >= 1 && mo <= 12
                        ? _monthAbbr[mo - 1] : "";

                    MonthBars.Add(new BillingMonthBar(
                        FormatPeriodLabel(period), monthLabel, yearLabel, total, segments));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Error(ex, "BillingManagerReport load failed.");
                ErrorMessage = $"Failed to load billing data: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        // ── ODBC loader ───────────────────────────────────────────────────────
        // fxByWbs1 maps each WBS1 to its USD→CAD multiplier (1.0 for CAD projects, snap.UsdToCadRate
        // for USA-org projects). Applied in C# at storage time so all downstream period-level math
        // (last-mo, last-12, prior-12, manager rollups) is CAD-equivalent.
        private (List<string> sortedPeriods, Dictionary<string, Dictionary<string, double>> byWbsPeriod)
            LoadPeriodsByWbs1(List<string> wbs1List, string catalog, IReadOnlyDictionary<string, double> fxByWbs1, CancellationToken ct)
        {
            var byWbsPeriod = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
            var allPeriods  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var cn = _factory.Create();
            cn.Open();

            foreach (var chunk in ExecutiveSummaryLoaderSupport.Chunk(wbs1List, ExecutiveSummaryLoaderSupport.OdbcParameterChunkSize))
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.Batch;
                cmd.CommandText = $@"
SELECT WBS1, Period, SUM(COALESCE(Billed, 0)) AS Billed
FROM [{catalog}].dbo.PRSummaryMain
WHERE WBS1 IN ({ExecutiveSummaryLoaderSupport.MakeInListPlaceholders(chunk.Count)})
GROUP BY WBS1, Period;";
                ExecutiveSummaryLoaderSupport.AddInListParameters(cmd, chunk);

                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { /* best-effort cancel; may race with disposal */ } });
                using var r   = cmd.ExecuteReader();
                while (r.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var wbs1   = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 0);
                    var period = ExecutiveSummaryLoaderSupport.GetTrimmed(r, 1);
                    if (wbs1.Length == 0 || period.Length == 0) continue;
                    var fx = fxByWbs1.TryGetValue(wbs1, out var rate) ? rate : 1.0;
                    var billed = ExecutiveSummaryLoaderSupport.GetDouble(r, 2) * fx;
                    if (Math.Abs(billed) < AnalyticsThresholds.RoundingDollarFloor) continue;

                    if (!byWbsPeriod.TryGetValue(wbs1, out var pmap))
                        byWbsPeriod[wbs1] = pmap = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    pmap[period] = billed;
                    allPeriods.Add(period);
                }
            }

            var sorted = allPeriods.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            return (sorted, byWbsPeriod);
        }

        private static string FormatPeriodLabel(string period)
        {
            if (period.Length == 6 &&
                int.TryParse(period[..4], out var year) &&
                int.TryParse(period[4..], out var month) &&
                month >= 1 && month <= 12)
                return new DateTime(year, month, 1).ToString("MMM yy", CultureInfo.InvariantCulture);
            return period;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>One colored segment within a BillingMonthBar (represents one manager's share).</summary>
    public sealed class BillingBarSegment
    {
        public double SegmentHeight { get; set; }
        public string Color         { get; }
        public string Tooltip       { get; }

        public BillingBarSegment(double height, string color, string tooltip)
        {
            SegmentHeight = height;
            Color         = color;
            Tooltip       = tooltip;
        }
    }

    /// <summary>One vertical bar in the 60-month stacked billing history chart.</summary>
    public sealed class BillingMonthBar
    {
        public string PeriodLabel { get; }
        public string MonthLabel  { get; }   // 3-letter month abbreviation shown on every bar
        public string YearLabel   { get; }   // non-empty only for January periods
        public double TotalBilled { get; }
        public string Tooltip     => $"{PeriodLabel}  {TotalBilled:C0}";
        public IReadOnlyList<BillingBarSegment> Segments { get; }

        public BillingMonthBar(string periodLabel, string monthLabel, string yearLabel,
                               double totalBilled, IReadOnlyList<BillingBarSegment> segments)
        {
            PeriodLabel = periodLabel;
            MonthLabel  = monthLabel;
            YearLabel   = yearLabel;
            TotalBilled = totalBilled;
            Segments    = segments;
        }
    }
}
