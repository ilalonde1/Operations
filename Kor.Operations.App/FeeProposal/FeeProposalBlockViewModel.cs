#nullable enable
using Kor.Operations.Core;
using Kor.Operations.Core.Models.Proposal;

namespace Kor.Operations.App.FeeProposal
{
    public sealed class FeeProposalBlockViewModel : ObservableObject
    {
        public FeeProposalBlock Block { get; }

        public string TemplateName
        {
            get => Block.TemplateName;
            set
            {
                Block.TemplateName = value;
                OnPropertyChanged();
            }
        }

        public ProposalBlockType BlockType => Block.BlockType;

        public string Summary => Block.GetContentAs<ISummarizable>()?.GetSummary() ?? string.Empty;

        public FeeProposalBlockViewModel(FeeProposalBlock block)
        {
            Block = block;
        }
    }
}
