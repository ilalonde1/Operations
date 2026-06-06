#nullable enable
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Kor.Opportunities.Data.Briefs;

/// <summary>
/// Read-only data source for the BD Pursuit Brief feature. Returns typed records
/// that a separate generator (Phase 1B) renders into a Word document.
/// </summary>
public interface IBriefDataStore
{
    /// <summary>Returns the brief data for a specific live opportunity, or null if it does not exist.</summary>
    Task<OpportunityBriefData?> GetOpportunityBriefAsync(long opportunityId, CancellationToken ct);

    Task<IReadOnlyList<ProjectSearchRow>> SearchProjectsAsync(
        string query, int take, CancellationToken ct);

    Task<ProjectBriefData?> GetProjectBriefAsync(
        long mpiId, CancellationToken ct);

    /// <summary>Returns market-wide brief data for a province (and optional city fragment).</summary>
    Task<RegionBriefData> GetRegionBriefAsync(string province, string? city, CancellationToken ct);

    /// <summary>Returns brief data for a specific canonical organization, or null if it does not exist.</summary>
    Task<OrgBriefData?> GetOrgBriefAsync(long canonicalOrgId, CancellationToken ct);
}
