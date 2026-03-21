#nullable enable
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.Core.Models.Proposal;

namespace Kor.Operations.App.FeeProposal.Editors
{
    public partial class CompanyEditor : UserControl
    {
        public CompanyEditor()
        {
            InitializeComponent();
            Loaded += (_, _) => SectionsList.ItemsSource = Model?.Sections;
        }

        private CompanyBlockContent? Model => DataContext as CompanyBlockContent;

        private void AddSection_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null) return;
            Model.Sections.Add(new CompanySection { Title = "New section" });
            SectionsList.Items.Refresh();
            SectionsList.SelectedIndex = Model.Sections.Count - 1;
        }

        private void RemoveSection_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null || SectionsList.SelectedItem is not CompanySection selected)
                return;

            Model.Sections.Remove(selected);
            SectionsList.Items.Refresh();
            SectionTitleText.Text = string.Empty;
            SectionBodyText.Text = string.Empty;
        }

        private void SectionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SectionsList.SelectedItem is CompanySection selected)
            {
                SectionTitleText.Text = selected.Title;
                SectionBodyText.Text = selected.Body;
            }
            else
            {
                SectionTitleText.Text = string.Empty;
                SectionBodyText.Text = string.Empty;
            }
        }

        private void SectionTitleText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Model is null)
                return;

            var selectedIndex = SectionsList.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= Model.Sections.Count)
                return;

            Model.Sections[selectedIndex].Title = SectionTitleText.Text;
            SectionsList.Items.Refresh();
        }

        private void SectionBodyText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Model is null)
                return;

            var selectedIndex = SectionsList.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= Model.Sections.Count)
                return;

            Model.Sections[selectedIndex].Body = SectionBodyText.Text;
            SectionsList.Items.Refresh();
        }
    }
}
