#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Core.Deltek;

public sealed class NullKorWonProjectAccessor : IKorWonProjectAccessor
{
    public Task<IReadOnlyList<KorWonProjectRow>> GetForClientAsync(
        string clendorClientId,
        int maxRows,
        CancellationToken ct)
        => Task.FromResult<IReadOnlyList<KorWonProjectRow>>(Array.Empty<KorWonProjectRow>());

    public Task<IReadOnlyList<KorWonProjectAggregate>> GetAllClientAggregatesAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<KorWonProjectAggregate>>(Array.Empty<KorWonProjectAggregate>());
}
