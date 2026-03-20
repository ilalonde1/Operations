#nullable enable
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.Core.Models.Proposal;

namespace Kor.Operations.App.FeeProposal.Editors
{
    public partial class ExcludedServicesEditor : UserControl
    {
        public ExcludedServicesEditor()
        {
            InitializeComponent();
            Loaded += (_, _) => RefreshExcluded();
        }

        private ExcludedServicesBlockContent? Model => DataContext as ExcludedServicesBlockContent;

        private void RefreshExcluded()
        {
            ExcludedList.ItemsSource = null;
            ExcludedList.ItemsSource = Model?.ExcludedItems;
        }

        private void AddExcluded_Click(object sender, RoutedEventArgs e)
        {
            Model?.ExcludedItems.Add("New excluded service");
            RefreshExcluded();
            ExcludedList.SelectedIndex = Model!.ExcludedItems.Count - 1;
        }

        private void RemoveExcluded_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null || ExcludedList.SelectedItem is not string selected)
                return;

            Model.ExcludedItems.Remove(selected);
            RefreshExcluded();
            SelectedExcludedText.Text = string.Empty;
        }

        private void ExcludedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectedExcludedText.Text = ExcludedList.SelectedItem as string ?? string.Empty;
        }

        private void SelectedExcludedText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Model is null || ExcludedList.SelectedIndex < 0)
                return;

            Model.ExcludedItems[ExcludedList.SelectedIndex] = SelectedExcludedText.Text;
            RefreshExcluded();
            ExcludedList.SelectedIndex = System.Math.Min(ExcludedList.SelectedIndex, Model.ExcludedItems.Count - 1);
        }
    }
}
