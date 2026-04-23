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

        public HistoricalAnalyticsWindow(DeltekOdbcOptions odbcOptions)
        {
            _svc = new HistoricalAnalyticsService(odbcOptions ?? throw new ArgumentNullException(nameof(odbcOptions)));
            InitializeComponent();
            DataContext = _vm;
            _vm.SetOptions(odbcOptions);
            _vm.PropertyChanged += OnVmPropertyChanged;

            var contextBuilder = Kor.Operations.Services.AppServices.Get<Kor.Operations.Services.AppAiContextBuilder>();
            contextBuilder.Register(_vm);
            var aiService = Kor.Operations.Services.AppServices.Get<Kor.Operations.Services.AppAiService>();
            AiPanel.Initialize(aiService, _vm);
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(HistoricalAnalyticsViewModel.SelectedRow))
            {
                if (_vm.SelectedRow != null)
                {
                    // Project selected — clear summary detail, show project detail
                    _vm.DetailMetrics.ReplaceAll(System.Array.Empty<DetailMetric>());
                    _vm.DetailTitle = "";
                    _vm.DetailSubtitle = "";
                    // Defer visibility update so DataContext binding propagates first
                    Dispatcher.BeginInvoke(UpdateDetailVisibility, System.Windows.Threading.DispatcherPriority.DataBind);
                }
                else
                {
                    // SelectedRow cleared (by filter, view change, or summary handler).
                    // If no summary detail is pending, show empty state.
                    // If a summary handler is about to populate, it will call UpdateDetailVisibility after.
                    if (_vm.DetailMetrics.Count == 0)
                        UpdateDetailVisibility();
                }
            }
            else if (e.PropertyName == nameof(HistoricalAnalyticsViewModel.ViewMode))
            {
                // View mode changed — clear both selections
                _vm.SelectedRow = null;
                _vm.DetailMetrics.ReplaceAll(System.Array.Empty<DetailMetric>());
                _vm.DetailTitle = "";
                _vm.DetailSubtitle = "";
                UpdateDetailVisibility();
            }
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
                var (rows, utilization, employeeHours, weeklyHours, employeeRates) = await _svc.LoadAsync();
                _vm.SetUtilization(utilization);
                _vm.SetEmployeeHours(employeeHours);
                _vm.SetEmployeeWeeklyHours(weeklyHours);
                _vm.SetEmployeeRates(employeeRates);
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
            _vm.SelectedRow = null;
            UpdateDetailVisibility();
        }

        private void DmGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            _vm.SetPmSummaryDetail(DmGrid.SelectedItem as PmPerformanceSummaryRow, "Drafting Manager");
            _vm.SelectedRow = null;
            UpdateDetailVisibility();
        }

        private void EmployeeGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            _vm.SetEmployeeSummaryDetail(EmployeeGrid.SelectedItem as EmployeeSummaryRow);
            _vm.SelectedRow = null;
            UpdateDetailVisibility();
        }

        private void FeeBandGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            _vm.SetFeeBandDetail(FeeBandGrid.SelectedItem as FeeBandSummaryRow);
            _vm.SelectedRow = null;
            UpdateDetailVisibility();
        }

        private void ConstTypeGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            _vm.SetConstructionTypeDetail(ConstTypeGrid.SelectedItem as ConstructionTypeSummaryRow);
            _vm.SelectedRow = null;
            UpdateDetailVisibility();
        }

        private void YoYGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            _vm.SetYearTrendDetail(YoYGrid.SelectedItem as YearTrendRow);
            _vm.SelectedRow = null;
            UpdateDetailVisibility();
        }

        private void HelpBtn_Click(object sender, RoutedEventArgs e)
            => new HistoricalAnalyticsHelpWindow { Owner = this }.Show();
        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    }
}
