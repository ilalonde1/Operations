#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Kor.Operations.Core;
using Kor.Operations.Services;

namespace Kor.Operations.Financials
{
    public sealed class ExecutiveSummaryViewModel : ObservableObject, IAiContextProvider
    {
        private readonly ExecutiveSummaryService _svc;
        private bool _isLoading;
        private DateTimeOffset? _lastUpdated;
        private string _statusHint = "";

        public ExecutiveSummaryViewModel(ExecutiveSummaryService svc)
        {
            _svc = svc ?? throw new ArgumentNullException(nameof(svc));
        }

        public ObservableCollection<KpiCardVm> Kpis { get; } = new();
        public ObservableCollection<TrendCardVm> Trends { get; } = new();
        public ObservableCollection<AlertVm> Alerts { get; } = new();

        public string LastUpdatedDisplay =>
            _lastUpdated.HasValue
                ? _lastUpdated.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")
                : "Not yet";

        public string StatusHint => _statusHint ?? "";

        public DateTime? MaxPostedPeriod { get; private set; }

        public string ScopeLabel { get; private set; } = "";
        public int ScopeProjectCount { get; private set; }
        public string ScopeEcho => string.IsNullOrEmpty(ScopeLabel) ? "" : $"Scope: {ScopeLabel}  {ScopeProjectCount:N0} projects";
        public Visibility ScopeEchoVisibility => string.IsNullOrEmpty(ScopeLabel) ? Visibility.Collapsed : Visibility.Visible;

        public void SetScope(string label, int count)
        {
            ScopeLabel = label ?? "";
            ScopeProjectCount = Math.Max(0, count);
            OnPropertyChanged(nameof(ScopeLabel));
            OnPropertyChanged(nameof(ScopeProjectCount));
            OnPropertyChanged(nameof(ScopeEcho));
            OnPropertyChanged(nameof(ScopeEchoVisibility));
        }

        public string PostingLagBanner
        {
            get
            {
                var lag = PostingLagMonths;
                if (!MaxPostedPeriod.HasValue || lag <= 1) return "";
                var postedMonth = new DateTime(MaxPostedPeriod.Value.Year, MaxPostedPeriod.Value.Month, 1);
                var postedLabel = postedMonth.ToString("MMM yyyy", System.Globalization.CultureInfo.CurrentCulture);
                return lag == 2
                    ? $"Deltek posted through {postedLabel} — normal close lag."
                    : $"Deltek posted through {postedLabel} — figures may reflect a {lag}-month posting lag.";
            }
        }

        public Visibility PostingLagVisibility => PostingLagMonths >= 2 ? Visibility.Visible : Visibility.Collapsed;
        public string PostingLagSeverity => PostingLagMonths switch
        {
            2 => "Info",
            >= 3 => "Warning",
            _ => string.Empty
        };

        private int PostingLagMonths
        {
            get
            {
                if (!MaxPostedPeriod.HasValue) return 0;
                var postedMonth = new DateTime(MaxPostedPeriod.Value.Year, MaxPostedPeriod.Value.Month, 1);
                var currentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                return (currentMonth.Year - postedMonth.Year) * 12 + currentMonth.Month - postedMonth.Month;
            }
        }

        public async Task RefreshAsync(
            bool forceRefresh,
            FinancialsSnapshot? existingSnapshot,
            PortfolioTrendPoint[]? existingTrend,
            UtilizationRow[]? existingUtilRows,
            CancellationToken ct)
        {
            if (_isLoading) return;
            _isLoading = true;
            _statusHint = "Loading...";
            OnPropertyChanged(nameof(StatusHint));

            try
            {
                var result = await _svc.GetExecutiveSummaryAsync(forceRefresh, existingSnapshot, existingTrend, existingUtilRows, ct)
                    .ConfigureAwait(true);

                Apply(result);
                _lastUpdated = result.GeneratedAt;
                _statusHint = "";
                OnPropertyChanged(nameof(LastUpdatedDisplay));
                OnPropertyChanged(nameof(StatusHint));
            }
            catch (OperationCanceledException)
            {
                // no-op
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ExecutiveSummary] Refresh failed: {ex.GetType().Name}: {ex.Message}");
                _statusHint = "Data unavailable (summary refresh failed).";
                OnPropertyChanged(nameof(StatusHint));
            }
            finally
            {
                _isLoading = false;
            }
        }

        private void Apply(ExecutiveSummaryResult result)
        {
            MaxPostedPeriod = result.MaxPostedPeriod;
            OnPropertyChanged(nameof(MaxPostedPeriod));
            OnPropertyChanged(nameof(PostingLagBanner));
            OnPropertyChanged(nameof(PostingLagVisibility));
            OnPropertyChanged(nameof(PostingLagSeverity));

            Kpis.Clear();
            foreach (var k in result.Kpis)
                Kpis.Add(new KpiCardVm(k));

            Trends.Clear();
            foreach (var t in result.Trends)
            {
                Trends.Add(new TrendCardVm(t));
            }

            Alerts.Clear();
            foreach (var a in result.Alerts)
            {
                Alerts.Add(new AlertVm(
                    a.Title,
                    a.Message,
                    a.ProjectDrilldownRows,
                    a.ArOutstandingRows,
                    a.ArInvoiceRows,
                    a.BacklogRows,
                    a.BudgetBurnRows));
            }
        }

        string IAiContextProvider.ProviderName => "Executive Summary KPIs (Deltek + portfolio store)";

        bool IAiContextProvider.HasData => Kpis.Count > 0 || Trends.Count > 0 || Alerts.Count > 0;

        string IAiContextProvider.BuildContext()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Source: Executive Summary KPIs (Deltek + portfolio store)");
            if (_lastUpdated.HasValue)
                sb.AppendLine($"Last updated: {_lastUpdated.Value.LocalDateTime:yyyy-MM-dd HH:mm:ss}");
            if (MaxPostedPeriod.HasValue)
                sb.AppendLine($"Max posted period: {MaxPostedPeriod.Value:yyyy-MM-dd}");

            if (Kpis.Count > 0)
            {
                sb.AppendLine("KPI headlines:");
                foreach (var kpi in Kpis)
                {
                    sb.Append("  ");
                    sb.Append(kpi.Title);
                    sb.Append(": ");
                    sb.Append(kpi.ValueText);
                    if (!string.IsNullOrWhiteSpace(kpi.SubText))
                    {
                        sb.Append(" - ");
                        sb.Append(kpi.SubText);
                    }
                    if (!string.IsNullOrWhiteSpace(kpi.StatusMessage))
                    {
                        sb.Append(" (");
                        sb.Append(kpi.StatusMessage);
                        sb.Append(')');
                    }
                    sb.AppendLine();
                }
            }

            var cash = Kpis.FirstOrDefault(k => string.Equals(k.Title, "Cash Position", StringComparison.OrdinalIgnoreCase));
            if (cash?.CashAccountRows is { Count: > 0 })
            {
                sb.AppendLine("Cash accounts included:");
                foreach (var account in cash.CashAccountRows)
                    sb.AppendLine($"  {account.Company} {account.Account} ({account.Org}): {account.Balance:C0}");
            }

            if (Trends.Count > 0)
            {
                sb.AppendLine("Trend headlines:");
                foreach (var trend in Trends)
                    sb.AppendLine($"  {trend.Title}: {trend.ValueText}");
            }

            if (Alerts.Count > 0)
            {
                sb.AppendLine("Alerts:");
                foreach (var alert in Alerts)
                    sb.AppendLine($"  {alert.Title}: {alert.Message}");
            }

            return sb.ToString();
        }

        string IAiContextProvider.BuildLocalContext() => ((IAiContextProvider)this).BuildContext();

    }

    public sealed class KpiCardVm
    {
        public string Title { get; }
        public string ValueText { get; }
        public string SubText { get; }
        public string StatusMessage { get; }
        public Visibility StatusVisibility { get; }
        public string ScopeBadgeText { get; }
        public Visibility ScopeBadgeVisibility { get; }
        public IReadOnlyList<KpiProjectDrilldownRow>? ProjectDrilldownRows { get; }
        public IReadOnlyList<KpiCashHistoryRow>? CashHistoryRows { get; }
        public IReadOnlyList<KpiCashAccountRow>? CashAccountRows { get; }
        public IReadOnlyList<KpiArOutstandingRow>? ArOutstandingRows { get; }
        public IReadOnlyList<KpiArInvoiceRow>? ArInvoiceRows { get; }
        public IReadOnlyList<KpiWipUnbilledRow>? WipUnbilledRows { get; }
        public IReadOnlyList<KpiBacklogRow>? BacklogRows { get; }
        public IReadOnlyList<KpiBillingsRow>? BillingsRows { get; }
        public IReadOnlyList<KpiBudgetBurnRow>? BudgetBurnRows { get; }
        public IReadOnlyList<KpiDeliveryRiskRow>? DeliveryRiskRows { get; }
        public IReadOnlyList<KpiUtilizationRow>? UtilizationRows { get; }

        public KpiCardVm(ExecutiveKpi k)
        {
            Title = k.Title ?? "";
            ValueText = k.ValueText ?? "";
            SubText = k.SubText ?? "";
            StatusMessage = k.StatusMessage ?? "";
            StatusVisibility = string.IsNullOrWhiteSpace(StatusMessage) ? Visibility.Collapsed : Visibility.Visible;
            ScopeBadgeText = ScopeBadgeTextFor(k.Scope);
            ScopeBadgeVisibility = string.IsNullOrWhiteSpace(ScopeBadgeText) ? Visibility.Collapsed : Visibility.Visible;
            ProjectDrilldownRows = k.ProjectDrilldownRows;
            CashHistoryRows = k.CashHistoryRows;
            CashAccountRows = k.CashAccountRows;
            ArOutstandingRows = k.ArOutstandingRows;
            ArInvoiceRows = k.ArInvoiceRows;
            WipUnbilledRows = k.WipUnbilledRows;
            BacklogRows = k.BacklogRows;
            BillingsRows = k.BillingsRows;
            BudgetBurnRows = k.BudgetBurnRows;
            DeliveryRiskRows = k.DeliveryRiskRows;
            UtilizationRows = k.UtilizationRows;
        }

        private static string ScopeBadgeTextFor(ScopeKind scope)
            => scope switch
            {
                ScopeKind.Firmwide => "Firmwide",
                ScopeKind.Scoped => "Scoped",
                _ => string.Empty
            };
    }

    public sealed class TrendCardVm
    {
        public string Title { get; }
        public string ValueText { get; }
        public string StatusMessage { get; }
        public Visibility StatusVisibility { get; }
        public string BadgeText { get; }
        public Visibility BadgeVisibility { get; }
        public string ScopeBadgeText { get; }
        public Visibility ScopeBadgeVisibility { get; }

        public double[]? Values { get; }
        public IReadOnlyList<TrendPayerRow>? TrendPayerRows { get; }
        public IReadOnlyList<KpiDeliveryRiskRow>? DeliveryRiskRows { get; }

        public PointCollection Points { get; } = new();
        public Visibility SparklineVisibility { get; }

        public TrendCardVm(ExecutiveTrend t)
        {
            Title = t.Title ?? "";
            ValueText = t.ValueText ?? "";
            StatusMessage = t.StatusMessage ?? "";
            StatusVisibility = string.IsNullOrWhiteSpace(StatusMessage) ? Visibility.Collapsed : Visibility.Visible;
            BadgeText = t.IsAligned ? "Aligned" : string.Empty;
            BadgeVisibility = t.IsAligned ? Visibility.Visible : Visibility.Collapsed;
            ScopeBadgeText = ScopeBadgeTextFor(t.Scope);
            ScopeBadgeVisibility = string.IsNullOrWhiteSpace(ScopeBadgeText) ? Visibility.Collapsed : Visibility.Visible;

            Values = t.Values;
            TrendPayerRows = t.TrendPayerRows;
            DeliveryRiskRows = t.DeliveryRiskRows;

            if (Values == null || Values.Length < 2)
            {
                SparklineVisibility = Visibility.Collapsed;
                return;
            }

            SparklineVisibility = Visibility.Visible;
            foreach (var p in SparklineBuilder.Build(Values, width: 108, height: 22))
                Points.Add(p);
        }

        private static string ScopeBadgeTextFor(ScopeKind scope)
            => scope switch
            {
                ScopeKind.Firmwide => "Firmwide",
                ScopeKind.Scoped => "Scoped",
                _ => string.Empty
            };
    }

    internal static class SparklineBuilder
    {
        public static Point[] Build(double[] values, double width, double height)
        {
            if (values == null || values.Length < 2)
                return Array.Empty<Point>();

            double min = double.MaxValue;
            double max = double.MinValue;
            for (int i = 0; i < values.Length; i++)
            {
                var v = values[i];
                if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                if (v < min) min = v;
                if (v > max) max = v;
            }

            if (min == double.MaxValue || max == double.MinValue)
                return Array.Empty<Point>();

            // flat line handling
            var span = Math.Abs(max - min);
            if (span < 1e-9) span = 1.0;

            var pts = new Point[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                var x = (values.Length == 1) ? 0 : (i * (width / (values.Length - 1)));
                var norm = (values[i] - min) / span;
                var y = height - (norm * height);
                pts[i] = new Point(x, y);
            }
            return pts;
        }
    }

    public sealed class AlertVm
    {
        public string Title { get; }
        public string Message { get; }
        public IReadOnlyList<KpiProjectDrilldownRow>? ProjectDrilldownRows { get; }
        public IReadOnlyList<KpiArOutstandingRow>? ArOutstandingRows { get; }
        public IReadOnlyList<KpiArInvoiceRow>? ArInvoiceRows { get; }
        public IReadOnlyList<KpiBacklogRow>? BacklogRows { get; }
        public IReadOnlyList<KpiBudgetBurnRow>? BudgetBurnRows { get; }

        public AlertVm(
            string title,
            string message,
            IReadOnlyList<KpiProjectDrilldownRow>? projectDrilldownRows = null,
            IReadOnlyList<KpiArOutstandingRow>? arOutstandingRows = null,
            IReadOnlyList<KpiArInvoiceRow>? arInvoiceRows = null,
            IReadOnlyList<KpiBacklogRow>? backlogRows = null,
            IReadOnlyList<KpiBudgetBurnRow>? budgetBurnRows = null)
        {
            Title = title ?? "";
            Message = message ?? "";
            ProjectDrilldownRows = projectDrilldownRows;
            ArOutstandingRows = arOutstandingRows;
            ArInvoiceRows = arInvoiceRows;
            BacklogRows = backlogRows;
            BudgetBurnRows = budgetBurnRows;
        }
    }
}
