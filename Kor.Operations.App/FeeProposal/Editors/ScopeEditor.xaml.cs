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
        }

        private ScopeBlockContent? Model => DataContext as ScopeBlockContent;

        private void AddService_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null) return;
            Model.IncludedServices.Add(new ScopeItem { Text = "New included service", IsActive = true });
            ServicesList.SelectedIndex = Model.IncludedServices.Count - 1;
        }

        private void RemoveService_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null || ServicesList.SelectedItem is not ScopeItem selected)
                return;

            Model.IncludedServices.Remove(selected);
        }
    }
}
