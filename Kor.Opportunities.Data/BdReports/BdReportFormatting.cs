#nullable enable
namespace Kor.Opportunities.Data.BdReports;

public static class BdReportFormatting
{
    public const string SourceProvenance = "Project details are as last researched; confirm status before acting.";

    public static string EvidenceNote(System.Collections.Generic.IEnumerable<PursuitBriefRow> rows)
    {
        var list = rows.ToList();
        var dates = list.Where(x => x.LastRefreshAtUtc.HasValue).Select(x => x.LastRefreshAtUtc!.Value).ToList();
        var datesNote = dates.Count == 0
            ? SourceProvenance
            : "Project details are as last researched (" + ResearchedRange(dates.Min(), dates.Max()) + "); confirm status before acting.";
        var refreshNotes = list.Select(x => x.RefreshNote).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct();
        return string.Join(" ", new[] { datesNote }.Concat(refreshNotes));
    }

    /// <summary>Reader-facing date, e.g. "22 Oct 2026" — culture-invariant.</summary>
    public static string ReaderDate(System.DateTimeOffset value) =>
        value.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);

    private static string ResearchedRange(System.DateTimeOffset first, System.DateTimeOffset last)
    {
        var a = ReaderDate(first);
        var b = ReaderDate(last);
        return a == b ? a : a + " to " + b;
    }
    public static string CostCoverage(int? costed, int? population) =>
        costed.HasValue && population.HasValue
            ? $"{costed}/{population} projects costed; unknown costs excluded"
            : "cost coverage unavailable; unknown costs excluded";

    /// <summary>What a clipped note ends with. A cut recommendation must say so ("Do not approach yet" can be the part
    /// cut), and the project name links to its brief, where the whole note is.</summary>
    public const string AbridgedMark = "… (continued in the project brief)";

    // A research note opens with its rating code ("PURSUE_URGENT — B+H Architects confirmed…"); the report already
    // shows the rating as its section, so the code is noise to a reader (2026-10-22).
    private static readonly System.Text.RegularExpressions.Regex LeadingRatingCode = new(
        @"^\s*(PURSUE_URGENT|PURSUE|MONITOR|DISCOVER|DEAD|DUPLICATE)\s*[—–:\-]+\s*",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>A research note as a reader gets it: no leading rating code, clipped to max with the mark.</summary>
    public static string Abridge(string value, int max)
    {
        var text = LeadingRatingCode.Replace(value, "");
        return text.Length <= max ? text : text[..max].TrimEnd() + AbridgedMark;
    }

    /// <summary>A cost as a reader gets it: a bare number in the cost text ("159000000") is written as dollars.</summary>
    public static string Cost(string? costText, decimal? costCad, string unknown)
    {
        if (!string.IsNullOrWhiteSpace(costText))
        {
            var t = costText.Trim();
            return decimal.TryParse(t, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var n)
                ? "$" + n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
                : t;
        }

        return costCad is { } cad ? "$" + cad.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) : unknown;
    }
}
