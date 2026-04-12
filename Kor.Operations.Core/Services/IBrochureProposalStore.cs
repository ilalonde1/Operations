#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core.Models.Brochure;

namespace Kor.Operations.Core.Services;

public interface IBrochureProposalStore
{
    Task SaveAsync(BrochureProposal proposal, CancellationToken ct = default);

    Task<List<BrochureProposal>> LoadAllAsync(CancellationToken ct = default);

    Task<BrochureProposal?> LoadAsync(string id, CancellationToken ct = default);

    Task DeleteAsync(string id, CancellationToken ct = default);
}
