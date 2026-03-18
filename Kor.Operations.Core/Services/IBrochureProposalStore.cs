#nullable enable
using System.Collections.Generic;
using Kor.Operations.Core.Models.Brochure;

namespace Kor.Operations.Core.Services;

public interface IBrochureProposalStore
{
    void Save(BrochureProposal proposal);
    List<BrochureProposal> LoadAll();
    void Delete(string id);
}
