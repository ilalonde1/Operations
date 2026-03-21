#nullable enable
using System.Collections.Generic;
using Kor.Operations.Core.Models.Proposal;

namespace Kor.Operations.Core.Services;

public interface IFeeProposalStore
{
    void Save(FeeProposal proposal);

    List<FeeProposal> LoadAll();

    void Delete(string id);
}
