#nullable enable
using System.Collections.ObjectModel;
using System.Windows;
using Kor.Operations.Core.Models.Proposal;
using Kor.Operations.Core.Services;
using FeeProposalModel = Kor.Operations.Core.Models.Proposal.FeeProposal;

namespace Kor.Operations.App.FeeProposal
{
    public partial class OpenProposalDialog : Window
    {
        private readonly IFeeProposalStore _proposalStore;

        public ObservableCollection<FeeProposalSummary> Proposals { get; } = new();
        public FeeProposalModel? SelectedProposal { get; private set; }

        public OpenProposalDialog(IFeeProposalStore store)
        {
            _proposalStore = store;
            InitializeComponent();
            DataContext = this;
            Loaded += OpenProposalDialog_Loaded;
        }

        private async void OpenProposalDialog_Loaded(object sender, RoutedEventArgs e)
        {
            foreach (var p in await _proposalStore.LoadSummariesAsync())
                Proposals.Add(p);
        }

        private async void Open_Click(object sender, RoutedEventArgs e)
        {
            if (ProposalList.SelectedItem is FeeProposalSummary summary)
            {
                var proposal = await Task.Run(() => _proposalStore.LoadByIdAsync(summary.Id));
                if (proposal is not null)
                {
                    SelectedProposal = proposal;
                    DialogResult = true;
                }
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
