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
            Loaded += (_, _) => RefreshBullets();
        }

        private ProjectDescriptionBlockContent? Model => DataContext as ProjectDescriptionBlockContent;

        private void RefreshBullets()
        {
            BulletsList.ItemsSource = null;
            BulletsList.ItemsSource = Model?.AssumptionBullets;
        }

        private void AddBullet_Click(object sender, RoutedEventArgs e)
        {
            Model?.AssumptionBullets.Add("New assumption");
            RefreshBullets();
            BulletsList.SelectedIndex = Model!.AssumptionBullets.Count - 1;
        }

        private void RemoveBullet_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null || BulletsList.SelectedItem is not string selected)
                return;

            Model.AssumptionBullets.Remove(selected);
            RefreshBullets();
            SelectedBulletText.Text = string.Empty;
        }

        private void BulletsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectedBulletText.Text = BulletsList.SelectedItem as string ?? string.Empty;
        }

        private void SelectedBulletText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Model is null || BulletsList.SelectedIndex < 0)
                return;

            Model.AssumptionBullets[BulletsList.SelectedIndex] = SelectedBulletText.Text;
            RefreshBullets();
            BulletsList.SelectedIndex = System.Math.Min(BulletsList.SelectedIndex, Model.AssumptionBullets.Count - 1);
        }
    }
}
