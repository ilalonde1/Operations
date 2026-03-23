#nullable enable
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.Core.Models.Proposal;

namespace Kor.Operations.App.FeeProposal.Editors
{
    public partial class ProjectDescriptionEditor : UserControl
    {
        public ProjectDescriptionEditor()
        {
            InitializeComponent();
        }

        private ProjectDescriptionBlockContent? Model => DataContext as ProjectDescriptionBlockContent;

        private void AddBullet_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null) return;
            Model.AssumptionBullets.Add("New assumption");
            BulletsList.SelectedIndex = Model.AssumptionBullets.Count - 1;
        }

        private void RemoveBullet_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null || BulletsList.SelectedItem is not string selected)
                return;

            Model.AssumptionBullets.Remove(selected);
            SelectedBulletText.Text = string.Empty;
        }

        private void BulletsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectedBulletText.Text = BulletsList.SelectedItem as string ?? string.Empty;
        }

        private void SelectedBulletText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Model is null)
                return;

            var selectedIndex = BulletsList.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= Model.AssumptionBullets.Count)
                return;

            Model.AssumptionBullets[selectedIndex] = SelectedBulletText.Text;
        }
    }
}
