#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Crm;

/// <summary>
/// Append-only log for engagement activity. There is no Update path — use a
/// new "correction" row if needed.
/// </summary>
public interface ICrmActivityStore
{
    Task<CrmActivity> AppendAsync(CrmActivity activity, string actorDisplay, CancellationToken ct);

    Task<IReadOnlyList<CrmActivity>> ListByEngagementAsync(long engagementId, CancellationToken ct);
}
