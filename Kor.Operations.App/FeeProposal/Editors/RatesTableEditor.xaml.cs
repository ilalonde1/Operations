#nullable enable
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.Core.Models.Proposal;

namespace Kor.Operations.App.FeeProposal.Editors
{
    public partial class RatesTableEditor : UserControl
    {
        public RatesTableEditor()
        {
            InitializeComponent();
            Loaded += (_, _) => RefreshRates();
        }

        private RatesTableBlockContent? Model => DataContext as RatesTableBlockContent;

        private void RefreshRates()
        {
            RatesList.ItemsSource = null;
            RatesList.ItemsSource = Model?.Rates;
            RatesList.Items.Refresh();
        }

        private void AddRate_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null) return;
            Model.Rates.Add(new HourlyRateRow { Role = "New Role" });
            RefreshRates();
            RatesList.SelectedIndex = Model.Rates.Count - 1;
        }

        private void RemoveRate_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null || RatesList.SelectedItem is not HourlyRateRow selected)
                return;

            Model.Rates.Remove(selected);
            RefreshRates();
        }

        private void RatesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RatesList.SelectedItem is HourlyRateRow selected)
            {
                RoleText.Text = selected.Role;
                RateText.Text = selected.RatePerHour.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                RoleText.Text = string.Empty;
                RateText.Text = string.Empty;
            }
        }

        private void RoleText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Model is null)
                return;

            var selectedIndex = RatesList.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= Model.Rates.Count)
                return;

            Model.Rates[selectedIndex].Role = RoleText.Text;
            RatesList.Items.Refresh();
        }

        private void RateText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Model is null)
                return;

            var selectedIndex = RatesList.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= Model.Rates.Count)
                return;

            if (decimal.TryParse(RateText.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                Model.Rates[selectedIndex].RatePerHour = value;

            RatesList.Items.Refresh();
        }
    }
}
