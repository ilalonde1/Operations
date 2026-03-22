#nullable enable
using System.Collections.Generic;
using System;
using Kor.Operations.Core.Models.Proposal;

namespace Kor.Operations.Core.Services;

public record FeeProposalSummary(string Id, string Name, DateTime ModifiedAt);

public interface IFeeProposalStore
{
    void Save(FeeProposal proposal);

    List<FeeProposal> LoadAll();

    IReadOnlyList<FeeProposalSummary> LoadSummaries();

    void Delete(string id);
}
