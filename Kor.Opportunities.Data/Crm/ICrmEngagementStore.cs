#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Crm;

/// <summary>
/// Read/write access to <c>opportunities.CrmEngagements</c>. RowVersion is
/// the concurrency token on every update path; stale tokens raise
/// <see cref="CrmConcurrencyException"/>.
/// </summary>
public interface ICrmEngagementStore
{
    Task<IReadOnlyList<CrmEngagement>> ListAsync(CancellationToken ct);

    Task<CrmEngagement?> GetByIdAsync(long id, CancellationToken ct);

    /// <summary>Returns null if no engagement exists for the opportunity yet.</summary>
    Task<CrmEngagement?> GetByOpportunityAsync(long opportunityId, CancellationToken ct);

    Task<CrmEngagement> InsertAsync(CrmEngagement engagement, string actorDisplay, CancellationToken ct);

    Task<CrmEngagement> UpdateAsync(CrmEngagement engagement, string actorDisplay, CancellationToken ct);
}
