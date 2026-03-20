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
            Model?.Rates.Add(new HourlyRateRow { Role = "New Role" });
            RefreshRates();
            RatesList.SelectedIndex = Model!.Rates.Count - 1;
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
            if (RatesList.SelectedItem is not HourlyRateRow selected)
                return;

            selected.Role = RoleText.Text;
            RatesList.Items.Refresh();
        }

        private void RateText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (RatesList.SelectedItem is not HourlyRateRow selected)
                return;

            if (decimal.TryParse(RateText.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                selected.RatePerHour = value;

            RatesList.Items.Refresh();
        }
    }
}
