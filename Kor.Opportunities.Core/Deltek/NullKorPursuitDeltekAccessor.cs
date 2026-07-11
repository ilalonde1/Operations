#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Core.Deltek;

public sealed class NullKorPursuitDeltekAccessor : IKorPursuitDeltekAccessor
{
    public Task<IReadOnlyList<DeltekPursuitRow>> GetExplicitStagePursuitsAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<DeltekPursuitRow>>(Array.Empty<DeltekPursuitRow>());

    public Task<IReadOnlyList<DeltekPursuitRow>> GetPromotionalPursuitsAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<DeltekPursuitRow>>(Array.Empty<DeltekPursuitRow>());

    public Task<IReadOnlyList<DeltekPursuitRow>> GetPursuitsByWbs1Async(IReadOnlyCollection<string> wbs1Keys, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<DeltekPursuitRow>>(Array.Empty<DeltekPursuitRow>());
}
