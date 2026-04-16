#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core.Models.Brochure;

namespace Kor.Operations.Core.Services;

public sealed class CompositeBrochureProposalStore : IBrochureProposalStore
{
    private readonly IReadOnlyList<IBrochureProposalStore> _stores;

    public CompositeBrochureProposalStore(IEnumerable<IBrochureProposalStore> stores)
    {
        _stores = stores?.ToList() ?? throw new ArgumentNullException(nameof(stores));
        if (_stores.Count == 0)
        {
            throw new ArgumentException("At least one brochure proposal store is required.", nameof(stores));
        }
    }

    public async Task SaveAsync(BrochureProposal proposal, CancellationToken ct = default)
    {
        Exception? firstError = null;
        var successCount = 0;

        foreach (var store in _stores)
        {
            try
            {
                await store.SaveAsync(proposal, ct).ConfigureAwait(false);
                successCount++;
            }
            catch (Exception ex)
            {
                firstError ??= ex;
            }
        }

        if (successCount == 0 && firstError is not null)
        {
            throw firstError;
        }
    }

    public async Task<List<BrochureProposal>> LoadAllAsync(CancellationToken ct = default)
    {
        var all = new List<BrochureProposal>();
        foreach (var store in _stores)
        {
            all.AddRange(await store.LoadAllAsync(ct).ConfigureAwait(false));
        }

        return all
            .GroupBy(static proposal => proposal.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group
                .OrderByDescending(proposal => proposal.ModifiedAt)
                .First())
            .OrderByDescending(static proposal => proposal.ModifiedAt)
            .ToList();
    }

    public async Task<BrochureProposal?> LoadAsync(string id, CancellationToken ct = default)
    {
        foreach (var store in _stores)
        {
            var proposal = await store.LoadAsync(id, ct).ConfigureAwait(false);
            if (proposal is not null)
            {
                return proposal;
            }
        }
        return null;
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        Exception? firstError = null;

        foreach (var store in _stores)
        {
            try
            {
                await store.DeleteAsync(id, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                firstError ??= ex;
            }
        }

        if (firstError is not null)
        {
            throw firstError;
        }
    }
}
