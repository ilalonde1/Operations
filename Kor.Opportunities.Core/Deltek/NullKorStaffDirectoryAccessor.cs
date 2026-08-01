#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Opportunities.Core.Deltek;

/// <summary>Bound when Deltek is unavailable; the sync job no-ops on it.</summary>
public sealed class NullKorStaffDirectoryAccessor : IKorStaffDirectoryAccessor
{
    public Task<IReadOnlyList<DeltekStaffRow>> GetActiveStaffAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<DeltekStaffRow>>(new List<DeltekStaffRow>());
}
