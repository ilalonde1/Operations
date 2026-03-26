#nullable enable
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace Kor.Operations.Financials
{
    public partial class FinancialMetricDictionaryWindow : Window
    {
        private readonly List<FinancialMetricDefinition> _allDefinitions;
        private readonly ICollectionView _view;
        private string _categoryFilter = "All";

        public FinancialMetricDictionaryWindow()
        {
            InitializeComponent();

            _allDefinitions = FinancialMetricDefinitions.Definitions.Values
                .OrderBy(d => d.DisplayName, System.StringComparer.OrdinalIgnoreCase)
                .ToList();

            MetricsList.ItemsSource = _allDefinitions;
            _view = CollectionViewSource.GetDefaultView(MetricsList.ItemsSource);
            _view.Filter = FilterMetric;
            SetCategoryFilter("All");
        }

        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _view.Refresh();
            UpdateCount();
        }

        private void CategoryAll_Click(object sender, RoutedEventArgs e) => SetCategoryFilter("All");

        private void CategoryFinancial_Click(object sender, RoutedEventArgs e) => SetCategoryFilter("Financial");

        private void CategoryPm_Click(object sender, RoutedEventArgs e) => SetCategoryFilter("PM");

        private void SetCategoryFilter(string cat)
        {
            _categoryFilter = cat;
            CatAllBtn.FontWeight = cat == "All" ? FontWeights.SemiBold : FontWeights.Normal;
            CatFinancialBtn.FontWeight = cat == "Financial" ? FontWeights.SemiBold : FontWeights.Normal;
            CatPmBtn.FontWeight = cat == "PM" ? FontWeights.SemiBold : FontWeights.Normal;
            CatAllBtn.Background = cat == "All" ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Transparent;
            CatFinancialBtn.Background = cat == "Financial" ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Transparent;
            CatPmBtn.Background = cat == "PM" ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Transparent;
            _view.Refresh();
            UpdateCount();
        }

        private bool FilterMetric(object item)
        {
            if (item is not FinancialMetricDefinition def)
                return false;

            if (_categoryFilter != "All" && def.Category != _categoryFilter)
                return false;

            var q = (SearchBox.Text ?? string.Empty).Trim();
            if (q.Length == 0)
                return true;

            return Contains(def.DisplayName, q)
                || Contains(def.Key, q)
                || Contains(def.Description, q)
                || Contains(def.Formula, q);
        }

        private static bool Contains(string? source, string needle)
            => !string.IsNullOrWhiteSpace(source)
               && source.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;

        private void UpdateCount()
        {
            var visible = _view.Cast<object>().Count();
            ResultsCountText.Text = $"{visible:N0} metrics";
        }
    }
}
