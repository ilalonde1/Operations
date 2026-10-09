#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Opportunities;

public sealed record OpportunityHubRecord(Opportunity Opportunity, OpportunityKind Kind, DateTimeOffset? FiledAtUtc, bool? IsBigTicket = null);

public interface IOpportunityHubStore
{
    /// <summary>All undismissed opportunities, including closed/awarded rows, read in bounded ID pages.</summary>
    Task<IReadOnlyList<OpportunityHubRecord>> ListHubAsync(CancellationToken ct);
}
