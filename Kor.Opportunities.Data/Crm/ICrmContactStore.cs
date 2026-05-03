#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Crm;

/// <summary>
/// Buyer-side contacts on a CRM engagement. RowVersion drives optimistic
/// concurrency; <see cref="UpdateAsync"/> raises <see cref="CrmConcurrencyException"/>
/// on stale tokens.
/// </summary>
public interface ICrmContactStore
{
    Task<CrmContact> InsertAsync(CrmContact contact, string actorDisplay, CancellationToken ct);

    Task<CrmContact> UpdateAsync(CrmContact contact, string actorDisplay, CancellationToken ct);

    Task DeleteAsync(long contactId, CancellationToken ct);

    Task<IReadOnlyList<CrmContact>> ListByEngagementAsync(long engagementId, CancellationToken ct);
}
