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
            foreach (var p in store.LoadSummaries())
                Proposals.Add(p);
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            if (ProposalList.SelectedItem is FeeProposalSummary summary)
            {
                var proposal = _proposalStore.LoadById(summary.Id);
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
