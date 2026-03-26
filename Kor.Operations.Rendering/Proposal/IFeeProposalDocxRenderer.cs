#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core.Models.Proposal;

namespace Kor.Operations.Rendering.Proposal
{
    public interface IFeeProposalDocxRenderer
    {
        Task<string> RenderAsync(
            FeeProposal proposal,
            IReadOnlyList<ProposalStaffMember> staff,
            string outputPath,
            CancellationToken ct);
    }
}
