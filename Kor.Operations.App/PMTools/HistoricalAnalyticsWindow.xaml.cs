#nullable enable
using System;
using System.ComponentModel;
using System.Windows;
using Kor.Operations.App.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations.PMTools
{
    public partial class HistoricalAnalyticsWindow : Window
    {
        private readonly HistoricalAnalyticsViewModel _vm = new();
        private readonly HistoricalAnalyticsService _svc;

        public HistoricalAnalyticsWindow()
            : this(((global::Kor.Operations.OperationsApp)Application.Current).Services.GetRequiredService<DeltekOdbcOptions>())
        {
        }

        public HistoricalAnalyticsWindow(DeltekOdbcOptions odbcOptions)
        {
            _svc = new HistoricalAnalyticsService(odbcOptions ?? throw new ArgumentNullException(nameof(odbcOptions)));
            InitializeComponent();
            DataContext = _vm;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(HistoricalAnalyticsViewModel.SelectedRow))
                UpdateDetailVisibility();
        }

        private void UpdateDetailVisibility()
        {
            var hasProject = _vm.SelectedRow != null;
            var hasSummary = _vm.DetailMetrics.Count > 0;
            EmptyStateText.Visibility = (hasProject || hasSummary) ? Visibility.Collapsed : Visibility.Visible;
            DetailContent.Visibility = hasProject ? Visibility.Visible : Visibility.Collapsed;
            SummaryDetailContent.Visibility = (!hasProject && hasSummary) ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _vm.IsLoading = true;
            _vm.ErrorMessage = "";
            StatusText.Text = "Loading historical data from Deltek...";
            try
            {
                var (rows, utilization, employeeHours) = await _svc.LoadAsync();
                _vm.SetUtilization(utilization);
                _vm.SetEmployeeHours(employeeHours);
                _vm.SetRows(rows);
                HistGrid.ItemsSource = _vm.Rows;
                StatusText.Text = $"Loaded {rows.Count} projects — {_vm.VisibleCount} visible.";
            }
            catch (Exception ex)
            {
                _vm.ErrorMessage = ex.Message;
                StatusText.Text = $"Error: {ex.Message}";
            }
            finally
            {
                _vm.IsLoading = false;
            }
        }

        private void PmGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            _vm.SetPmSummaryDetail(PmGrid.SelectedItem as PmPerformanceSummaryRow, "Project Manager");
            UpdateDetailVisibility();
        }

        private void DmGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            _vm.SetPmSummaryDetail(DmGrid.SelectedItem as PmPerformanceSummaryRow, "Drafting Manager");
            UpdateDetailVisibility();
        }

        private void EmployeeGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            _vm.SetEmployeeSummaryDetail(EmployeeGrid.SelectedItem as EmployeeSummaryRow);
            UpdateDetailVisibility();
        }

        private void HelpBtn_Click(object sender, RoutedEventArgs e)
            => new HistoricalAnalyticsHelpWindow { Owner = this }.Show();
        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
    }
}
