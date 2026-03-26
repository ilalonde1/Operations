#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.Core.Models.Brochure;

namespace Kor.Operations.Core.Services;

/// <summary>
/// Combines multiple brochure proposal stores into a single logical store.
/// </summary>
public sealed class CompositeBrochureProposalStore : IBrochureProposalStore
{
    private readonly IReadOnlyList<IBrochureProposalStore> _stores;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeBrochureProposalStore"/> class.
    /// </summary>
    /// <param name="stores">The underlying stores to use.</param>
    public CompositeBrochureProposalStore(IEnumerable<IBrochureProposalStore> stores)
    {
        _stores = stores?.ToList() ?? throw new ArgumentNullException(nameof(stores));
        if (_stores.Count == 0)
        {
            throw new ArgumentException("At least one brochure proposal store is required.", nameof(stores));
        }
    }

    /// <inheritdoc />
    public void Save(BrochureProposal proposal)
    {
        Exception? firstError = null;
        var successCount = 0;

        foreach (var store in _stores)
        {
            try
            {
                store.Save(proposal);
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

    /// <inheritdoc />
    public List<BrochureProposal> LoadAll() =>
        _stores.SelectMany(static store => store.LoadAll())
            .GroupBy(static proposal => proposal.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group
                .OrderByDescending(proposal => proposal.ModifiedAt)
                .First())
            .OrderByDescending(static proposal => proposal.ModifiedAt)
            .ToList();

    /// <inheritdoc />
    public void Delete(string id)
    {
        Exception? firstError = null;

        foreach (var store in _stores)
        {
            try
            {
                store.Delete(id);
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
