#nullable enable
using System;
using System.Linq;
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
            Loaded += (_, _) => ClientList.ItemsSource = Model?.ClientNames;
        }

        private ReferencesBlockContent? Model => DataContext as ReferencesBlockContent;

        private void AddClient_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null) return;
            Model.ClientNames.Add("New client");
            ClientList.SelectedIndex = Model.ClientNames.Count - 1;
        }

        private void RemoveClient_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null || ClientList.SelectedItem is not string selected)
                return;

            Model.ClientNames.Remove(selected);
            SelectedClientText.Text = string.Empty;
        }

        private void ClientList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectedClientText.Text = ClientList.SelectedItem as string ?? string.Empty;
        }

        private void PasteClients_Click(object sender, RoutedEventArgs e)
        {
            if (Model is null) return;

            var text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text)) return;

            var names = text
                .Split(new[] { '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().TrimEnd('.'))
                .Where(s => s.Length > 1)
                .ToList();

            foreach (var name in names)
                if (!Model.ClientNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    Model.ClientNames.Add(name);
        }

        private void SelectedClientText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Model is null)
                return;

            var selectedIndex = ClientList.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= Model.ClientNames.Count)
                return;

            Model.ClientNames[selectedIndex] = SelectedClientText.Text;
        }
    }
}
