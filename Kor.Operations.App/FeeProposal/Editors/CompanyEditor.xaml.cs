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
            Loaded += (_, _) => RefreshSections();
        }

        private CompanyBlockContent? Model => DataContext as CompanyBlockContent;

        private void RefreshSections()
        {
            SectionsList.ItemsSource = null;
            SectionsList.ItemsSource = Model?.Sections;
            SectionsList.Items.Refresh();
        }

        private void AddSection_Click(object sender, RoutedEventArgs e)
        {
            Model?.Sections.Add(new CompanySection { Title = "New section" });
            RefreshSections();
            SectionsList.SelectedIndex = Model!.Sections.Count - 1;
        }

        private void RemoveSection_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null || SectionsList.SelectedItem is not CompanySection selected)
                return;

            Model.Sections.Remove(selected);
            RefreshSections();
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
            if (SectionsList.SelectedItem is not CompanySection selected)
                return;

            selected.Title = SectionTitleText.Text;
            SectionsList.Items.Refresh();
        }

        private void SectionBodyText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SectionsList.SelectedItem is not CompanySection selected)
                return;

            selected.Body = SectionBodyText.Text;
            SectionsList.Items.Refresh();
        }
    }
}
