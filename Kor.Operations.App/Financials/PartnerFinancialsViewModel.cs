#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Kor.Operations.App.Options;
using Kor.Operations.Core;
using Serilog;

namespace Kor.Operations.Financials
{
    public sealed class PartnerFinancialsFlatRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded))); }
        }

        public bool IsHeader { get; }
        public string Label { get; }
        public string Wbs1 { get; }
        public string InfoText { get; }
        public double TotalBilled { get; }
        public double LastMo { get; }
        public double TwoMoAgo { get; }
        public double[] MonthlyBilled { get; }
        public int Streak { get; }
        public string StreakText { get; }
        public double YoyDelta { get; }
        public string YoyDeltaText { get; }
        public bool YoyPositive { get; }
        public bool YoyNeutral { get; }
        public string TrendArrow { get; }
        public bool TrendPositive { get; }
        public bool TrendNeutral { get; }
        public string SparklineColor { get; }
        public bool IsStale { get; }
        public double BarWidth { get; set; }

        public string JanText => FormatMoney(MonthlyBilled[0]);
        public string FebText => FormatMoney(MonthlyBilled[1]);
        public string MarText => FormatMoney(MonthlyBilled[2]);
        public string AprText => FormatMoney(MonthlyBilled[3]);
        public string MayText => FormatMoney(MonthlyBilled[4]);
        public string JunText => FormatMoney(MonthlyBilled[5]);
        public string JulText => FormatMoney(MonthlyBilled[6]);
        public string AugText => FormatMoney(MonthlyBilled[7]);
        public string SepText => FormatMoney(MonthlyBilled[8]);
        public string OctText => FormatMoney(MonthlyBilled[9]);
        public string NovText => FormatMoney(MonthlyBilled[10]);
        public string DecText => FormatMoney(MonthlyBilled[11]);
        public string LastMoText => FormatMoney(LastMo);
        public string TwoMoAgoText => FormatMoney(TwoMoAgo);

        private PartnerFinancialsFlatRow(
            bool isHeader,
            string label,
            string wbs1,
            string infoText,
            double totalBilled,
            double lastMo,
            double twoMoAgo,
            double[] monthlyBilled,
            int streak,
            string streakText,
            double yoyDelta,
            string yoyDeltaText,
            bool yoyPositive,
            bool yoyNeutral,
            string trendArrow,
            bool trendPositive,
            bool trendNeutral,
            string sparklineColor,
            bool isStale)
        {
            IsHeader = isHeader;
            Label = label;
            Wbs1 = wbs1;
            InfoText = infoText;
            TotalBilled = totalBilled;
            LastMo = lastMo;
            TwoMoAgo = twoMoAgo;
            MonthlyBilled = monthlyBilled;
            Streak = streak;
            StreakText = streakText;
            YoyDelta = yoyDelta;
            YoyDeltaText = yoyDeltaText;
            YoyPositive = yoyPositive;
            YoyNeutral = yoyNeutral;
            TrendArrow = trendArrow;
            TrendPositive = trendPositive;
            TrendNeutral = trendNeutral;
            SparklineColor = sparklineColor;
            IsStale = isStale;
        }

        public static PartnerFinancialsFlatRow Header(string partnerName, int projectCount, double[] monthly12, double priorYearTotal, int lastMoPeriodIdx)
        {
            var total = monthly12.Sum();
            var lastMo = lastMoPeriodIdx >= 0 ? monthly12[lastMoPeriodIdx] : 0.0;
            var twoMo = lastMoPeriodIdx >= 1 ? monthly12[lastMoPeriodIdx - 1] : 0.0;
            var yoyNeutral = Math.Abs(priorYearTotal) < AnalyticsThresholds.RoundingDollarFloor;
            var yoyDelta = total - priorYearTotal;
            var (arrow, trendPos, trendNeu) = ComputeTrend(monthly12, lastMoPeriodIdx);
            var activeMonths = monthly12.Count(v => Math.Abs(v) > AnalyticsThresholds.RoundingDollarFloor);
            var sparklineColor = trendPos ? "#FF16A34A" : trendNeu ? "#FF6B7280" : "#FFEF4444";

            return new PartnerFinancialsFlatRow(
                true,
                partnerName,
                "",
                $"{projectCount} project{(projectCount == 1 ? "" : "s")} | {activeMonths}/12 active months",
                total,
                lastMo,
                twoMo,
                monthly12,
                activeMonths,
                activeMonths == 0 ? "-" : $"{activeMonths} mo",
                yoyDelta,
                yoyNeutral ? "-" : FormatDelta(yoyDelta),
                !yoyNeutral && yoyDelta >= 0,
                yoyNeutral,
                arrow,
                trendPos,
                trendNeu,
                sparklineColor,
                false);
        }

        public static PartnerFinancialsFlatRow Detail(string wbs1, string projectName, double[] monthly12, double priorYearTotal, int lastMoPeriodIdx)
        {
            var total = monthly12.Sum();
            var lastMo = lastMoPeriodIdx >= 0 ? monthly12[lastMoPeriodIdx] : 0.0;
            var twoMo = lastMoPeriodIdx >= 1 ? monthly12[lastMoPeriodIdx - 1] : 0.0;
            var yoyNeutral = Math.Abs(priorYearTotal) < AnalyticsThresholds.RoundingDollarFloor;
            var yoyDelta = total - priorYearTotal;
            var (arrow, trendPos, trendNeu) = ComputeTrend(monthly12, lastMoPeriodIdx);
            var streak = ComputeStreak(monthly12, lastMoPeriodIdx);
            var checkCount = Math.Min(3, lastMoPeriodIdx + 1);
            var isStale = checkCount > 0 &&
                          Enumerable.Range(lastMoPeriodIdx - checkCount + 1, checkCount)
                              .All(i => Math.Abs(monthly12[i]) < AnalyticsThresholds.RoundingDollarFloor);
            var sparklineColor = trendPos ? "#FF16A34A" : trendNeu ? "#FF6B7280" : "#FFEF4444";

            return new PartnerFinancialsFlatRow(
                false,
                $"    {wbs1}  |  {projectName}",
                wbs1,
                "",
                total,
                lastMo,
                twoMo,
                monthly12,
                streak,
                streak == 0 ? "-" : $"{streak} mo",
                yoyDelta,
                yoyNeutral ? "-" : FormatDelta(yoyDelta),
                !yoyNeutral && yoyDelta >= 0,
                yoyNeutral,
                arrow,
                trendPos,
                trendNeu,
                sparklineColor,
                isStale);
        }

        private static int ComputeStreak(double[] m, int lastIdx)
        {
            var streak = 0;
            for (var i = lastIdx; i >= 0; i--)
            {
                if (Math.Abs(m[i]) > AnalyticsThresholds.RoundingDollarFloor) streak++;
                else break;
            }
            return streak;
        }

        private static (string arrow, bool positive, bool neutral) ComputeTrend(double[] m, int lastIdx)
        {
            if (lastIdx < 5) return ("->", false, true);
            var last3 = (m[lastIdx] + m[lastIdx - 1] + m[lastIdx - 2]) / 3.0;
            var prev3 = (m[lastIdx - 3] + m[lastIdx - 4] + m[lastIdx - 5]) / 3.0;
            if (prev3 < 0.01)
                return last3 > 0.01 ? ("Up", true, false) : ("->", false, true);
            if (last3 > prev3 * 1.05) return ("Up", true, false);
            if (last3 < prev3 * 0.95) return ("Down", false, false);
            return ("->", false, true);
        }

        private static string FormatMoney(double value)
            => Math.Abs(value) < AnalyticsThresholds.RoundingDollarFloor
                ? "-"
                : value.ToString("C0", CultureInfo.CurrentCulture);

        private static string FormatDelta(double delta)
            => $"{(delta >= 0 ? "+" : "-")}{Math.Abs(delta).ToString("C0", CultureInfo.CurrentCulture)}";
    }

    public sealed class PartnerFinancialsChartEntry
    {
        public string PartnerName { get; }
        public double TotalBilled { get; }
        public double BarWidth { get; set; }
        public double YoyPositiveBarWidth { get; set; }
        public double YoyNegativeBarWidth { get; set; }
        public string YoyDeltaText { get; }
        public string LegendColor { get; set; } = "";

        public PartnerFinancialsChartEntry(string partnerName, double totalBilled, string yoyDeltaText)
        {
            PartnerName = partnerName;
            TotalBilled = totalBilled;
            YoyDeltaText = yoyDeltaText;
        }
    }

    public sealed class PartnerFinancialsMonthBar
    {
        public string PeriodLabel { get; }
        public string MonthLabel { get; }
        public string YearLabel { get; }
        public double TotalBilled { get; }
        public string Tooltip => $"{PeriodLabel}  {TotalBilled:C0}";
        public IReadOnlyList<PartnerFinancialsBarSegment> Segments { get; }

        public PartnerFinancialsMonthBar(string periodLabel, string monthLabel, string yearLabel, double totalBilled, IReadOnlyList<PartnerFinancialsBarSegment> segments)
        {
            PeriodLabel = periodLabel;
            MonthLabel = monthLabel;
            YearLabel = yearLabel;
            TotalBilled = totalBilled;
            Segments = segments;
        }
    }

    public sealed class PartnerFinancialsBarSegment
    {
        public double SegmentHeight { get; set; }
        public string Color { get; }
        public string Tooltip { get; }

        public PartnerFinancialsBarSegment(double segmentHeight, string color, string tooltip)
        {
            SegmentHeight = segmentHeight;
            Color = color;
            Tooltip = tooltip;
        }
    }

    public sealed class PartnerFinancialsYoyRow
    {
        public string Month { get; }
        public double LastYear { get; }
        public double CurrentYear { get; }
        public double Difference => CurrentYear - LastYear;
        public string DifferenceText => $"{(Difference >= 0 ? "+" : "-")}{Math.Abs(Difference).ToString("C0", CultureInfo.CurrentCulture)}";
        public string ChangePercentText { get; }
        public bool IsPositive => Difference >= 0;
        public bool IsNeutral => Math.Abs(LastYear) < AnalyticsThresholds.RoundingDollarFloor;

        public PartnerFinancialsYoyRow(string month, double lastYear, double currentYear)
        {
            Month = month;
            LastYear = lastYear;
            CurrentYear = currentYear;
            ChangePercentText = Math.Abs(lastYear) < AnalyticsThresholds.RoundingDollarFloor
                ? "-"
                : ((currentYear - lastYear) / lastYear).ToString("P0", CultureInfo.CurrentCulture);
        }
    }

    public sealed class PartnerFinancialsViewModel : ObservableObject
    {
        private static readonly string[] MonthAbbr =
            { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

        private static readonly string[] BarPalette =
        {
            "#FF3B82F6", "#FFEF4444", "#FF16A34A", "#FFD97706", "#FF8B5CF6",
            "#FF0891B2", "#FFDB2777", "#FF65A30D", "#FFF97316", "#FF475569"
        };

        private static readonly IReadOnlyDictionary<string, string> DisplayAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["James DesRoches"] = "Jim DesRoches"
            };

        private readonly BilledFinancialsService _service;
        private readonly FinancialsOptions _financialsOptions;
        private readonly Dictionary<PartnerFinancialsFlatRow, List<PartnerFinancialsFlatRow>> _detailsByHeader = new();
        private bool _isLoading;
        private string _errorMessage = "";
        private int _selectedYear;
        private double _resolvedUsdToCadRate;
        private bool _isFxProvisional;

        public ObservableCollection<int> Years { get; } = new() { 2024, 2025, 2026 };
        public ObservableCollection<PartnerFinancialsFlatRow> CombinedRows { get; } = new();
        public ObservableCollection<PartnerFinancialsFlatRow> CadRows { get; } = new();
        public ObservableCollection<PartnerFinancialsFlatRow> UsdRows { get; } = new();
        public ObservableCollection<PartnerFinancialsChartEntry> ChartEntries { get; } = new();
        public ObservableCollection<PartnerFinancialsMonthBar> MonthBars { get; } = new();
        public ObservableCollection<PartnerFinancialsYoyRow> YoyRows { get; } = new();

        public PartnerFinancialsViewModel(BilledFinancialsService service, FinancialsOptions financialsOptions)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _financialsOptions = financialsOptions ?? throw new ArgumentNullException(nameof(financialsOptions));
            _selectedYear = Years.Contains(DateTime.Today.Year) ? DateTime.Today.Year : Years.Last();
            ResolveFx();
        }

        public int SelectedYear
        {
            get => _selectedYear;
            set
            {
                var next = Years.Contains(value) ? value : Years.Last();
                if (_selectedYear == next) return;
                _selectedYear = next;
                ResolveFx();
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsFxProvisional));
                OnPropertyChanged(nameof(FxTooltip));
                _ = RefreshAsync(CancellationToken.None);
            }
        }

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

        public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
        public string StatusHint => IsLoading ? "Loading..." : "";
        public bool IsFxProvisional => _isFxProvisional;
        public string FxBadgeText => $"FX provisional";
        public string FxTooltip => $"USD billings are converted at {_resolvedUsdToCadRate:N6} CAD per USD for {SelectedYear}.";
        public string CombinedGridTitle => $"CAD+USD Combined ({SelectedYear})";
        public string CadGridTitle => $"CAD Only ({SelectedYear})";
        public string UsdGridTitle => $"USD Only ({SelectedYear})";
        public string AsOfLabel { get; private set; } = "";
        public Visibility AsOfVisibility => FinancialPostingPeriodLabels.VisibleWhenPresent(AsOfLabel);
        public string LastMoPeriodLabel { get; private set; } = "";
        public string TwoMoAgoPeriodLabel { get; private set; } = "";
        public double YoyLastYearTotal => YoyRows.Sum(r => r.LastYear);
        public double YoyCurrentYearTotal => YoyRows.Sum(r => r.CurrentYear);
        // Internal helper only — the bound surface is YoyDifferenceTotalText. Kept private so
        // UnusedViewModelPropertyTests doesn't read an unbound public property as dead code.
        private double YoyDifferenceTotal => YoyCurrentYearTotal - YoyLastYearTotal;
        public string YoyDifferenceTotalText => $"{(YoyDifferenceTotal >= 0 ? "+" : "-")}{Math.Abs(YoyDifferenceTotal).ToString("C0", CultureInfo.CurrentCulture)}";

        public async Task RefreshAsync(CancellationToken ct)
        {
            if (IsLoading) return;
            IsLoading = true;
            ErrorMessage = "";

            try
            {
                ResolveFx();
                var minPeriod = (SelectedYear - 4) * 100 + 1;
                var maxPeriod = SelectedYear * 100 + 12;
                var rows = await _service.LoadPartnerBilledRevenueByPeriodAsync(minPeriod, maxPeriod, ct).ConfigureAwait(true);
                ApplyRows(rows);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Error(ex, "PartnerFinancials load failed.");
                ErrorMessage = $"Failed to load partner financials: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        public void TogglePartner(PartnerFinancialsFlatRow header)
        {
            if (!header.IsHeader) return;
            header.IsExpanded = !header.IsExpanded;
            ToggleIn(CombinedRows, header);
            ToggleIn(CadRows, header);
            ToggleIn(UsdRows, header);
        }

        private void ToggleIn(ObservableCollection<PartnerFinancialsFlatRow> rows, PartnerFinancialsFlatRow header)
        {
            var idx = rows.IndexOf(header);
            if (idx < 0) return;

            if (header.IsExpanded)
            {
                if (_detailsByHeader.TryGetValue(header, out var details))
                {
                    for (var i = 0; i < details.Count; i++)
                        rows.Insert(idx + 1 + i, details[i]);
                }
            }
            else
            {
                while (idx + 1 < rows.Count && !rows[idx + 1].IsHeader)
                    rows.RemoveAt(idx + 1);
            }
        }

        internal void ApplyRows(IReadOnlyList<BilledFinancialsService.PartnerBilledRevenueRow> sourceRows)
        {
            var selectedPeriods = Enumerable.Range(1, 12).Select(m => SelectedYear * 100 + m).ToArray();
            var priorPeriods = Enumerable.Range(1, 12).Select(m => (SelectedYear - 1) * 100 + m).ToArray();
            var lastIdx = FindLastActivityIndex(sourceRows, selectedPeriods);
            AsOfLabel = FinancialPostingPeriodLabels.BilledThrough(selectedPeriods[Math.Max(0, lastIdx)]);
            LastMoPeriodLabel = FormatPeriodLabel(selectedPeriods[Math.Max(0, lastIdx)]);
            TwoMoAgoPeriodLabel = lastIdx >= 1 ? FormatPeriodLabel(selectedPeriods[lastIdx - 1]) : "";
            OnPropertyChanged(nameof(AsOfLabel));
            OnPropertyChanged(nameof(AsOfVisibility));
            OnPropertyChanged(nameof(LastMoPeriodLabel));
            OnPropertyChanged(nameof(TwoMoAgoPeriodLabel));

            _detailsByHeader.Clear();
            var combinedGroups = BuildGroups(sourceRows, selectedPeriods, priorPeriods, lastIdx, r => r.OrgBucket.Equals("USA", StringComparison.OrdinalIgnoreCase) ? _resolvedUsdToCadRate : 1.0, _ => true);
            var cadGroups = BuildGroups(sourceRows, selectedPeriods, priorPeriods, lastIdx, _ => 1.0, r => r.OrgBucket.Equals("CAD", StringComparison.OrdinalIgnoreCase));
            var usdGroups = BuildGroups(sourceRows, selectedPeriods, priorPeriods, lastIdx, _ => 1.0, r => r.OrgBucket.Equals("USA", StringComparison.OrdinalIgnoreCase));

            ReplaceRows(CombinedRows, combinedGroups);
            ReplaceRows(CadRows, cadGroups);
            ReplaceRows(UsdRows, usdGroups);
            BuildCharts(sourceRows, combinedGroups);
            BuildYoy(sourceRows, selectedPeriods, priorPeriods);

            OnPropertyChanged(nameof(IsFxProvisional));
            OnPropertyChanged(nameof(FxTooltip));
            OnPropertyChanged(nameof(CombinedGridTitle));
            OnPropertyChanged(nameof(CadGridTitle));
            OnPropertyChanged(nameof(UsdGridTitle));
        }

        private List<(PartnerFinancialsFlatRow Header, List<PartnerFinancialsFlatRow> Projects)> BuildGroups(
            IReadOnlyList<BilledFinancialsService.PartnerBilledRevenueRow> sourceRows,
            IReadOnlyList<int> selectedPeriods,
            IReadOnlyList<int> priorPeriods,
            int lastIdx,
            Func<BilledFinancialsService.PartnerBilledRevenueRow, double> fx,
            Func<BilledFinancialsService.PartnerBilledRevenueRow, bool> include)
        {
            var rows = sourceRows.Where(include).ToList();
            var partnerGroups = rows
                .GroupBy(r => (Id: NormalizePartnerId(r), Name: Alias(r.PartnerDisplayName)))
                .Select(g =>
                {
                    var projectRows = new List<PartnerFinancialsFlatRow>();
                    var projects = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var r in g)
                    {
                        var wbs1 = string.IsNullOrWhiteSpace(r.Wbs1) ? "(unassigned)" : r.Wbs1.Trim();
                        projects[wbs1] = string.IsNullOrWhiteSpace(r.ProjectName) ? wbs1 : r.ProjectName.Trim();
                    }

                    foreach (var p in projects.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        var projectSource = g.Where(r => string.Equals(string.IsNullOrWhiteSpace(r.Wbs1) ? "(unassigned)" : r.Wbs1.Trim(), p.Key, StringComparison.OrdinalIgnoreCase)).ToList();
                        var monthly = Monthly(projectSource, selectedPeriods, fx);
                        var priorTotal = Total(projectSource, priorPeriods, fx);
                        if (Math.Abs(monthly.Sum()) < AnalyticsThresholds.RoundingDollarFloor &&
                            Math.Abs(priorTotal) < AnalyticsThresholds.RoundingDollarFloor)
                            continue;
                        projectRows.Add(PartnerFinancialsFlatRow.Detail(p.Key, p.Value, monthly, priorTotal, lastIdx));
                    }

                    var headerMonthly = Monthly(g, selectedPeriods, fx);
                    var headerPrior = Total(g, priorPeriods, fx);
                    var header = PartnerFinancialsFlatRow.Header(g.Key.Name, projectRows.Count, headerMonthly, headerPrior, lastIdx);
                    return (Header: header, Projects: projectRows.OrderByDescending(r => r.TotalBilled).ToList());
                })
                .Where(g => Math.Abs(g.Header.TotalBilled) >= AnalyticsThresholds.RoundingDollarFloor ||
                            Math.Abs(g.Header.YoyDelta) >= AnalyticsThresholds.RoundingDollarFloor)
                .OrderByDescending(g => g.Header.TotalBilled)
                .ToList();

            return partnerGroups;
        }

        private void ReplaceRows(ObservableCollection<PartnerFinancialsFlatRow> target, List<(PartnerFinancialsFlatRow Header, List<PartnerFinancialsFlatRow> Projects)> groups)
        {
            target.Clear();
            foreach (var group in groups)
            {
                _detailsByHeader[group.Header] = group.Projects;
                target.Add(group.Header);
            }
        }

        private void BuildCharts(IReadOnlyList<BilledFinancialsService.PartnerBilledRevenueRow> sourceRows, List<(PartnerFinancialsFlatRow Header, List<PartnerFinancialsFlatRow> Projects)> groups)
        {
            ChartEntries.Clear();
            MonthBars.Clear();

            var maxTotal = groups.Count > 0 ? groups.Max(g => g.Header.TotalBilled) : 1.0;
            if (maxTotal <= 0) maxTotal = 1.0;
            var maxYoyAbs = groups.Count > 0 ? groups.Max(g => Math.Abs(g.Header.YoyDelta)) : 1.0;
            if (maxYoyAbs <= 0) maxYoyAbs = 1.0;

            foreach (var (group, index) in groups.Select((g, i) => (g, i)))
            {
                group.Header.BarWidth = group.Header.TotalBilled / maxTotal * 250.0;
                var yoyWidth = Math.Abs(group.Header.YoyDelta) / maxYoyAbs * 85.0;
                ChartEntries.Add(new PartnerFinancialsChartEntry(group.Header.Label, group.Header.TotalBilled, group.Header.YoyDeltaText)
                {
                    BarWidth = group.Header.BarWidth,
                    YoyPositiveBarWidth = group.Header.YoyDelta >= 0 ? yoyWidth : 0.0,
                    YoyNegativeBarWidth = group.Header.YoyDelta < 0 ? yoyWidth : 0.0,
                    LegendColor = BarPalette[index % BarPalette.Length]
                });
            }

            var last60 = BuildPeriodRange(SelectedYear - 4, SelectedYear);
            var maxMonthTotal = 0.0;
            var rawMonthData = last60.Select(period =>
            {
                var perPartner = groups.Select((g, idx) =>
                {
                    var partnerRows = sourceRows.Where(r => Alias(r.PartnerDisplayName).Equals(g.Header.Label, StringComparison.OrdinalIgnoreCase));
                    var value = partnerRows
                        .Where(r => r.Period == period)
                        .Sum(r => (double)r.Amount * (r.OrgBucket.Equals("USA", StringComparison.OrdinalIgnoreCase) ? _resolvedUsdToCadRate : 1.0));
                    return (idx, value);
                })
                .Where(x => Math.Abs(x.value) > AnalyticsThresholds.RoundingDollarFloor)
                .ToList();
                var total = perPartner.Sum(x => x.value);
                if (total > maxMonthTotal) maxMonthTotal = total;
                return (period, total, perPartner);
            }).ToList();

            if (maxMonthTotal <= 0) maxMonthTotal = 1.0;
            const double maxBarHeight = 160.0;

            foreach (var (period, total, perPartner) in rawMonthData)
            {
                var segments = perPartner
                    .OrderByDescending(x => x.value)
                    .Select(x => new PartnerFinancialsBarSegment(
                        Math.Max(4.0, x.value / maxMonthTotal * maxBarHeight),
                        BarPalette[x.idx % BarPalette.Length],
                        $"{groups[x.idx].Header.Label}: {x.value:C0}"))
                    .ToList();
                var month = period % 100;
                MonthBars.Add(new PartnerFinancialsMonthBar(
                    FormatPeriodLabel(period),
                    month >= 1 && month <= 12 ? MonthAbbr[month - 1] : "",
                    month == 1 ? (period / 100).ToString(CultureInfo.InvariantCulture) : "",
                    total,
                    segments));
            }
        }

        private void BuildYoy(IReadOnlyList<BilledFinancialsService.PartnerBilledRevenueRow> rows, IReadOnlyList<int> selectedPeriods, IReadOnlyList<int> priorPeriods)
        {
            YoyRows.Clear();
            for (var i = 0; i < 12; i++)
            {
                var ly = rows.Where(r => r.Period == priorPeriods[i]).Sum(CombinedAmount);
                var cy = rows.Where(r => r.Period == selectedPeriods[i]).Sum(CombinedAmount);
                YoyRows.Add(new PartnerFinancialsYoyRow(MonthAbbr[i], ly, cy));
            }
            OnPropertyChanged(nameof(YoyLastYearTotal));
            OnPropertyChanged(nameof(YoyCurrentYearTotal));
            OnPropertyChanged(nameof(YoyDifferenceTotalText));
        }

        private void ResolveFx()
        {
            var table = OrgFx.ParseUsdToCadRateTable(_financialsOptions.UsdToCadRateByYear);
            var fallback = OrgFx.ParseUsdToCadRate(_financialsOptions.BilledUsdToCadRate);
            var resolved = OrgFx.ResolveUsdToCadRate(table, SelectedYear, fallback);
            _resolvedUsdToCadRate = resolved.Rate;
            _isFxProvisional = resolved.IsProvisional;
        }

        private double CombinedAmount(BilledFinancialsService.PartnerBilledRevenueRow row)
            => (double)row.Amount * (row.OrgBucket.Equals("USA", StringComparison.OrdinalIgnoreCase) ? _resolvedUsdToCadRate : 1.0);

        private static double[] Monthly(IEnumerable<BilledFinancialsService.PartnerBilledRevenueRow> rows, IReadOnlyList<int> periods, Func<BilledFinancialsService.PartnerBilledRevenueRow, double> fx)
            => periods.Select(p => rows.Where(r => r.Period == p).Sum(r => (double)r.Amount * fx(r))).ToArray();

        private static double Total(IEnumerable<BilledFinancialsService.PartnerBilledRevenueRow> rows, IReadOnlyList<int> periods, Func<BilledFinancialsService.PartnerBilledRevenueRow, double> fx)
            => rows.Where(r => periods.Contains(r.Period)).Sum(r => (double)r.Amount * fx(r));

        private static int FindLastActivityIndex(IReadOnlyList<BilledFinancialsService.PartnerBilledRevenueRow> rows, IReadOnlyList<int> periods)
        {
            for (var i = periods.Count - 1; i >= 0; i--)
            {
                var period = periods[i];
                if (rows.Any(r => r.Period == period && Math.Abs((double)r.Amount) > AnalyticsThresholds.RoundingDollarFloor))
                    return i;
            }
            return periods.Count - 1;
        }

        private static int[] BuildPeriodRange(int startYear, int endYear)
            => Enumerable.Range(startYear, endYear - startYear + 1)
                .SelectMany(y => Enumerable.Range(1, 12).Select(m => y * 100 + m))
                .ToArray();

        private static string NormalizePartnerId(BilledFinancialsService.PartnerBilledRevenueRow row)
            => string.IsNullOrWhiteSpace(row.PartnerEmployeeId) ? Alias(row.PartnerDisplayName) : row.PartnerEmployeeId.Trim();

        private static string Alias(string name)
        {
            var trimmed = string.IsNullOrWhiteSpace(name) ? "Unassigned" : name.Trim();
            return DisplayAliases.TryGetValue(trimmed, out var alias) ? alias : trimmed;
        }

        private static string FormatPeriodLabel(int period)
        {
            var year = period / 100;
            var month = period % 100;
            return month >= 1 && month <= 12
                ? new DateTime(year, month, 1).ToString("MMM yy", CultureInfo.InvariantCulture)
                : period.ToString(CultureInfo.InvariantCulture);
        }
    }
}
