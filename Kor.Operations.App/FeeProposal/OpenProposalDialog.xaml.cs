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
        public ObservableCollection<FeeProposalModel> Proposals { get; } = new();
        public FeeProposalModel? SelectedProposal { get; private set; }

        public OpenProposalDialog(FeeProposalStore store)
        {
            InitializeComponent();
            DataContext = this;
            foreach (var p in store.LoadAll())
                Proposals.Add(p);
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            if (ProposalList.SelectedItem is FeeProposalModel p)
            {
                SelectedProposal = p;
                DialogResult = true;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
