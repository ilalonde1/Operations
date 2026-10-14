#nullable enable
namespace Kor.Opportunities.Data.BdReports;

public static class BdReportFormatting
{
    public const string SourceProvenance = "Generation time is not a verification date; inventory source as-of date unavailable in these report inputs. Confirm source currency before acting.";

    public static string EvidenceNote(System.Collections.Generic.IEnumerable<PursuitBriefRow> rows)
    {
        var list = rows.ToList();
        var dates = list.Where(x => x.LastRefreshAtUtc.HasValue).Select(x => x.LastRefreshAtUtc!.Value).ToList();
        var datesNote = dates.Count == 0 ? "No stored honing timestamp available." :
            $"Stored honing timestamps: {dates.Min():yyyy-MM-dd} to {dates.Max():yyyy-MM-dd}; {dates.Count}/{list.Count} dated (may include legacy cache touches).";
        return SourceProvenance + " " + datesNote + " " +
            string.Join(" ", list.Select(x => x.RefreshNote).Distinct());
    }
    public static string CostCoverage(int? costed, int? population) =>
        costed.HasValue && population.HasValue
            ? $"{costed}/{population} projects costed; unknown costs excluded"
            : "cost coverage unavailable; unknown costs excluded";

    public static string Abridge(string value, int max) =>
        value.Length <= max ? value : value[..max] + "... [abridged]";
}
