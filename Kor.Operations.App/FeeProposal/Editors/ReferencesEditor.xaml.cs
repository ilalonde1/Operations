#nullable enable
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.Core.Models.Proposal;

namespace Kor.Operations.App.FeeProposal.Editors
{
    public partial class ReferencesEditor : UserControl
    {
        public ReferencesEditor()
        {
            InitializeComponent();
            Loaded += (_, _) => RefreshClients();
        }

        private ReferencesBlockContent? Model => DataContext as ReferencesBlockContent;

        private void RefreshClients()
        {
            ClientList.ItemsSource = null;
            ClientList.ItemsSource = Model?.ClientNames;
        }

        private void AddClient_Click(object sender, RoutedEventArgs e)
        {
            Model?.ClientNames.Add("New client");
            RefreshClients();
            ClientList.SelectedIndex = Model!.ClientNames.Count - 1;
        }

        private void RemoveClient_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null || ClientList.SelectedItem is not string selected)
                return;

            Model.ClientNames.Remove(selected);
            RefreshClients();
            SelectedClientText.Text = string.Empty;
        }

        private void ClientList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectedClientText.Text = ClientList.SelectedItem as string ?? string.Empty;
        }

        private void SelectedClientText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Model is null)
                return;

            var selectedIndex = ClientList.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= Model.ClientNames.Count)
                return;

            Model.ClientNames[selectedIndex] = SelectedClientText.Text;
            ClientList.Items.Refresh();
        }
    }
}
