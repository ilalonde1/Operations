#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Deltek;
using Microsoft.Extensions.Caching.Memory;

namespace Kor.Operations.Data.Deltek;

public sealed class CachingKorWonProjectAccessor : IKorWonProjectAccessor
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IKorWonProjectAccessor _inner;
    private readonly IMemoryCache _cache;

    public CachingKorWonProjectAccessor(IKorWonProjectAccessor inner, IMemoryCache cache)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public Task<IReadOnlyList<KorWonProjectRow>> GetForClientAsync(
        string clendorClientId,
        int maxRows,
        CancellationToken ct)
    {
        var clientId = (clendorClientId ?? string.Empty).Trim();
        if (clientId.Length == 0 || maxRows <= 0)
        {
            return Task.FromResult<IReadOnlyList<KorWonProjectRow>>(Array.Empty<KorWonProjectRow>());
        }

        var take = Math.Min(maxRows, 200);
        var key = $"kor-won-projects:{clientId.ToUpperInvariant()}:{take}";
        if (_cache.TryGetValue(key, out IReadOnlyList<KorWonProjectRow>? hit) && hit is not null)
        {
            return Task.FromResult(hit);
        }

        return LoadAndCacheAsync(clientId, take, key, ct);
    }

    public Task<IReadOnlyList<KorWonProjectAggregate>> GetAllClientAggregatesAsync(CancellationToken ct)
        => _inner.GetAllClientAggregatesAsync(ct);

    private async Task<IReadOnlyList<KorWonProjectRow>> LoadAndCacheAsync(
        string clientId,
        int maxRows,
        string key,
        CancellationToken ct)
    {
        var rows = await _inner.GetForClientAsync(clientId, maxRows, ct).ConfigureAwait(false);
        _cache.Set(key, rows, CacheTtl);
        return rows;
    }
}
