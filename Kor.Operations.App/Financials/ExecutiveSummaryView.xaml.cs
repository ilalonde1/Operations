#nullable enable
using System.Windows;
using System.Windows.Controls;

namespace Kor.Operations.Financials
{
    public partial class ExecutiveSummaryView : UserControl
    {
        public ExecutiveSummaryView()
        {
            InitializeComponent();
        }

        private void KpiCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not KpiCardVm kpi) return;

            var lastUpdated = (DataContext as ExecutiveSummaryViewModel)?.LastUpdatedDisplay ?? string.Empty;
            var owner = Window.GetWindow(this);
            var dlg = new MetricDetailWindow(new MetricDetailVm(
                kind: MetricKind.Kpi,
                title: kpi.Title,
                valueText: kpi.ValueText,
                subText: kpi.SubText,
                statusMessage: kpi.StatusMessage,
                lastUpdatedDisplay: lastUpdated,
                trendValues: null,
                projectDrilldownRows: kpi.ProjectDrilldownRows,
                cashHistoryRows: kpi.CashHistoryRows,
                cashAccountRows: kpi.CashAccountRows,
                arOutstandingRows: kpi.ArOutstandingRows,
                arInvoiceRows: kpi.ArInvoiceRows,
                wipUnbilledRows: kpi.WipUnbilledRows,
                backlogRows: kpi.BacklogRows,
                billingsRows: kpi.BillingsRows,
                budgetBurnRows: kpi.BudgetBurnRows,
                deliveryRiskRows: kpi.DeliveryRiskRows,
                utilizationRows: kpi.UtilizationRows), Kor.Operations.Services.AppServices.Get<FinancialsService>())
            {
                Owner = owner
            };
            dlg.ShowDialog();
        }

        private void TrendCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not TrendCardVm t) return;

            var lastUpdated = (DataContext as ExecutiveSummaryViewModel)?.LastUpdatedDisplay ?? string.Empty;
            var owner = Window.GetWindow(this);
            var dlg = new MetricDetailWindow(new MetricDetailVm(
                kind: MetricKind.Trend,
                title: t.Title,
                valueText: t.ValueText,
                subText: string.Empty,
                statusMessage: t.StatusMessage,
                lastUpdatedDisplay: lastUpdated,
                trendValues: t.Values,
                trendPayerRows: t.TrendPayerRows,
                deliveryRiskRows: t.DeliveryRiskRows), Kor.Operations.Services.AppServices.Get<FinancialsService>())
            {
                Owner = owner
            };
            dlg.ShowDialog();
        }

        private void AlertCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not AlertVm a) return;

            var lastUpdated = (DataContext as ExecutiveSummaryViewModel)?.LastUpdatedDisplay ?? string.Empty;
            var owner = Window.GetWindow(this);
            var dlg = new MetricDetailWindow(new MetricDetailVm(
                kind: MetricKind.Alert,
                title: a.Title,
                valueText: "Triggered",
                subText: a.Message,
                statusMessage: string.Empty,
                lastUpdatedDisplay: lastUpdated,
                trendValues: null,
                projectDrilldownRows: a.ProjectDrilldownRows,
                arOutstandingRows: a.ArOutstandingRows,
                arInvoiceRows: a.ArInvoiceRows,
                backlogRows: a.BacklogRows,
                budgetBurnRows: a.BudgetBurnRows), Kor.Operations.Services.AppServices.Get<FinancialsService>())
            {
                Owner = owner
            };
            dlg.ShowDialog();
        }
    }
}
