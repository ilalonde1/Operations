#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Kor.Operations.Financials;

public partial class MetricDetailWindow : Window
{
    private readonly FinancialsService _financialsService;

    public MetricDetailWindow(MetricDetailVm vm, FinancialsService financialsService)
    {
        _financialsService = financialsService ?? throw new ArgumentNullException(nameof(financialsService));
        InitializeComponent();
        DataContext = vm ?? throw new ArgumentNullException(nameof(vm));
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void CopySummary_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MetricDetailVm vm) return;
        Clipboard.SetText(vm.ToClipboardText());
    }

    private void PreviousProjectPage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MetricDetailVm vm) return;
        vm.GoToPreviousProjectPage();
    }

    private void NextProjectPage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MetricDetailVm vm) return;
        vm.GoToNextProjectPage();
    }

    private void PreviousArPage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MetricDetailVm vm) return;
        vm.GoToPreviousArPage();
    }

    private void NextArPage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MetricDetailVm vm) return;
        vm.GoToNextArPage();
    }

    private void PreviousArInvoicePage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MetricDetailVm vm) return;
        vm.GoToPreviousArInvoicePage();
    }

    private void NextArInvoicePage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MetricDetailVm vm) return;
        vm.GoToNextArInvoicePage();
    }

    private void PreviousWipPage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MetricDetailVm vm) return;
        vm.GoToPreviousWipPage();
    }

    private void NextWipPage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MetricDetailVm vm) return;
        vm.GoToNextWipPage();
    }

    private void PreviousBacklogPage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MetricDetailVm vm) return;
        vm.GoToPreviousBacklogPage();
    }

    private void NextBacklogPage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MetricDetailVm vm) return;
        vm.GoToNextBacklogPage();
    }

    private void PreviousBillingsPage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MetricDetailVm vm) return;
        vm.GoToPreviousBillingsPage();
    }

    private void NextBillingsPage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MetricDetailVm vm) return;
        vm.GoToNextBillingsPage();
    }

    private void PreviousBudgetBurnPage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MetricDetailVm vm) return;
        vm.GoToPreviousBudgetBurnPage();
    }

    private void NextBudgetBurnPage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MetricDetailVm vm) return;
        vm.GoToNextBudgetBurnPage();
    }

    private void PreviousUtilizationPage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MetricDetailVm vm) return;
        vm.GoToPreviousUtilizationPage();
    }

    private void NextUtilizationPage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MetricDetailVm vm) return;
        vm.GoToNextUtilizationPage();
    }

    private void PreviousDeliveryRiskPage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MetricDetailVm vm) return;
        vm.GoToPreviousDeliveryRiskPage();
    }

    private void NextDeliveryRiskPage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MetricDetailVm vm) return;
        vm.GoToNextDeliveryRiskPage();
    }

    private void PreviousTrendPayerPage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MetricDetailVm vm) return;
        vm.GoToPreviousTrendPayerPage();
    }

    private void NextTrendPayerPage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MetricDetailVm vm) return;
        vm.GoToNextTrendPayerPage();
    }

    private async void ProjectRowGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid)
            return;

        if (!IsDataGridRowDoubleClick(e))
            return;

        var wbs1 = GetSelectedWbs1(grid.SelectedItem);
        if (string.IsNullOrWhiteSpace(wbs1))
            return;

        try
        {
            var snapshot = await _financialsService.GetSnapshotAsync(forceRefresh: false, CancellationToken.None);
            var project = snapshot.Rows.FirstOrDefault(r => string.Equals((r.Wbs1 ?? string.Empty).Trim(), wbs1, StringComparison.OrdinalIgnoreCase));
            if (project == null)
                return;

            var counts = BuildPortfolioHealthCounts(snapshot.Rows);
            var win = new ProjectFinancialDetailWindow(project, counts) { Owner = this };
            win.Show();
        }
        catch
        {
            // Ignore navigation failures; the metric modal remains usable.
        }
    }

    private static bool IsDataGridRowDoubleClick(MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject d)
            return false;

        DependencyObject? cur = d;
        while (cur != null && cur is not DataGridRow)
            cur = VisualTreeHelper.GetParent(cur);
        return cur is DataGridRow;
    }

    private static string GetSelectedWbs1(object? selectedItem)
    {
        if (selectedItem == null)
            return string.Empty;

        var prop = selectedItem.GetType().GetProperty("Wbs1");
        if (prop?.PropertyType != typeof(string))
            return string.Empty;

        return ((string?)prop.GetValue(selectedItem) ?? string.Empty).Trim();
    }

    private static CfoMetrics.PortfolioHealthCounts BuildPortfolioHealthCounts(IEnumerable<FinancialsProjectRow> rows)
    {
        var critical = 0;
        var watch = 0;
        var healthy = 0;

        foreach (var row in rows)
        {
            var util = UtilizationRow.FromProject(row);
            switch (util.ConfidenceLevel)
            {
                case DeliveryConfidenceLevel.Critical:
                    critical++;
                    break;
                case DeliveryConfidenceLevel.AtRisk:
                case DeliveryConfidenceLevel.Watch:
                    watch++;
                    break;
                default:
                    healthy++;
                    break;
            }
        }

        return new CfoMetrics.PortfolioHealthCounts(
            Healthy: healthy,
            Watch: watch,
            Critical: critical);
    }
}
