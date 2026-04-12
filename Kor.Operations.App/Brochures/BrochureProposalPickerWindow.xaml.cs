#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Data;
using Kor.Operations.Core.Models.Brochure;
using Kor.Operations.Core.Services;

namespace Kor.Operations.Brochures
{
    public partial class BrochureProposalPickerWindow : Window
    {
        private readonly IBrochureProposalStore _store;

        public BrochureProposal? SelectedProposal { get; private set; }
        public bool IsClone { get; private set; }

        public BrochureProposalPickerWindow(IBrochureProposalStore store)
        {
            _store = store;
            InitializeComponent();
            Loaded += BrochureProposalPickerWindow_Loaded;
            ProposalList.SelectionChanged += (_, _) => UpdateButtons();
        }

        private async void BrochureProposalPickerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            var proposals = await _store.LoadAllAsync();
            ProposalList.ItemsSource = proposals;
            EmptyHint.Visibility = proposals.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            var hasSelection = ProposalList.SelectedItem is BrochureProposal;
            OpenButton.IsEnabled = hasSelection;
            CloneButton.IsEnabled = hasSelection;
            DeleteButton.IsEnabled = hasSelection;
        }

        private async void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProposalList.SelectedItem is not BrochureProposal summary)
                return;

            OpenButton.IsEnabled = false;
            CloneButton.IsEnabled = false;

            var proposal = await Task.Run(() => _store.LoadAsync(summary.Id));
            if (proposal is null)
            {
                UpdateButtons();
                return;
            }

            SelectedProposal = proposal;
            IsClone = false;
            DialogResult = true;
        }

        private async void CloneButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProposalList.SelectedItem is not BrochureProposal summary)
                return;

            OpenButton.IsEnabled = false;
            CloneButton.IsEnabled = false;

            var proposal = await Task.Run(() => _store.LoadAsync(summary.Id));
            if (proposal is null)
            {
                UpdateButtons();
                return;
            }

            SelectedProposal = proposal;
            IsClone = true;
            DialogResult = true;
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProposalList.SelectedItem is not BrochureProposal proposal)
                return;

            var result = MessageBox.Show(
                $"Delete \"{proposal.Name}\"? This cannot be undone.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            await _store.DeleteAsync(proposal.Id);
            await RefreshAsync();
        }

        private void ProposalList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ProposalList.SelectedItem is BrochureProposal)
                OpenButton_Click(sender, e);
        }
    }

    public sealed class ProposalSummaryConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not List<BrochureBlock> blocks)
                return string.Empty;

            var projects = blocks
                .Where(b => b.BlockType == BrochureBlockType.Section)
                .Sum(b => b.Section?.Projects.Count ?? 0);

            var sections = blocks.Count(b => b.BlockType == BrochureBlockType.Section);

            return $"{sections} section{(sections != 1 ? "s" : "")}    {projects} project{(projects != 1 ? "s" : "")}";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
