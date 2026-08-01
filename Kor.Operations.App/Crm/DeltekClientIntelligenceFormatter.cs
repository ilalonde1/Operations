#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Kor.Operations.App.Crm;

internal static class DeltekClientIntelligenceFormatter
{
    private const int MaxProjects = 5;
    private const int MaxContacts = 5;
    private const int MaxActivity = 5;
    private const int MemoCharBudget = 400;

    /// <summary>
    /// Appends a multi-line Deltek client intelligence summary to <paramref name="sb"/>.
    /// Caller is responsible for any preceding blank line. Output is plain text;
    /// no markdown, no leading/trailing blank lines beyond the closing newline.
    /// Each section is gated on whether the corresponding sub-record is present.
    /// </summary>
    public static void Append(StringBuilder sb, DeltekClientIntelligence dc)
    {
        if (dc is null) throw new ArgumentNullException(nameof(dc));

        sb.AppendLine($"Deltek client history ({dc.ClientName}, ID {dc.ClientId}):");
        sb.AppendLine($"  Lifetime fee: {dc.LifetimeFee.ToString("C0", CultureInfo.CurrentCulture)} across {dc.ProjectCount} project(s).");

        if (dc.LatestProjectStart.HasValue)
        {
            var name = string.IsNullOrWhiteSpace(dc.LatestProjectName) ? "(unnamed)" : dc.LatestProjectName;
            sb.AppendLine($"  Most recent project: {name} (opened {dc.LatestProjectStart.Value:yyyy-MM-dd}).");
        }

        if (dc.Company is { } co)
        {
            var flags = new List<string>();
            if (co.PriorWork) flags.Add("PriorWork");
            if (co.Recommend) flags.Add("Recommend");
            if (co.GovernmentAgency) flags.Add("GovernmentAgency");
            if (co.Competitor) flags.Add("Competitor");
            if (flags.Count > 0)
            {
                sb.AppendLine($"  Flags: {string.Join(", ", flags)}");
            }

            AppendIfPresent(sb, "  Type", co.Type);
            AppendIfPresent(sb, "  Specialty", co.Specialty);
            AppendIfPresent(sb, "  Market", co.Market);
            AppendIfPresent(sb, "  Website", co.Website);
            if (co.AnnualRevenue.HasValue)
            {
                sb.AppendLine($"  Annual revenue: {co.AnnualRevenue.Value.ToString("C0", CultureInfo.CurrentCulture)}");
            }
        }

        if (dc.Ar is { } ar && ar.OpenInvoiceCount > 0)
        {
            sb.AppendLine($"  AR: outstanding {ar.TotalOutstanding.ToString("C0", CultureInfo.CurrentCulture)}, " +
                          $"90+ {ar.Outstanding90Plus.ToString("C0", CultureInfo.CurrentCulture)} " +
                          $"across {ar.OpenInvoiceCount} open invoice(s).");
        }

        if (dc.Projects.Count > 0)
        {
            sb.AppendLine($"  Top {Math.Min(MaxProjects, dc.Projects.Count)} project(s) by recency:");
            foreach (var p in dc.Projects.Take(MaxProjects))
            {
                var open = p.OpenDate.HasValue ? p.OpenDate.Value.ToString("yyyy-MM-dd") : "";
                sb.AppendLine($"    {p.Wbs1} {p.Name} (opened {open}); fee {p.Fee.ToString("C0", CultureInfo.CurrentCulture)}; " +
                              $"billed {p.FeeBilled.ToString("C0", CultureInfo.CurrentCulture)}");
            }
        }

        if (dc.Contacts.Count > 0)
        {
            sb.AppendLine($"  Top {Math.Min(MaxContacts, dc.Contacts.Count)} contact(s):");
            foreach (var p in dc.Contacts.Take(MaxContacts))
            {
                var primary = p.IsPrimary ? " [primary]" : string.Empty;
                var titlePart = string.IsNullOrWhiteSpace(p.Title) ? string.Empty : $" - {p.Title}";
                var emailPart = string.IsNullOrWhiteSpace(p.Email) ? string.Empty : $" - {p.Email}";
                sb.AppendLine($"    {p.FirstName} {p.LastName}{primary}{titlePart}{emailPart}");
            }
        }

        if (dc.RecentActivity.Count > 0)
        {
            sb.AppendLine($"  Recent Deltek activity (top {Math.Min(MaxActivity, dc.RecentActivity.Count)}):");
            foreach (var a in dc.RecentActivity.Take(MaxActivity))
            {
                var date = a.StartDate.HasValue ? a.StartDate.Value.ToString("yyyy-MM-dd") : "";
                var type = string.IsNullOrWhiteSpace(a.Type) ? "" : a.Type;
                var subj = string.IsNullOrWhiteSpace(a.Subject) ? "(no subject)" : a.Subject;
                sb.AppendLine($"    {date} [{type}] {subj}");
            }
        }

        if (dc.Company is { Memo: { Length: > 0 } memo })
        {
            var collapsed = CollapseWhitespace(memo);
            var truncated = collapsed.Length > MemoCharBudget
                ? collapsed.Substring(0, MemoCharBudget) + "..."
                : collapsed;
            sb.AppendLine($"  Memo: {truncated}");
        }

        if (dc.HasDegradedSections)
        {
            sb.AppendLine("  (Note: some Deltek sections were unavailable due to read permissions.)");
        }
    }

    private static void AppendIfPresent(StringBuilder sb, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.AppendLine($"{label}: {value.Trim()}");
    }

    private static string CollapseWhitespace(string s)
    {
        var sb = new StringBuilder(s.Length);
        var prevWs = false;
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!prevWs)
                {
                    sb.Append(' ');
                    prevWs = true;
                }
            }
            else
            {
                sb.Append(ch);
                prevWs = false;
            }
        }

        return sb.ToString().Trim();
    }
}
