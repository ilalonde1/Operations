#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Kor.Operations.Core;

namespace Kor.Operations.Financials
{
    internal sealed class ExecutiveSummaryViewModel : ObservableObject
    {
        private readonly ExecutiveSummaryService _svc = new();
        private bool _isLoading;
        private DateTimeOffset? _lastUpdated;
        private string _statusHint = "";

        public ObservableCollection<KpiCardVm> Kpis { get; } = new();
        public ObservableCollection<TrendCardVm> Trends { get; } = new();
        public ObservableCollection<AlertVm> Alerts { get; } = new();

        public string LastUpdatedDisplay =>
            _lastUpdated.HasValue
                ? _lastUpdated.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")
                : "Not yet";

        public string StatusHint => _statusHint ?? "";

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
            Kpis.Clear();
            foreach (var k in result.Kpis)
                Kpis.Add(new KpiCardVm(k));

            Trends.Clear();
            foreach (var t in result.Trends)
                Trends.Add(new TrendCardVm(t));

            Alerts.Clear();
            foreach (var a in result.Alerts)
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

    internal sealed class KpiCardVm
    {
        public string Title { get; }
        public string ValueText { get; }
        public string SubText { get; }
        public string StatusMessage { get; }
        public Visibility StatusVisibility { get; }
        public IReadOnlyList<KpiProjectDrilldownRow>? ProjectDrilldownRows { get; }
        public IReadOnlyList<KpiCashHistoryRow>? CashHistoryRows { get; }
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
            ProjectDrilldownRows = k.ProjectDrilldownRows;
            CashHistoryRows = k.CashHistoryRows;
            ArOutstandingRows = k.ArOutstandingRows;
            ArInvoiceRows = k.ArInvoiceRows;
            WipUnbilledRows = k.WipUnbilledRows;
            BacklogRows = k.BacklogRows;
            BillingsRows = k.BillingsRows;
            BudgetBurnRows = k.BudgetBurnRows;
            DeliveryRiskRows = k.DeliveryRiskRows;
            UtilizationRows = k.UtilizationRows;
        }
    }

    internal sealed class TrendCardVm
    {
        public string Title { get; }
        public string ValueText { get; }
        public string StatusMessage { get; }
        public Visibility StatusVisibility { get; }
        public string BadgeText { get; }
        public Visibility BadgeVisibility { get; }

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
            var isRevenueAligned =
                string.Equals(Title, "Revenue (Earned) (30/90 day)", StringComparison.OrdinalIgnoreCase) &&
                StatusMessage.IndexOf("aligned", StringComparison.OrdinalIgnoreCase) >= 0;
            BadgeText = isRevenueAligned ? "Aligned" : string.Empty;
            BadgeVisibility = isRevenueAligned ? Visibility.Visible : Visibility.Collapsed;

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

    internal sealed class AlertVm
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
