#nullable enable
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.Core.Models.Proposal;

namespace Kor.Operations.App.FeeProposal.Editors
{
    public partial class ScopeEditor : UserControl
    {
        public ScopeEditor()
        {
            InitializeComponent();
            Loaded += (_, _) => RefreshServices();
        }

        private ScopeBlockContent? Model => DataContext as ScopeBlockContent;

        private void RefreshServices()
        {
            ServicesList.ItemsSource = null;
            ServicesList.ItemsSource = Model?.IncludedServices;
        }

        private void AddService_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null) return;
            Model.IncludedServices.Add(new ScopeItem { Text = "New included service", IsActive = true });
            RefreshServices();
            ServicesList.SelectedIndex = Model.IncludedServices.Count - 1;
        }

        private void RemoveService_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null || ServicesList.SelectedItem is not ScopeItem selected)
                return;

            Model.IncludedServices.Remove(selected);
            RefreshServices();
        }
    }
}
