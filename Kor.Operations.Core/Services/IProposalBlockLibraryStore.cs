#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core.Models.Proposal;

namespace Kor.Operations.Core.Services;

public interface IProposalBlockLibraryStore
{
    Task SaveAsync(ProposalBlockTemplate template, CancellationToken ct = default);

    Task<List<ProposalBlockTemplate>> LoadAllAsync(CancellationToken ct = default);

    Task DeleteAsync(string id, CancellationToken ct = default);
}
