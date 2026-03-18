#nullable enable
using System.Collections.Generic;
using Kor.Operations.Core.Models.Brochure;

namespace Kor.Operations.Core.Services;

/// <summary>
/// Defines persistence operations for brochure proposals.
/// </summary>
public interface IBrochureProposalStore
{
    /// <summary>
    /// Saves the supplied brochure proposal.
    /// </summary>
    /// <param name="proposal">The proposal to persist.</param>
    void Save(BrochureProposal proposal);

    /// <summary>
    /// Loads all stored brochure proposals.
    /// </summary>
    /// <returns>The stored brochure proposals.</returns>
    List<BrochureProposal> LoadAll();

    /// <summary>
    /// Deletes the brochure proposal with the specified identifier.
    /// </summary>
    /// <param name="id">The proposal identifier.</param>
    void Delete(string id);
}
