#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core.Models.Proposal;

namespace Kor.Operations.Core.Services;

public interface IProposalStaffStore
{
    Task<List<ProposalStaffMember>> LoadAllAsync(CancellationToken ct = default);

    Task SaveAllAsync(List<ProposalStaffMember> staff, CancellationToken ct = default);
}
