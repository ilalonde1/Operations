#nullable enable
using System.Windows;
using System.Windows.Input;
using Kor.Operations.Core.Models.Brochure;
using Kor.Operations.Core.Services;

namespace Kor.Operations.GeneralTools
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
            Refresh();
            ProposalList.SelectionChanged += (_, _) => UpdateButtons();
        }

        private void Refresh()
        {
            var proposals = _store.LoadAll();
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

        private void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProposalList.SelectedItem is not BrochureProposal proposal)
                return;

            SelectedProposal = proposal;
            IsClone = false;
            DialogResult = true;
        }

        private void CloneButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProposalList.SelectedItem is not BrochureProposal proposal)
                return;

            SelectedProposal = proposal;
            IsClone = true;
            DialogResult = true;
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
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

            _store.Delete(proposal.Id);
            Refresh();
        }

        private void ProposalList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ProposalList.SelectedItem is BrochureProposal)
                OpenButton_Click(sender, e);
        }
    }
}
