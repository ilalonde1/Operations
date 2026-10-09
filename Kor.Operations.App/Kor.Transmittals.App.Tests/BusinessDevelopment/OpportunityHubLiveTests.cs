#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.BusinessDevelopment.Workspace;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Opportunities;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// Opt-in read-only gate: compares paged hub IDs to an independent unbounded query,
/// checks the kind partition, and measures both legacy doors on the same database.
/// Does not check visuals or perform claims/dismissals. A wrong source metadata value
/// would pass this reachability check; source classification has separate unit coverage.
/// </summary>
public sealed class OpportunityHubLiveTests(ITestOutputHelper output)
{
    [LiveHubFact]
    public async Task PagedHubReachesEveryUndismissedRowExactlyOnce()
    {
        var connection = Environment.GetEnvironmentVariable("KOR_HUB_VERIFY_DB")!;
        var store = new SqlOpportunityStore(connection);
        var hub = await store.ListHubAsync(CancellationToken.None);
        await using var con = new SqlConnection(connection);
        await con.OpenAsync();
        await using var command = new SqlCommand("SELECT Id FROM opportunities.Opportunities WHERE DismissedAtUtc IS NULL ORDER BY Id;", con);
        await using var reader = await command.ExecuteReaderAsync();
        var expectedIds = new List<long>();
        while (await reader.ReadAsync()) expectedIds.Add(reader.GetInt64(0));
        Assert.Equal(expectedIds, hub.Select(r => r.Opportunity.Id));
        Assert.Equal(hub.Count, hub.Select(r => r.Opportunity.Id).Distinct().Count());
        var signals = hub.Count(r => r.Kind is OpportunityKind.Application or OpportunityKind.Other);
        var bids = hub.Count(r => r.Kind is OpportunityKind.Bid or OpportunityKind.Award);
        Assert.Equal(hub.Count, signals + bids);
        var legacy = await store.ListAsync(CancellationToken.None, includeClosed: false, includeNonPrime: true);
        var grab = BazaarOpportunitySelector.SelectDefaultView(legacy, DateTimeOffset.UtcNow);
        output.WriteLine($"Legacy loaded={legacy.Count}; default prime-only={legacy.Count(o => o.IsPrimeConsultantRfp == true)}; To grab={grab.Count}");
        output.WriteLine($"Hub total={hub.Count}; Signals={signals}; Bids={bids}; owned={hub.Count(r => !string.IsNullOrWhiteSpace(r.Opportunity.OwnerStaffId))}");
        foreach (var group in hub.GroupBy(r => r.Kind).OrderBy(g => g.Key)) output.WriteLine($"{group.Key}={group.Count()}");
    }

    private sealed class LiveHubFactAttribute : FactAttribute
    {
        public LiveHubFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KOR_HUB_VERIFY_DB")))
                Skip = "Opt-in live read: set KOR_HUB_VERIFY_DB in the process environment.";
        }
    }
}
