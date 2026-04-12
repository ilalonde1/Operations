#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Kor.Operations.Core.Models.Proposal;

namespace Kor.Operations.Core.Services;

public record FeeProposalSummary(string Id, string Name, DateTime ModifiedAt);

public interface IFeeProposalStore
{
    Task SaveAsync(FeeProposal proposal, CancellationToken ct = default);

    Task<List<FeeProposal>> LoadAllAsync(CancellationToken ct = default);

    Task<FeeProposal?> LoadByIdAsync(string id, CancellationToken ct = default);

    Task<IReadOnlyList<FeeProposalSummary>> LoadSummariesAsync(CancellationToken ct = default);

    Task DeleteAsync(string id, CancellationToken ct = default);
}
