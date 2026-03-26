#nullable enable
using System.Collections.Generic;
using Kor.Operations.Core.Models.Proposal;

namespace Kor.Operations.Core.Services;

public interface IProposalStaffStore
{
    List<ProposalStaffMember> LoadAll();

    void SaveAll(List<ProposalStaffMember> staff);
}
