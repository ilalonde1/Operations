#nullable enable
namespace Kor.Opportunities.Data.BdReports;

public static class BdReportFormatting
{
    public static string CostCoverage(int? costed, int? population) =>
        costed.HasValue && population.HasValue
            ? $"{costed}/{population} projects costed; unknown costs excluded"
            : "cost coverage unavailable; unknown costs excluded";

    public static string Abridge(string value, int max) =>
        value.Length <= max ? value : value[..max] + "... [abridged]";
}
