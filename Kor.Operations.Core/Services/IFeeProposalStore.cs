#nullable enable
using System;
using System.Collections.Generic;

using Kor.Operations.Core.Models.Proposal;

namespace Kor.Operations.Core.Services;

public record FeeProposalSummary(string Id, string Name, DateTime ModifiedAt);

public interface IFeeProposalStore
{
    void Save(FeeProposal proposal);

    List<FeeProposal> LoadAll();

    FeeProposal? LoadById(string id);

    IReadOnlyList<FeeProposalSummary> LoadSummaries();

    void Delete(string id);
}
