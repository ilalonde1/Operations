#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Kor.Operations.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations.App.FeeProposal
{
    public partial class ProposalProjectPickerDialog : Window
    {
        private readonly VantagepointRepository _repo;
        private IReadOnlyList<(string Wbs1, string Display)> _allProjects = Array.Empty<(string, string)>();

        public string? SelectedProjectName { get; private set; }
        public string? SelectedProjectNumber { get; private set; }

        private record ProjectItem(string Wbs1, string Display);

        public ProposalProjectPickerDialog()
        {
            InitializeComponent();
            _repo = Kor.Operations.Services.AppServices.Get<VantagepointRepository>();
        }

        protected override async void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            try
            {
                _allProjects = await _repo.GetProjectsAsync(onlyActive: true, yearsBack: 5, max: 2000);
                ApplyFilter(string.Empty);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Failed to load Vantagepoint project list in {Dialog}.", nameof(ProposalProjectPickerDialog));
                MessageBox.Show(
                    this,
                    $"Could not load the project list from Vantagepoint.\n\n{ex.Message}\n\nClose this dialog and try again.",
                    "Project picker",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void ApplyFilter(string q)
        {
            var items = string.IsNullOrWhiteSpace(q)
                ? _allProjects
                : _allProjects.Where(p =>
                    p.Display.Contains(q, StringComparison.OrdinalIgnoreCase));
            ResultsList.ItemsSource = items
                .Select(p => new ProjectItem(p.Wbs1, p.Display))
                .ToList();
        }

        private void SearchBtn_Click(object sender, RoutedEventArgs e)
            => ApplyFilter(SearchBox.Text?.Trim() ?? string.Empty);

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { e.Handled = true; SearchBtn_Click(sender, new RoutedEventArgs()); }
        }

        private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => TryConfirm();
        private void OkBtn_Click(object sender, RoutedEventArgs e) => TryConfirm();

        private void TryConfirm()
        {
            if (ResultsList.SelectedItem is ProjectItem item)
            {
                SelectedProjectNumber = item.Wbs1;
                var dash = item.Display.IndexOf(" - ", StringComparison.Ordinal);
                SelectedProjectName = dash >= 0 ? item.Display[(dash + 3)..].Trim() : item.Display;
                DialogResult = true;
            }
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
