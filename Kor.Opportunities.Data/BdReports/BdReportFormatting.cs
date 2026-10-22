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

    public static string Abridge(string value, int max) =>
        value.Length <= max ? value : value[..max] + "... [abridged]";
}
