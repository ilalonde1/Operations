#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Awards;

public interface IOpportunityAwardStore
{
    /// <summary>Insert or refresh by (SourceId, ExternalReference). Returns the
    /// new row Id for an insert, or 0 when an existing row was updated.</summary>
    Task<long> UpsertAsync(OpportunityAward award, CancellationToken ct);

    Task<IReadOnlyList<OpportunityAward>> ListRecentAsync(int max, CancellationToken ct);
}
