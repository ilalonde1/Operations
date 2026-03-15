#nullable enable
using System;
using System.Threading.Tasks;
using System.Windows;
using Kor.Operations.App.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations.Financials
{
    public partial class GlProfitLossWindow : Window
    {
        private readonly GlProfitLossPresenter _presenter;

        public GlProfitLossWindow()
            : this(
                ((global::Kor.Operations.OperationsApp)Application.Current).Services.GetRequiredService<GlProfitLossService>(),
                ((global::Kor.Operations.OperationsApp)Application.Current).Services.GetRequiredService<FinancialsOptions>())
        {
        }

        public GlProfitLossWindow(GlProfitLossService glProfitLossService, FinancialsOptions financialsOptions)
        {
            InitializeComponent();
            _presenter = new GlProfitLossPresenter(
                this,
                glProfitLossService ?? throw new ArgumentNullException(nameof(glProfitLossService)),
                financialsOptions ?? throw new ArgumentNullException(nameof(financialsOptions)),
                PnLGrid,
                NetTrendCanvas,
                NetTrendLine,
                RevExpCanvas,
                NetTrendLabelGrid,
                RevExpLabelGrid);
            DataContext = _presenter.ViewModel;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try { await global::Kor.Operations.HeaderLoader.ApplyAsync(HeaderBar); } catch { /* non-fatal */ }
            await _presenter.InitializeAsync().ConfigureAwait(true);
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            await _presenter.RefreshAsync(forceRefresh: true).ConfigureAwait(true);
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

        private void NetTrendCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => _presenter.RenderCharts();

        private void RevExpCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => _presenter.RenderCharts();

        private async void ExportBtn_Click(object sender, RoutedEventArgs e)
        {
            await _presenter.ExportAsync(this).ConfigureAwait(true);
        }
    }
}
