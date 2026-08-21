#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Kor.Opportunities.Core.Models;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

internal static class BazaarOpportunitySelector
{
    public static IReadOnlyList<Opportunity> SelectDefaultView(IEnumerable<Opportunity> opportunities, DateTimeOffset nowUtc)
        => opportunities
            .Where(o => IsDefaultCandidate(o, nowUtc))
            .GroupBy(SameTenderKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(o => o.RelevanceScore.HasValue)
                .ThenByDescending(o => o.RelevanceScore)
                .ThenBy(o => o.SubmissionDeadlineUtc ?? DateTimeOffset.MaxValue)
                .ThenByDescending(o => o.UpdatedAtUtc)
                .First())
            .OrderByDescending(o => o.RelevanceScore.HasValue)
            .ThenByDescending(o => o.RelevanceScore)
            .ThenBy(o => o.SubmissionDeadlineUtc ?? DateTimeOffset.MaxValue)
            .ThenByDescending(o => o.UpdatedAtUtc)
            .ToList();

    private static bool IsDefaultCandidate(Opportunity o, DateTimeOffset nowUtc)
        => o.Status == OpportunityStatus.New
           && string.IsNullOrWhiteSpace(o.OwnerStaffId)
           && o.DismissedAtUtc is null
           && (o.SubmissionDeadlineUtc is null || o.SubmissionDeadlineUtc > nowUtc)
           && !SourcePrefix(o).Equals("BDALERTS", StringComparison.OrdinalIgnoreCase);

    internal static string SameTenderKey(Opportunity o)
    {
        var title = Normalize(o.Name);
        var deadline = o.SubmissionDeadlineUtc?.UtcDateTime.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(deadline))
            return $"{title}|deadline:{deadline}";

        var buyer = NormalizeBuyer(o.BuyerName);
        return $"{title}|buyer:{buyer}";
    }

    private static string SourcePrefix(Opportunity o)
    {
        var key = o.OpportunityKey ?? string.Empty;
        var dash = key.IndexOf('-');
        return dash > 0 ? key[..dash].Trim() : key.Trim();
    }

    private static string NormalizeBuyer(string? value)
    {
        var normalized = Normalize(value);
        return normalized is "" or "unknown" or "manual" ? "" : normalized;
    }

    private static string Normalize(string? value)
    {
        var s = (value ?? string.Empty).Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"[^\p{L}\p{Nd}]+", " ");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }
}
