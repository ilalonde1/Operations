#nullable enable
using System.Collections.Generic;
using Kor.Operations.Core.Models.Proposal;

namespace Kor.Operations.Core.Services;

public interface IProposalBlockLibraryStore
{
    void Save(ProposalBlockTemplate template);

    List<ProposalBlockTemplate> LoadAll();

    void Delete(string id);
}
