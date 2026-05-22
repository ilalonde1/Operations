#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Awards;

public interface IVendorAnalyticsStore
{
    Task<CompetitorProfile> GetCompetitorProfileAsync(string vendorName, CancellationToken ct);
    Task<BuyerProfile> GetBuyerProfileAsync(string buyerName, CancellationToken ct);
}
