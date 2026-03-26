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
        }

        private ExcludedServicesBlockContent? Model => DataContext as ExcludedServicesBlockContent;

        private void AddExcluded_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null) return;
            Model.ExcludedItems.Add("New excluded service");
            ExcludedList.SelectedIndex = Model.ExcludedItems.Count - 1;
        }

        private void RemoveExcluded_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null || ExcludedList.SelectedItem is not string selected)
                return;

            Model.ExcludedItems.Remove(selected);
            SelectedExcludedText.Text = string.Empty;
        }

        private void ExcludedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectedExcludedText.Text = ExcludedList.SelectedItem as string ?? string.Empty;
        }

        private void SelectedExcludedText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Model is null)
                return;

            var selectedIndex = ExcludedList.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= Model.ExcludedItems.Count)
                return;

            Model.ExcludedItems[selectedIndex] = SelectedExcludedText.Text;
        }
    }
}
