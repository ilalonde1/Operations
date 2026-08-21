#nullable enable
using System;
using System.IO;
using System.Linq;
using Kor.Operations.App.BusinessDevelopment.Workspace;
using Kor.Opportunities.Core.Models;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

public sealed class BazaarOpportunitySelectorTests
{
    [Fact]
    public void DefaultViewDropsExpiredDigestAndDuplicateRows()
    {
        var now = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        var liveDeadline = now.AddDays(3);

        var rows = new[]
        {
            Opp(1, "BCBID-123", "North Tower Seismic Upgrade", "City of Burnaby", liveDeadline, score: 72),
            Opp(2, "BCBIDENG-123", "North Tower - Seismic Upgrade", "Unknown", liveDeadline, score: 91),
            Opp(3, "BCBID-OLD", "Closed Friday Upgrade", "City of Burnaby", now.AddMinutes(-1), score: 99),
            Opp(4, "BDALERTS-abc", "APC Notification of New Postings", "APC", now.AddDays(5), score: 100),
            Opp(5, "BCBID-LATER", "Library Retrofit", "City of Surrey", now.AddDays(9), score: 60),
        };

        var selected = BazaarOpportunitySelector.SelectDefaultView(rows, now);

        Assert.Equal([2, 5], selected.Select(o => o.Id).ToArray());
        Assert.DoesNotContain(selected, o => o.OpportunityKey.StartsWith("BDALERTS-", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(selected, o => o.SubmissionDeadlineUtc <= now);
    }

    [Fact]
    public void EqualScoresOrderByEarliestLiveDeadline()
    {
        var now = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        var friday = now.AddDays(1);
        var nextMonth = now.AddDays(30);

        var selected = BazaarOpportunitySelector.SelectDefaultView(
            [
                Opp(1, "BCBID-LATE", "Later", "Buyer", nextMonth, score: 50),
                Opp(2, "BCBID-SOON", "Soon", "Buyer", friday, score: 50),
            ],
            now);

        Assert.Equal([2, 1], selected.Select(o => o.Id).ToArray());
    }

    [Fact]
    public void BazaarViewModelUsesTheDefaultSelector()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Kor.Operations.App", "BusinessDevelopment", "Workspace", "BazaarViewModel.cs"));

        Assert.Contains("BazaarOpportunitySelector.SelectDefaultView", source, StringComparison.Ordinal);
    }

    private static Opportunity Opp(long id, string key, string name, string buyer, DateTimeOffset? deadline, decimal score)
        => new()
        {
            Id = id,
            OpportunityKey = key,
            Name = name,
            BuyerName = buyer,
            Status = OpportunityStatus.New,
            SubmissionDeadlineUtc = deadline,
            RelevanceScore = score,
            UpdatedAtUtc = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero).AddMinutes(id),
        };

    private static string RepoRoot()
    {
        // There is no Kor.Operations.sln at the repo root - the only solution file is
        // Kor.Operations.App/Kor.Operations.App.sln. Anchor on a directory instead.
        var dir = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(dir, "Kor.Operations.App")))
            dir = Directory.GetParent(dir)?.FullName ?? throw new DirectoryNotFoundException("Could not find repo root.");
        return dir;
    }
}
