#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Bids;

public interface IOpportunityBidStore
{
    /// <summary>Insert or refresh by (SourceId, ExternalReference,
    /// BidderName). Returns the new row Id for an insert, or 0 when an
    /// existing row was updated.</summary>
    Task<long> UpsertAsync(OpportunityBid bid, CancellationToken ct);

    Task<int> ListBidderCountForAsync(Guid sourceId, string externalReference, CancellationToken ct);
}
