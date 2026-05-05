#nullable enable
using System;
using System.Threading.Tasks;
using System.Windows;
using Kor.Operations.App.Options;
using Kor.Operations.Services;

namespace Kor.Operations.Financials
{
    public partial class GlProfitLossWindow : Window
    {
        private readonly BilledFinancialsPresenter _billedPresenter;
        private readonly GlProfitLossPresenter _postedPresenter;
        private bool _initialized;
        private PnlViewMode _currentMode = PnlViewMode.Billed;

        public GlProfitLossWindow(
            GlProfitLossService glProfitLossService,
            BilledFinancialsService billedFinancialsService,
            FinancialsOptions financialsOptions)
        {
            InitializeComponent();
            var options = financialsOptions ?? throw new ArgumentNullException(nameof(financialsOptions));

            _billedPresenter = new BilledFinancialsPresenter(
                billedFinancialsService ?? throw new ArgumentNullException(nameof(billedFinancialsService)),
                options,
                PnLGrid,
                NetTrendCanvas,
                NetTrendLine,
                RevExpCanvas,
                NetTrendLabelGrid,
                RevExpLabelGrid);

            _postedPresenter = new GlProfitLossPresenter(
                this,
                glProfitLossService ?? throw new ArgumentNullException(nameof(glProfitLossService)),
                options,
                PostedPnLGrid,
                NetTrendCanvas,
                NetTrendLine,
                RevExpCanvas,
                NetTrendLabelGrid,
                RevExpLabelGrid);

            DataContext = _billedPresenter.ViewModel;

            AppServices.GetOptional<AppAiContextBuilder>()?.Register(_billedPresenter.ViewModel);
            AppServices.GetOptional<AppAiContextBuilder>()?.Register(_postedPresenter.ViewModel);
            Closed += (_, _) =>
            {
                var builder = AppServices.GetOptional<AppAiContextBuilder>();
                builder?.Unregister(_billedPresenter.ViewModel);
                builder?.Unregister(_postedPresenter.ViewModel);
            };
        }

        private bool IsPostedMode => PostedViewRadio.IsChecked == true;

        private bool IsSideBySideMode => SideBySideViewRadio.IsChecked == true;

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try { await global::Kor.Operations.HeaderLoader.ApplyAsync(HeaderBar); } catch (Exception ex) { Serilog.Log.Warning(ex, "Header load failed."); }
            await _billedPresenter.InitializeAsync().ConfigureAwait(true);
            await _postedPresenter.InitializeAsync().ConfigureAwait(true);
            _initialized = true;
            BilledViewRadio.IsChecked = true;
            _currentMode = PnlViewMode.Billed;
            ApplyViewMode();
        }

        private async void ViewMode_Checked(object sender, RoutedEventArgs e)
        {
            if (!_initialized)
                return;

            var nextMode = GetSelectedMode();
            CopyFilters(_currentMode, nextMode);
            _currentMode = nextMode;
            ApplyViewMode();
            await RefreshActiveAsync(forceRefresh: false).ConfigureAwait(true);
        }

        private void ApplyViewMode()
        {
            if (IsPostedMode)
            {
                DataContext = _postedPresenter.ViewModel;
                PnLGrid.Visibility = Visibility.Collapsed;
                PostedPnLGrid.Visibility = Visibility.Visible;
                PostedGridLabel.Visibility = Visibility.Collapsed;
                TableFilterPanel.Visibility = Visibility.Visible;
                FlipSignCheckBox.Visibility = Visibility.Visible;
                ReconciliationPanel.Visibility = Visibility.Collapsed;
                PostedLagPanel.Visibility = _postedPresenter.ViewModel.PostingLagVisibility;
                HeaderBar.HeaderText = "Profit & Loss (Posted GL)";
                HeaderBar.SubtitleText = "Audit-trail P&L from GLSummary after accounting posts transactions.";
                _postedPresenter.RenderCharts();
                return;
            }

            DataContext = _billedPresenter.ViewModel;
            PnLGrid.Visibility = Visibility.Visible;
            PostedPnLGrid.Visibility = IsSideBySideMode ? Visibility.Visible : Visibility.Collapsed;
            PostedGridLabel.Visibility = IsSideBySideMode ? Visibility.Visible : Visibility.Collapsed;
            TableFilterPanel.Visibility = Visibility.Collapsed;
            FlipSignCheckBox.Visibility = Visibility.Collapsed;
            ReconciliationPanel.Visibility = _billedPresenter.ViewModel.ReconciliationVisibility;
            PostedLagPanel.Visibility = Visibility.Collapsed;
            HeaderBar.HeaderText = IsSideBySideMode ? "Profit & Loss (Billed vs Posted)" : "Profit & Loss (Billed)";
            HeaderBar.SubtitleText = "Billed source-of-truth view from LedgerAR with posted-GL reconciliation.";
            _billedPresenter.RenderCharts();
        }

        private void SyncFiltersFromActive()
        {
            if (IsPostedMode)
            {
                _billedPresenter.ViewModel.FromDate = _postedPresenter.ViewModel.FromDate;
                _billedPresenter.ViewModel.ToDate = _postedPresenter.ViewModel.ToDate;
                _billedPresenter.ViewModel.OrgFilter = _postedPresenter.ViewModel.OrgFilter;
                _billedPresenter.ViewModel.HideZeroRows = _postedPresenter.ViewModel.HideZeroRows;
                return;
            }

            _postedPresenter.ViewModel.FromDate = _billedPresenter.ViewModel.FromDate;
            _postedPresenter.ViewModel.ToDate = _billedPresenter.ViewModel.ToDate;
            _postedPresenter.ViewModel.OrgFilter = _billedPresenter.ViewModel.OrgFilter;
            _postedPresenter.ViewModel.HideZeroRows = _billedPresenter.ViewModel.HideZeroRows;
        }

        private void CopyFilters(PnlViewMode fromMode, PnlViewMode toMode)
        {
            if (fromMode == toMode)
                return;

            if (fromMode == PnlViewMode.Posted && toMode != PnlViewMode.Posted)
            {
                _billedPresenter.ViewModel.FromDate = _postedPresenter.ViewModel.FromDate;
                _billedPresenter.ViewModel.ToDate = _postedPresenter.ViewModel.ToDate;
                _billedPresenter.ViewModel.OrgFilter = _postedPresenter.ViewModel.OrgFilter;
                _billedPresenter.ViewModel.HideZeroRows = _postedPresenter.ViewModel.HideZeroRows;
                return;
            }

            if (fromMode != PnlViewMode.Posted && toMode == PnlViewMode.Posted)
            {
                _postedPresenter.ViewModel.FromDate = _billedPresenter.ViewModel.FromDate;
                _postedPresenter.ViewModel.ToDate = _billedPresenter.ViewModel.ToDate;
                _postedPresenter.ViewModel.OrgFilter = _billedPresenter.ViewModel.OrgFilter;
                _postedPresenter.ViewModel.HideZeroRows = _billedPresenter.ViewModel.HideZeroRows;
            }
        }

        private PnlViewMode GetSelectedMode()
        {
            if (PostedViewRadio.IsChecked == true)
                return PnlViewMode.Posted;
            if (SideBySideViewRadio.IsChecked == true)
                return PnlViewMode.SideBySide;
            return PnlViewMode.Billed;
        }

        private async Task RefreshActiveAsync(bool forceRefresh)
        {
            SyncFiltersFromActive();
            if (IsPostedMode)
            {
                await _postedPresenter.RefreshAsync(forceRefresh).ConfigureAwait(true);
            }
            else if (IsSideBySideMode)
            {
                await _billedPresenter.RefreshAsync(forceRefresh).ConfigureAwait(true);
                await _postedPresenter.RefreshAsync(forceRefresh).ConfigureAwait(true);
            }
            else
            {
                await _billedPresenter.RefreshAsync(forceRefresh).ConfigureAwait(true);
            }

            ApplyViewMode();
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            await RefreshActiveAsync(forceRefresh: true).ConfigureAwait(true);
        }

        private void MetricDictionaryBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new FinancialMetricDictionaryWindow { Owner = this };
                win.ShowDialog();
            }
            catch
            {
                // Non-critical: ignore if window cannot be created for any reason.
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

        private void NetTrendCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (IsPostedMode)
                _postedPresenter.RenderCharts();
            else
                _billedPresenter.RenderCharts();
        }

        private void RevExpCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (IsPostedMode)
                _postedPresenter.RenderCharts();
            else
                _billedPresenter.RenderCharts();
        }

        private async void ExportBtn_Click(object sender, RoutedEventArgs e)
        {
            if (IsPostedMode || IsSideBySideMode)
            {
                await _postedPresenter.ExportAsync(this).ConfigureAwait(true);
                return;
            }

            MessageBox.Show(
                this,
                "Billed P&L export is not wired yet. Use the grid view or switch to Posted (GL) for Excel export.",
                "Export to Excel",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private enum PnlViewMode
        {
            Billed,
            Posted,
            SideBySide
        }
    }
}
